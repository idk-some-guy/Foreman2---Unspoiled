using Foreman.DataCaching.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForemanTest.Storage {
    [TestClass]
    public class GraphLibraryStoreTests {
        private static readonly DateTime T0 = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
        private string tempRoot = "";
        private GraphLibraryStore store = null!;

        [TestInitialize]
        public void Open() {
            tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
            store = new GraphLibraryStore(StoragePaths.DbPath(tempRoot));
        }

        [TestCleanup]
        public void Delete() {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        [TestMethod]
        public void Create_StoresTheRowWithAllThreeTimesAndNoSavedAt() {
            GraphRow row = store.Create("Plant", "Base", "{\"a\":1}", 3, "/x/plant.fjson", T0);

            GraphRow? read = store.Get(row.Id);

            Assert.IsNotNull(read);
            Assert.AreEqual("Plant", read.Name);
            Assert.AreEqual("Base", read.PresetName);
            Assert.AreEqual("{\"a\":1}", read.Data);
            Assert.AreEqual(3, read.NodeCount);
            Assert.AreEqual("/x/plant.fjson", read.SourcePath);
            Assert.AreEqual(T0, read.CreatedAt);
            Assert.AreEqual(T0, read.ModifiedAt);
            Assert.AreEqual(T0, read.LastOpenedAt);
            Assert.IsNull(read.SavedAt);
        }

        [TestMethod]
        public void ListByLastOpened_NewestFirst_WithoutDataOrThumbnail() {
            GraphRow a = store.Create("A", "p", "data-a", 1, null, T0);
            GraphRow b = store.Create("B", "p", "data-b", 1, null, T0.AddMinutes(1));
            store.WriteAutosave(a.Id, "data-a2", 2, [1, 2, 3], T0.AddMinutes(2));
            store.MarkOpened(a.Id, T0.AddMinutes(3));

            IReadOnlyList<GraphRow> list = store.ListByLastOpened();

            CollectionAssert.AreEqual(new[] { "A", "B" }, list.Select(r => r.Name).ToArray());
            Assert.IsTrue(list.All(r => r.Data == "" && r.Thumbnail is null));
            Assert.AreEqual(2, list[0].NodeCount);
        }

        [TestMethod]
        public void WriteAutosave_UpdatesDataNodeCountThumbnailAndModifiedAt() {
            GraphRow row = store.Create("A", "p", "old", 1, null, T0);

            store.WriteAutosave(row.Id, "new", 7, [9, 9], T0.AddSeconds(2));

            GraphRow read = store.Get(row.Id)!;
            Assert.AreEqual("new", read.Data);
            Assert.AreEqual(7, read.NodeCount);
            CollectionAssert.AreEqual(new byte[] { 9, 9 }, read.Thumbnail);
            Assert.AreEqual(T0.AddSeconds(2), read.ModifiedAt);
            Assert.AreEqual(T0, read.LastOpenedAt);
        }

        [TestMethod]
        public void WriteAutosave_NullThumbnail_KeepsTheStoredOne() {
            GraphRow row = store.Create("A", "p", "old", 1, null, T0);
            store.WriteAutosave(row.Id, "one", 1, [4], T0.AddSeconds(1));

            store.WriteAutosave(row.Id, "two", 1, null, T0.AddSeconds(2));

            CollectionAssert.AreEqual(new byte[] { 4 }, store.Get(row.Id)!.Thumbnail);
        }

        [TestMethod]
        public void FindImport_MatchesOnlySamePathAndSameContent() {
            GraphRow row = store.Create("A", "p", "content", 1, "/x/a.fjson", T0);

            Assert.AreEqual(row.Id, store.FindImport("/x/a.fjson", "content")!.Id);
            Assert.IsNull(store.FindImport("/x/a.fjson", "edited"));
            Assert.IsNull(store.FindImport("/y/a.fjson", "content"));
        }

        [TestMethod]
        public void AddVersion_Saved_CopiesCurrentDataAndSetsSavedAt() {
            GraphRow row = store.Create("A", "p", "d1", 4, null, T0);

            GraphVersionRow version = store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(5));

            Assert.AreEqual(GraphVersionKind.Saved, version.Kind);
            Assert.AreEqual("d1", version.Data);
            Assert.AreEqual(4, version.NodeCount);
            Assert.AreEqual(T0.AddMinutes(5), store.Get(row.Id)!.SavedAt);
        }

        [TestMethod]
        public void Versions_AreNewestFirst() {
            GraphRow row = store.Create("A", "p", "d1", 1, null, T0);
            store.AddVersion(row.Id, GraphVersionKind.Automatic, T0.AddMinutes(1));
            store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(3));
            store.AddVersion(row.Id, GraphVersionKind.Automatic, T0.AddMinutes(2));

            IReadOnlyList<GraphVersionRow> versions = store.Versions(row.Id);

            CollectionAssert.AreEqual(
                new[] { T0.AddMinutes(3), T0.AddMinutes(2), T0.AddMinutes(1) },
                versions.Select(v => v.CreatedAt).ToArray());
        }

        [TestMethod]
        public void Restore_RecordsOneBeforeRestoreFirst_ThenLoadsTheVersion() {
            GraphRow row = store.Create("A", "p", "first", 1, null, T0);
            GraphVersionRow saved = store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(1));
            store.WriteAutosave(row.Id, "second", 2, null, T0.AddMinutes(2));

            store.Restore(row.Id, saved.Id, T0.AddMinutes(3));

            GraphRow read = store.Get(row.Id)!;
            Assert.AreEqual("first", read.Data);
            Assert.AreEqual(1, read.NodeCount);
            Assert.AreEqual(T0.AddMinutes(3), read.ModifiedAt);
            GraphVersionRow[] before = [.. store.Versions(row.Id).Where(v => v.Kind == GraphVersionKind.BeforeRestore)];
            Assert.AreEqual(1, before.Length);
            Assert.AreEqual("second", before[0].Data);
            Assert.AreEqual(T0.AddMinutes(3), before[0].CreatedAt);
        }

        [TestMethod]
        public void DeleteVersion_RemovesOnlyThatVersion() {
            GraphRow row = store.Create("A", "p", "d", 1, null, T0);
            GraphVersionRow a = store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(1));
            GraphVersionRow b = store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(2));

            store.DeleteVersion(a.Id);

            CollectionAssert.AreEqual(new[] { b.Id }, store.Versions(row.Id).Select(v => v.Id).ToArray());
        }

        [TestMethod]
        public void Delete_CascadesToVersions() {
            GraphRow row = store.Create("A", "p", "d", 1, null, T0);
            store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(1));

            store.Delete(row.Id);

            Assert.IsNull(store.Get(row.Id));
            Assert.AreEqual(0, store.Versions(row.Id).Count);
        }

        [TestMethod]
        public void RenameAndSetPreset_Persist() {
            GraphRow row = store.Create("A", "p", "d", 1, null, T0);

            store.Rename(row.Id, "B");
            store.SetPreset(row.Id, "Other");

            GraphRow read = store.Get(row.Id)!;
            Assert.AreEqual("B", read.Name);
            Assert.AreEqual("Other", read.PresetName);
        }

        [TestMethod]
        public void MarkOpened_MovesTheRowToTheTopOfTheList() {
            GraphRow a = store.Create("A", "p", "d", 1, null, T0);
            store.Create("B", "p", "d", 1, null, T0.AddMinutes(1));

            store.MarkOpened(a.Id, T0.AddMinutes(2));

            Assert.AreEqual("A", store.ListByLastOpened()[0].Name);
        }

        [TestMethod]
        public void Duplicate_CopiesDataUnderTheNewNameWithoutVersionsOrSavedAt() {
            GraphRow row = store.Create("A", "Base", "payload", 5, "/x/a.fjson", T0);
            store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(1));

            GraphRow copy = store.Duplicate(row.Id, "A copy", T0.AddMinutes(2));

            Assert.AreNotEqual(row.Id, copy.Id);
            GraphRow read = store.Get(copy.Id)!;
            Assert.AreEqual("A copy", read.Name);
            Assert.AreEqual("Base", read.PresetName);
            Assert.AreEqual("payload", read.Data);
            Assert.AreEqual(5, read.NodeCount);
            Assert.IsNull(read.SavedAt);
            Assert.AreEqual(T0.AddMinutes(2), read.CreatedAt);
            Assert.AreEqual(0, store.Versions(copy.Id).Count);
        }

        [TestMethod]
        public void ThinAutomaticVersions_DropsSupersededAndExpiredAutomaticOnly() {
            DateTime now = T0.AddDays(40);
            GraphRow row = store.Create("A", "p", "d", 1, null, T0);
            store.AddVersion(row.Id, GraphVersionKind.Saved, T0.AddMinutes(1));
            store.AddVersion(row.Id, GraphVersionKind.Automatic, T0.AddMinutes(2));
            store.AddVersion(row.Id, GraphVersionKind.Automatic, now.AddMinutes(-10));
            store.AddVersion(row.Id, GraphVersionKind.Automatic, now.AddMinutes(-20));

            store.ThinAutomaticVersions(row.Id, now);

            IReadOnlyList<GraphVersionRow> left = store.Versions(row.Id);
            Assert.AreEqual(2, left.Count);
            Assert.AreEqual(GraphVersionKind.Automatic, left[0].Kind);
            Assert.AreEqual(now.AddMinutes(-10), left[0].CreatedAt);
            Assert.AreEqual(GraphVersionKind.Saved, left[1].Kind);
        }
    }
}
