using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;

namespace Foreman.Mac.Services {
    internal readonly record struct GraphSnapshot(string Data, int NodeCount, byte[]? Thumbnail);

    internal sealed class GraphLibraryService(GraphLibraryStore store, TimeProvider time) : IDisposable {
        internal static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan AutomaticInterval = TimeSpan.FromMinutes(30);
        internal const int RecentLimit = 10;
        internal const string UntitledName = "Untitled Graph";

        private ITimer? timer;
        private bool pending;
        private bool failureReported;
        private DateTime nextAutomaticAt;

        public Guid? OpenGraphId { get; private set; }
        public string? OpenGraphName { get; private set; }
        public DateTime? OpenGraphSavedAt { get; private set; }

        public Func<GraphSnapshot?>? Snapshot { get; set; }
        public Action<Action> Post { get; set; } = static action => action();

        public event EventHandler<string>? WriteFailed;

        public DateTime Now => time.GetUtcNow().UtcDateTime;

        public void NotifyChanged() {
            if (OpenGraphId is null)
                return;
            pending = true;
            timer ??= time.CreateTimer(_ => Post(FlushNow), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }

        public void FlushNow() {
            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (pending)
                Write();
        }

        public bool Save() {
            if (OpenGraphId is not Guid id || !Write())
                return false;
            DateTime now = Now;
            store.AddVersion(id, GraphVersionKind.Saved, now);
            store.ThinAutomaticVersions(id, now);
            OpenGraphSavedAt = now;
            return true;
        }

        public GraphRow Create(string name, string presetName, string data, int nodeCount, string? sourcePath) {
            FlushNow();
            GraphRow row = store.Create(name, presetName, data, nodeCount, sourcePath, Now);
            Attach(row);
            return row;
        }

        public GraphRow? Open(Guid id) {
            FlushNow();
            GraphRow? row = store.Get(id);
            if (row is null)
                return null;
            DateTime now = Now;
            store.MarkOpened(id, now);
            Attach(row);
            IReadOnlyList<GraphVersionRow> versions = store.Versions(id);
            bool changed = versions.Count == 0 ? row.NodeCount > 0 : versions[0].Data != row.Data;
            if (changed) {
                store.AddVersion(id, GraphVersionKind.Automatic, now);
                store.ThinAutomaticVersions(id, now);
            }
            return row;
        }

        public GraphRow? Find(Guid id) => store.Get(id);

        public void Resume(GraphRow row) => Attach(row);

        public GraphRow? FindImport(string sourcePath, string data) => store.FindImport(sourcePath, data);

        public void Detach() {
            FlushNow();
            OpenGraphId = null;
            OpenGraphName = null;
            OpenGraphSavedAt = null;
            pending = false;
        }

        public GraphRow Duplicate(string name) {
            FlushNow();
            GraphRow copy = store.Duplicate(OpenGraphId ?? throw new InvalidOperationException("No graph is open."), name, Now);
            Attach(copy);
            return copy;
        }

        public void Rename(string name) {
            if (OpenGraphId is not Guid id)
                return;
            store.Rename(id, name);
            OpenGraphName = name;
        }

        public void SetPreset(string presetName) {
            if (OpenGraphId is Guid id)
                store.SetPreset(id, presetName);
        }

        public IReadOnlyList<GraphVersionRow> Versions() =>
            OpenGraphId is Guid id ? store.Versions(id) : [];

        public GraphRow? Restore(Guid versionId) {
            if (OpenGraphId is not Guid id)
                return null;
            FlushNow();
            store.Restore(id, versionId, Now);
            return store.Get(id);
        }

        public void DeleteVersion(Guid versionId) => store.DeleteVersion(versionId);

        public void Delete(Guid id) {
            if (OpenGraphId == id) {
                OpenGraphId = null;
                OpenGraphName = null;
                OpenGraphSavedAt = null;
                pending = false;
            }
            store.Delete(id);
        }

        public IReadOnlyList<GraphRow> All() => store.ListByLastOpened();

        public IReadOnlyList<GraphRow> OpenRecent(DateTime? clearedAt) =>
            [.. store.ListByLastOpened().Where(r => clearedAt is null || r.LastOpenedAt > clearedAt).Take(RecentLimit)];

        public string NextUntitledName() {
            HashSet<string> taken = [.. store.ListByLastOpened().Select(r => r.Name)];
            if (!taken.Contains(UntitledName))
                return UntitledName;
            for (int n = 2; ; n++) {
                string candidate = $"{UntitledName} {n}";
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        public static bool IsEditedSinceSave(GraphRow row) =>
            row.ModifiedAt > (row.SavedAt ?? row.CreatedAt);

        public void Dispose() {
            timer?.Dispose();
            timer = null;
        }

        private void Attach(GraphRow row) {
            OpenGraphId = row.Id;
            OpenGraphName = row.Name;
            OpenGraphSavedAt = row.SavedAt;
            pending = false;
            nextAutomaticAt = Now + AutomaticInterval;
        }

        private bool Write() {
            if (OpenGraphId is not Guid id || Snapshot?.Invoke() is not GraphSnapshot snapshot)
                return false;
            DateTime now = Now;
            try {
                store.WriteAutosave(id, snapshot.Data, snapshot.NodeCount, snapshot.Thumbnail, now);
                if (now >= nextAutomaticAt) {
                    store.AddVersion(id, GraphVersionKind.Automatic, now);
                    store.ThinAutomaticVersions(id, now);
                    nextAutomaticAt = now + AutomaticInterval;
                }
            } catch (Exception ex) when (ex is DbException or IOException or UnauthorizedAccessException) {
                ErrorLogging.LogException(ex, "Error autosaving the graph");
                if (!failureReported) {
                    failureReported = true;
                    WriteFailed?.Invoke(this, OpenGraphName ?? UntitledName);
                }
                return false;
            }
            pending = false;
            return true;
        }
    }
}
