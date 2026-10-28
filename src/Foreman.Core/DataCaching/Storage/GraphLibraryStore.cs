using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.DataCaching.Storage {
    public sealed class GraphLibraryStore(string dbPath) {
        public IReadOnlyList<GraphRow> ListByLastOpened() {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            return [.. db.Graphs
                .OrderByDescending(g => g.LastOpenedAt)
                .Select(g => new GraphRow {
                    Id = g.Id, Name = g.Name, PresetName = g.PresetName, NodeCount = g.NodeCount,
                    CreatedAt = g.CreatedAt, ModifiedAt = g.ModifiedAt, LastOpenedAt = g.LastOpenedAt,
                    SavedAt = g.SavedAt, SourcePath = g.SourcePath,
                })];
        }

        public GraphRow? Get(Guid id) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            return db.Graphs.SingleOrDefault(g => g.Id == id);
        }

        public GraphRow Create(string name, string presetName, string data, int nodeCount, string? sourcePath, DateTime now) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            var row = new GraphRow {
                Id = Guid.NewGuid(), Name = name, PresetName = presetName, Data = data, NodeCount = nodeCount,
                SourcePath = sourcePath, CreatedAt = now, ModifiedAt = now, LastOpenedAt = now,
            };
            db.Graphs.Add(row);
            db.SaveChanges();
            return row;
        }

        public GraphRow? FindImport(string sourcePath, string data) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            return db.Graphs.FirstOrDefault(g => g.SourcePath == sourcePath && g.Data == data);
        }

        public void WriteAutosave(Guid id, string data, int nodeCount, byte[]? thumbnail, DateTime now) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            GraphRow row = db.Graphs.Single(g => g.Id == id);
            row.Data = data;
            row.NodeCount = nodeCount;
            row.ModifiedAt = now;
            if (thumbnail is not null)
                row.Thumbnail = thumbnail;
            db.SaveChanges();
        }

        public GraphVersionRow AddVersion(Guid id, GraphVersionKind kind, DateTime now) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            GraphRow row = db.Graphs.Single(g => g.Id == id);
            GraphVersionRow version = NewVersion(row, kind, now);
            db.GraphVersions.Add(version);
            if (kind == GraphVersionKind.Saved)
                row.SavedAt = now;
            db.SaveChanges();
            return version;
        }

        public IReadOnlyList<GraphVersionRow> Versions(Guid id) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            return [.. db.GraphVersions.Where(v => v.GraphId == id).OrderByDescending(v => v.CreatedAt)];
        }

        public void Restore(Guid id, Guid versionId, DateTime now) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            GraphRow row = db.Graphs.Single(g => g.Id == id);
            GraphVersionRow version = db.GraphVersions.Single(v => v.Id == versionId && v.GraphId == id);
            db.GraphVersions.Add(NewVersion(row, GraphVersionKind.BeforeRestore, now));
            row.Data = version.Data;
            row.NodeCount = version.NodeCount;
            row.ModifiedAt = now;
            db.SaveChanges();
        }

        public void DeleteVersion(Guid versionId) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            db.GraphVersions.Where(v => v.Id == versionId).ExecuteDelete();
        }

        public void MarkOpened(Guid id, DateTime now) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            db.Graphs.Where(g => g.Id == id).ExecuteUpdate(s => s.SetProperty(g => g.LastOpenedAt, now));
        }

        public void Rename(Guid id, string name) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            db.Graphs.Where(g => g.Id == id).ExecuteUpdate(s => s.SetProperty(g => g.Name, name));
        }

        public GraphRow Duplicate(Guid id, string name, DateTime now) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            GraphRow source = db.Graphs.Single(g => g.Id == id);
            var copy = new GraphRow {
                Id = Guid.NewGuid(), Name = name, PresetName = source.PresetName, Data = source.Data,
                NodeCount = source.NodeCount, Thumbnail = source.Thumbnail,
                CreatedAt = now, ModifiedAt = now, LastOpenedAt = now,
            };
            db.Graphs.Add(copy);
            db.SaveChanges();
            return copy;
        }

        public void SetPreset(Guid id, string presetName) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            db.Graphs.Where(g => g.Id == id).ExecuteUpdate(s => s.SetProperty(g => g.PresetName, presetName));
        }

        public void Delete(Guid id) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            db.GraphVersions.Where(v => v.GraphId == id).ExecuteDelete();
            db.Graphs.Where(g => g.Id == id).ExecuteDelete();
        }

        public void ThinAutomaticVersions(Guid id, DateTime now) {
            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            GraphVersionRow[] all = [.. db.GraphVersions.Where(v => v.GraphId == id)];
            HashSet<Guid> keep = [.. GraphVersionThinning.ToKeep(all, now).Select(v => v.Id)];
            db.GraphVersions.RemoveRange(all.Where(v => !keep.Contains(v.Id)));
            db.SaveChanges();
        }

        private static GraphVersionRow NewVersion(GraphRow row, GraphVersionKind kind, DateTime now) => new() {
            Id = Guid.NewGuid(), GraphId = row.Id, CreatedAt = now, Kind = kind, NodeCount = row.NodeCount, Data = row.Data,
        };
    }
}
