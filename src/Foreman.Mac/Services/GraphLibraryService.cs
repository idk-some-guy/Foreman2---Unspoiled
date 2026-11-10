using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Foreman.Mac.Canvas;
using SkiaSharp;

namespace Foreman.Mac.Services {
    internal readonly record struct GraphSnapshot(string Data, int NodeCount, SKPicture? Thumbnail);

    internal sealed class GraphLibraryService(GraphLibraryStore store, TimeProvider time) : IDisposable {
        internal static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan FlushWait = TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan AutomaticInterval = TimeSpan.FromMinutes(30);
        internal const int RecentLimit = 10;
        internal const string UntitledName = "Untitled Graph";

        private readonly HashSet<Guid> blank = [];
        private ITimer? timer;
        private bool pending;
        private bool failureReported;
        private DateTime nextAutomaticAt;
        private readonly object gate = new();
        private Task writeChain = Task.CompletedTask;
        private WriteJob? waiting;
        private readonly HashSet<Guid> deleted = [];
        private long changeCount;

        private sealed class WriteJob(Guid id, string? name, GraphSnapshot snapshot, long changeStamp) {
            public Guid Id { get; } = id;
            public string? Name { get; } = name;
            public GraphSnapshot Snapshot { get; set; } = snapshot;
            public long ChangeStamp { get; set; } = changeStamp;
            public bool RecordSaved { get; set; }
        }

        public Guid? OpenGraphId { get; private set; }
        public string? OpenGraphName { get; private set; }
        public DateTime? OpenGraphSavedAt { get; private set; }

        public Func<GraphSnapshot?>? Snapshot { get; set; }
        public Action<Action> Post { get; set; } = static action => action();
        public TaskScheduler Worker { get; set; } = TaskScheduler.Default;

        public event EventHandler<string>? WriteFailed;

        public event EventHandler<bool>? Saved;

        public DateTime Now => time.GetUtcNow().UtcDateTime;

        public void NotifyChanged() {
            if (OpenGraphId is null)
                return;
            pending = true;
            changeCount++;
            timer ??= time.CreateTimer(_ => Post(FlushNow), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }

        public void FlushNow() {
            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (pending)
                Enqueue();
        }

        public bool FlushAndWait() {
            FlushNow();
            return Drain();
        }

        public bool Save() {
            if (OpenGraphId is null)
                return false;
            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return Enqueue(recordSaved: true);
        }

        public GraphRow CreateBlank(string name, string presetName, string data) {
            GraphRow row = Create(name, presetName, data, 0, null);
            blank.Add(row.Id);
            return row;
        }

        public GraphRow Create(string name, string presetName, string data, int nodeCount, string? sourcePath) {
            FlushAndWait();
            GraphRow row = store.Create(name, presetName, data, nodeCount, sourcePath, Now);
            Attach(row);
            return row;
        }

        public GraphRow? Open(Guid id) {
            FlushAndWait();
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
            FlushAndWait();
            OpenGraphId = null;
            OpenGraphName = null;
            OpenGraphSavedAt = null;
            pending = false;
        }

        public void PruneUntouched() {
            FlushAndWait();
            if (OpenGraphId is Guid id && blank.Contains(id) && store.Get(id) is { } row && IsUntouched(row))
                store.Delete(id);
        }

        public static bool IsUntouched(GraphRow row) =>
            row.NodeCount == 0 && row.SavedAt is null && row.SourcePath is null && row.ModifiedAt == row.CreatedAt;

        public GraphRow Duplicate(string name) {
            FlushAndWait();
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

        public GraphRow DuplicateGraph(Guid id, string name) => store.Duplicate(id, name, Now);

        public void RenameGraph(Guid id, string name) {
            store.Rename(id, name);
            if (OpenGraphId == id)
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
            FlushAndWait();
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
            lock (gate)
                deleted.Add(id);
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
            lock (gate)
                waiting = null;
        }

        private void Attach(GraphRow row) {
            lock (gate)
                nextAutomaticAt = Now + AutomaticInterval;
            OpenGraphId = row.Id;
            OpenGraphName = row.Name;
            OpenGraphSavedAt = row.SavedAt;
            pending = false;
        }

        private bool Drain() {
            Task chain;
            lock (gate)
                chain = writeChain;
            try {
                return chain.Wait(FlushWait);
            } catch (AggregateException) {
                return true;
            }
        }

        private bool Enqueue(bool recordSaved = false) {
            if (OpenGraphId is not Guid id || Snapshot?.Invoke() is not GraphSnapshot snapshot)
                return false;
            pending = false;
            lock (gate) {
                if (waiting is { } queued && queued.Id == id) {
                    queued.Snapshot.Thumbnail?.Dispose();
                    queued.Snapshot = snapshot;
                    queued.ChangeStamp = changeCount;
                    queued.RecordSaved |= recordSaved;
                    return true;
                }
                var job = new WriteJob(id, OpenGraphName, snapshot, changeCount) { RecordSaved = recordSaved };
                waiting = job;
                writeChain = writeChain.ContinueWith(_ => Run(job), CancellationToken.None, TaskContinuationOptions.None, Worker);
            }
            return true;
        }

        private void Run(WriteJob job) {
            GraphSnapshot snapshot;
            lock (gate) {
                if (ReferenceEquals(waiting, job))
                    waiting = null;
                snapshot = job.Snapshot;
                if (deleted.Contains(job.Id)) {
                    snapshot.Thumbnail?.Dispose();
                    return;
                }
            }
            DateTime now = Now;
            try {
                byte[]? thumbnail = snapshot.Thumbnail is { } picture ? GraphThumbnail.Encode(picture) : null;
                store.WriteAutosave(job.Id, snapshot.Data, snapshot.NodeCount, thumbnail, now);
                DateTime due;
                lock (gate)
                    due = nextAutomaticAt;
                if (now >= due) {
                    store.AddVersion(job.Id, GraphVersionKind.Automatic, now);
                    store.ThinAutomaticVersions(job.Id, now);
                    lock (gate)
                        nextAutomaticAt = now + AutomaticInterval;
                }
                if (job.RecordSaved) {
                    store.AddVersion(job.Id, GraphVersionKind.Saved, now);
                    store.ThinAutomaticVersions(job.Id, now);
                    Post(() => OnSaved(job, now));
                }
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Error autosaving the graph");
                Post(() => OnWriteFailed(job));
            } finally {
                snapshot.Thumbnail?.Dispose();
            }
        }

        private void OnSaved(WriteJob job, DateTime savedAt) {
            if (OpenGraphId != job.Id)
                return;
            OpenGraphSavedAt = savedAt;
            Saved?.Invoke(this, job.ChangeStamp == changeCount);
        }

        private void OnWriteFailed(WriteJob job) {
            lock (gate)
                if (deleted.Contains(job.Id))
                    return;
            if (OpenGraphId == job.Id)
                pending = true;
            if (failureReported)
                return;
            failureReported = true;
            WriteFailed?.Invoke(this, job.Name ?? UntitledName);
        }
    }
}
