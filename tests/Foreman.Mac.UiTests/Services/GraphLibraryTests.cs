using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using Foreman.Serialization;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class GraphLibraryTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        private static async Task<MainWindow> NewWindowAsync(LibraryFixture library) {
            DataCache cache = await GetCacheAsync();
            MainWindow window = library.Track(new MainWindow());
            window.Show();
            window.GraphCanvas.Viewport.SetSize(800, 800);
            window.DataCache = cache;
            window.Settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: library.NewTempDir());
            window.SettingsService = new SettingsService(library.NewTempDir());
            window.Library = library.Service;
            window.ApplyLoadedSettings();
            return window;
        }

        private static ItemQualityPair Pair(DataCache cache, string item) => new(cache.Items[item], cache.DefaultQuality!);

        private static string Serialize(MainWindow window) =>
            GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(window.GraphCanvas.Viewer, window.DataCache!), writeIndented: true);

        [AvaloniaFact]
        public async Task EditThenTwoSecondsOnTheTestClock_DataRoundTrips_ModifiedAtAdvances() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.SaveVersion();
            Guid id = library.Service.OpenGraphId!.Value;
            DateTime before = library.Store.Get(id)!.ModifiedAt;

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();

            GraphRow row = library.Store.Get(id)!;
            Assert.Equal(Serialize(window), row.Data);
            Assert.NotNull(GraphSaveCodec.ReadViewer(row.Data));
            Assert.Equal(1, row.NodeCount);
            Assert.True(row.ModifiedAt > before);
        }

        [AvaloniaFact]
        public async Task SecondEditWithinTwoSeconds_CoalescesIntoOneWrite() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.SaveVersion();
            int writes = 0;
            Func<GraphSnapshot?> inner = library.Service.Snapshot!;
            library.Service.Snapshot = () => { writes++; return inner(); };
            DataCache cache = window.DataCache!;

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "iron-plate"), System.Drawing.Point.Empty);
            library.Clock.Advance(TimeSpan.FromSeconds(1));
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "copper-plate"), new System.Drawing.Point(300, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(1.5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, writes);

            library.Clock.Advance(TimeSpan.FromSeconds(1));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, writes);
            Assert.Equal(2, library.Store.Get(library.Service.OpenGraphId!.Value)!.NodeCount);
        }

        [AvaloniaFact]
        public async Task OpeningAGraphAndLeavingItAlone_WritesNothing() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            window.SaveVersion();
            Guid id = library.Service.OpenGraphId!.Value;
            await window.NewGraphAsync();
            DateTime modified = library.Store.Get(id)!.ModifiedAt;

            await window.OpenLibraryGraphAsync(id);
            library.Clock.Advance(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(modified, library.Store.Get(id)!.ModifiedAt);
            Assert.Single(window.GraphCanvas.Viewer.NodeElements);
            Assert.False(window.ViewModel.IsDirty);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "copper-plate"), new System.Drawing.Point(300, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            await window.NewGraphAsync();
            await window.OpenLibraryGraphAsync(id);

            Assert.Equal(2, window.GraphCanvas.Viewer.NodeElements.Count);
            Assert.True(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public async Task AppDeactivated_FlushesThePendingEditInsideTheDebounceWindow() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.SaveVersion();
            Guid id = library.Service.OpenGraphId!.Value;
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            library.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(0, library.Store.Get(id)!.NodeCount);

            window.OnAppDeactivated();

            GraphRow row = library.Store.Get(id)!;
            Assert.Equal(Serialize(window), row.Data);
            Assert.Equal(1, row.NodeCount);
        }

        [AvaloniaFact]
        public async Task OpeningAnotherGraphInsideTheDebounceWindow_FlushesTheEditedOne_AndLeavesTheOpenedOneAlone() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.SaveVersion();
            Guid a = library.Service.OpenGraphId!.Value;
            await window.NewGraphAsync();
            Guid b = library.Service.OpenGraphId!.Value;
            DateTime modifiedB = library.Store.Get(b)!.ModifiedAt;
            await window.OpenLibraryGraphAsync(a);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            string expected = Serialize(window);
            library.Clock.Advance(TimeSpan.FromSeconds(1));

            await window.OpenLibraryGraphAsync(b);

            GraphRow flushed = library.Store.Get(a)!;
            Assert.Equal(1, flushed.NodeCount);
            Assert.Equal(expected, flushed.Data);
            Assert.Equal(modifiedB, library.Store.Get(b)!.ModifiedAt);
            Assert.Equal(b, library.Service.OpenGraphId);
        }

        [AvaloniaFact]
        public async Task Autosave_WriteFailure_AlertsOnceWithTheRuledCopy_AndTheNextEditWritesOnceTheDatabaseIsBack() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.SaveVersion();
            Guid id = library.Service.OpenGraphId!.Value;
            string backup = Path.Combine(library.NewTempDir(), "library.bak");
            SqliteConnection.ClearAllPools();
            File.Move(library.DbPath, backup);
            Directory.CreateDirectory(library.DbPath);

            for (int i = 0; i < 3; i++) {
                window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), new System.Drawing.Point(i * 300, 0));
                library.Clock.Advance(TimeSpan.FromSeconds(2));
                Dispatcher.UIThread.RunJobs();
            }

            AlertSheet alert = Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
            Assert.Equal("Couldn’t save “Untitled Graph”.", alert.FrameControl.Headline);
            Assert.Equal("Foreman keeps trying. Check that your disk has free space.", alert.FrameControl.InformativeText);

            Directory.Delete(library.DbPath);
            File.Move(backup, library.DbPath);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "copper-plate"), new System.Drawing.Point(0, 300));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Serialize(window), library.Store.Get(id)!.Data);
            Assert.Equal(4, library.Store.Get(id)!.NodeCount);
            Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
        }

        [AvaloniaFact]
        public async Task Save_CreatesOneSavedVersion_AndClearsTheEditedDot() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            Assert.True(window.ViewModel.IsDirty);

            window.SaveVersion();

            Assert.False(window.ViewModel.IsDirty);
            GraphVersionRow saved = Assert.Single(library.Service.Versions(), v => v.Kind == GraphVersionKind.Saved);
            Assert.Equal(1, saved.NodeCount);
            Assert.NotNull(library.Store.Get(library.Service.OpenGraphId!.Value)!.SavedAt);
        }

        [AvaloniaFact]
        public async Task Restore_WhenTheVersionCannotBeLoaded_KeepsTheRowTheCanvasAndTheEditedDotAsTheyWere() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            window.SaveVersion();
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "copper-plate"), new System.Drawing.Point(300, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            Guid id = library.Service.OpenGraphId!.Value;
            string before = library.Store.Get(id)!.Data;
            library.Store.WriteAutosave(id, "{}", 0, null, library.Service.Now);
            Guid broken = library.Store.AddVersion(id, GraphVersionKind.Automatic, library.Service.Now).Id;
            library.Store.WriteAutosave(id, before, 2, null, library.Service.Now);
            Assert.True(window.ViewModel.IsDirty);

            Task restoring = window.RestoreVersionAsync(broken);
            Dispatcher.UIThread.RunJobs();
            AlertSheet alert = Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
            alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await restoring.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(before, library.Store.Get(id)!.Data);
            Assert.True(window.ViewModel.IsDirty);
            Assert.Equal(2, window.GraphCanvas.Viewer.NodeElements.Count);
            Assert.Equal(id, library.Service.OpenGraphId);
            Assert.DoesNotContain(library.Service.Versions(), v => v.Kind == GraphVersionKind.BeforeRestore);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-ore"), new System.Drawing.Point(600, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, library.Store.Get(id)!.NodeCount);
            Assert.NotEqual(before, library.Store.Get(id)!.Data);
        }

        [AvaloniaFact]
        public async Task BrowseVersions_DeleteAsksFirstWithCancelDefault_ConfirmedDeletes_RestoreAppliesTheChosenVersion() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            DataCache cache = window.DataCache!;
            string[] items = ["iron-plate", "copper-plate", "iron-ore"];
            for (int i = 0; i < items.Length; i++) {
                window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, items[i]), new System.Drawing.Point(i * 300, 0));
                library.Clock.Advance(TimeSpan.FromMinutes(1));
                window.SaveVersion();
            }
            SheetHost host = SheetHost.For(window);

            Task browsing = window.BrowseVersionsAsync();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            VersionBrowserSheet sheet = Assert.Single(host.GetVisualDescendants().OfType<VersionBrowserSheet>());
            Assert.Equal([3, 2, 1], sheet.Rows.Select(r => int.Parse(r.NodesText, System.Globalization.CultureInfo.InvariantCulture)));

            sheet.VersionListControl.SelectedIndex = 0;
            sheet.DeleteButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            AlertSheet alert = Assert.Single(host.GetVisualDescendants().OfType<AlertSheet>());
            Assert.Equal("Delete this version?", alert.FrameControl.Headline);
            Assert.Equal("You can’t undo this action.", alert.FrameControl.InformativeText);
            Assert.Equal(["Delete", "Cancel"], alert.Buttons!.Items.Cast<Avalonia.Controls.Button>().Select(b => (string)b.Content!));
            Assert.Same(alert.NoButtonControl, alert.Buttons.ResolveDefaultButton());
            alert.NoButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            Assert.Equal(3, sheet.Rows.Count);

            sheet.DeleteButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(host.GetVisualDescendants().OfType<AlertSheet>()).YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, sheet.Rows.Count);
            Assert.Equal(2, library.Service.Versions().Count(v => v.Kind == GraphVersionKind.Saved));

            sheet.VersionListControl.SelectedIndex = 1;
            sheet.RestoreButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            await browsing.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(window.GraphCanvas.Viewer.NodeElements);
            Assert.Single(library.Service.Versions(), v => v.Kind == GraphVersionKind.BeforeRestore);
        }

        [AvaloniaFact]
        public async Task Restore_RecordsOneBeforeRestoreFirst() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.SaveVersion();
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            library.Clock.Advance(TimeSpan.FromSeconds(3));
            Dispatcher.UIThread.RunJobs();
            NativeMenu file = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            NativeMenu revert = file.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Revert To").Menu!;

            revert.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Last Saved Version").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            GraphVersionRow before = Assert.Single(library.Service.Versions(), v => v.Kind == GraphVersionKind.BeforeRestore);
            Assert.Equal(1, before.NodeCount);
            Assert.Empty(window.GraphCanvas.Viewer.NodeElements);
            Assert.Equal(0, library.Store.Get(library.Service.OpenGraphId!.Value)!.NodeCount);
        }

        [AvaloniaFact]
        public async Task Restore_WhenTheNodesFailToImportAfterTheCanvasClears_PutsTheRowAndTheCanvasBack() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            window.SaveVersion();
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "copper-plate"), new System.Drawing.Point(300, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            Guid id = library.Service.OpenGraphId!.Value;
            string before = library.Store.Get(id)!.Data;
            Assert.True(window.ViewModel.IsDirty);
            string broken = System.Text.RegularExpressions.Regex.Replace(before, "\"BaseQuality\"\\s*:\\s*\"[^\"]*\"", "\"BaseQuality\": \"missing\"");
            Assert.NotEqual(before, broken);
            library.Store.WriteAutosave(id, broken, 2, null, library.Service.Now);
            Guid brokenVersion = library.Store.AddVersion(id, GraphVersionKind.Automatic, library.Service.Now).Id;
            library.Store.WriteAutosave(id, before, 2, null, library.Service.Now);

            Task restoring = window.RestoreVersionAsync(brokenVersion);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>()).YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await restoring.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(before, library.Store.Get(id)!.Data);
            Assert.Equal(2, window.GraphCanvas.Viewer.NodeElements.Count);
            Assert.True(window.ViewModel.IsDirty);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-ore"), new System.Drawing.Point(600, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, library.Store.Get(id)!.NodeCount);
        }

        [AvaloniaFact]
        public async Task Restore_WhenTheRowAndTheVersionBothFailToLoad_LeavesTheRowDetachedFromTheCanvas() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            window.SaveVersion();
            Guid id = library.Service.OpenGraphId!.Value;
            string broken = System.Text.RegularExpressions.Regex.Replace(library.Store.Get(id)!.Data, "\"BaseQuality\"\\s*:\\s*\"[^\"]*\"", "\"BaseQuality\": \"missing\"");
            library.Store.WriteAutosave(id, broken, 1, null, library.Service.Now);
            Guid brokenVersion = library.Store.AddVersion(id, GraphVersionKind.Automatic, library.Service.Now).Id;
            string before = library.Store.Get(id)!.Data;
            int count = library.Store.Get(id)!.NodeCount;

            Task restoring = window.RestoreVersionAsync(brokenVersion);
            for (int i = 0; i < 2; i++) {
                Dispatcher.UIThread.RunJobs();
                Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>()).YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle();
            }
            await restoring.WaitAsync(TimeSpan.FromSeconds(5));
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "copper-plate"), new System.Drawing.Point(300, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, library.Store.Get(id)!.Data);
            Assert.Equal(count, library.Store.Get(id)!.NodeCount);
        }

        [AvaloniaFact]
        public async Task OpeningAGraphSavedUnderAnotherPreset_RecordsTheActivePresetOnTheRow() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            GraphRow row = library.Service.Create("Plant", "Other Preset", Serialize(window), 0, null);
            library.Service.Detach();

            await window.OpenLibraryGraphAsync(row.Id);

            Assert.Equal(VanillaPresetName, library.Store.Get(row.Id)!.PresetName);
        }

        [Fact]
        public void AutomaticVersion_RecordedOnOpenWhenChanged_AndOnlyAtThirtyMinutesOfEditing() {
            using var library = new LibraryFixture();
            int n = 0;
            library.Service.Snapshot = () => new GraphSnapshot("data " + n, 1, null);
            GraphRow row = library.Service.Create("Plant", "p", "data 0", 1, null);
            library.Service.Detach();
            int Automatic() => library.Service.Versions().Count(v => v.Kind == GraphVersionKind.Automatic);

            library.Service.Open(row.Id);
            Assert.Equal(1, Automatic());
            library.Service.Detach();
            library.Service.Open(row.Id);
            Assert.Equal(1, Automatic());

            n = 1;
            library.Service.NotifyChanged();
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            library.Clock.Advance(TimeSpan.FromMinutes(105) - TimeSpan.FromSeconds(2));
            library.Service.Detach();
            library.Service.Open(row.Id);
            Assert.Equal(2, Automatic());

            n = 2;
            library.Clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(4));
            library.Service.NotifyChanged();
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(2, Automatic());

            n = 3;
            library.Service.NotifyChanged();
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(3, Automatic());
        }

        [AvaloniaFact]
        public async Task OpenTwice_SameFjson_YieldsOneRow() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            string json = Serialize(window);
            await window.NewGraphAsync();
            int baseline = library.Store.ListByLastOpened().Count;

            await window.OpenFjsonAsync(json, "/tmp/graphs/plant.fjson");
            await window.NewGraphAsync();
            await window.OpenFjsonAsync(json, "/tmp/graphs/plant.fjson");

            GraphRow[] imported = [.. library.Store.ListByLastOpened().Where(r => r.SourcePath == "/tmp/graphs/plant.fjson")];
            Assert.Single(imported);
            Assert.Equal("plant", imported[0].Name);
            Assert.Equal(baseline + 2, library.Store.ListByLastOpened().Count);
            Assert.Equal(imported[0].Id, library.Service.OpenGraphId);
            Assert.Equal("plant", window.ViewModel.DocumentTitle);
            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public async Task PreRoutingFixture_LinksLoadAtTheDefaultRouting() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            DataCache cache = window.DataCache!;
            ItemQualityPair iron = Pair(cache, "iron-plate");
            var editor = window.GraphCanvas.Viewer.Session.Editor;
            NodeId supplier = editor.CreateSupplierNode(iron, new System.Drawing.Point(-300, 0));
            NodeId consumer = editor.CreateConsumerNode(iron, new System.Drawing.Point(300, 0));
            editor.CreateLink(supplier, consumer, iron, LinkRouting.Rounded);
            JsonNode saved = JsonNode.Parse(Serialize(window))!;
            foreach (JsonNode? wire in saved["ProductionGraph"]!["NodeLinks"]!.AsArray()) {
                wire!.AsObject().Remove("Routing");
                wire.AsObject().Remove("Waypoints");
            }
            saved["Version"] = 8;
            await window.NewGraphAsync();
            window.GraphCanvas.Viewer.DefaultLinkRouting = LinkRouting.Orthogonal;

            await window.OpenFjsonAsync(saved.ToJsonString(), "/tmp/graphs/pre-routing.fjson");

            Assert.Single(window.GraphCanvas.Viewer.LinkElements);
            Assert.All(window.GraphCanvas.Viewer.LinkElements, l => {
                Assert.Equal(LinkRouting.Orthogonal, l.ViewModel.Routing);
                Assert.Empty(l.ViewModel.Waypoints);
            });
        }

        [AvaloniaFact]
        public async Task OpenFjson_Unreadable_ShowsTheOpenAlert_AndAddsNoRow() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            int baseline = library.Store.ListByLastOpened().Count;

            Task opening = window.OpenFjsonAsync("{}", "/tmp/graphs/broken.fjson");
            Dispatcher.UIThread.RunJobs();

            AlertSheet alert = Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
            Assert.Equal("Couldn’t open “broken”.", alert.FrameControl.Headline);
            alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await opening;
            Assert.Equal(baseline, library.Store.ListByLastOpened().Count);
        }

        [AvaloniaFact]
        public async Task Thumbnail_IsNullForAnEmptyGraph_ThenA60By60PngWithTheNodeDrawnOnTheCanvasBackground() {
            using var library = new LibraryFixture();
            MainWindow window = await NewWindowAsync(library);
            window.SaveVersion();
            Guid id = library.Service.OpenGraphId!.Value;
            Assert.Null(library.Store.Get(id)!.Thumbnail);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(window.DataCache!, "iron-plate"), System.Drawing.Point.Empty);
            window.SaveVersion();
            byte[]? png = library.Store.Get(id)!.Thumbnail;

            Assert.NotNull(png);
            using SKBitmap bitmap = SKBitmap.Decode(png);
            Assert.Equal(60, bitmap.Width);
            Assert.Equal(60, bitmap.Height);
            Assert.Equal(window.GraphCanvas.Viewer.BackgroundColor, bitmap.GetPixel(0, 0));
            Assert.Contains(Enumerable.Range(20, 20).SelectMany(x => Enumerable.Range(20, 20).Select(y => bitmap.GetPixel(x, y))),
                pixel => pixel != window.GraphCanvas.Viewer.BackgroundColor);
        }

        private static int snapshotNodes;

        private static void UseSnapshotNodes(LibraryFixture library) {
            snapshotNodes = 0;
            library.Service.Snapshot = () => new GraphSnapshot("{}", snapshotNodes, null);
            library.Service.Post = action => Dispatcher.UIThread.Post(action);
        }

        private static void Edit(LibraryFixture library) {
            snapshotNodes++;
            library.Service.NotifyChanged();
        }

        [AvaloniaFact]
        public void TwoEditsInsideTheDebounce_QueueOneWrite_TheWorkerWritesOnceWithBothNodes() {
            using var library = new LibraryFixture();
            UseSnapshotNodes(library);
            Guid id = library.Service.Create("a", VanillaPresetName, "{}", 0, null).Id;
            library.Worker.RunInline = false;

            Edit(library);
            Edit(library);
            library.Clock.Advance(GraphLibraryService.Debounce);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, library.Worker.Pending);
            Assert.Equal(0, library.Store.Get(id)!.NodeCount);
            library.Worker.RunAll();
            Assert.Equal(2, library.Store.Get(id)!.NodeCount);
        }

        [AvaloniaFact]
        public void ASnapshotQueuedWhileOneWaits_ReplacesIt() {
            using var library = new LibraryFixture();
            UseSnapshotNodes(library);
            Guid id = library.Service.Create("a", VanillaPresetName, "{}", 0, null).Id;
            library.Worker.RunInline = false;

            Edit(library);
            library.Service.FlushNow();
            Edit(library);
            library.Service.FlushNow();
            Assert.Equal(1, library.Worker.Pending);
            library.Worker.RunNext();

            Assert.Equal(2, library.Store.Get(id)!.NodeCount);
            Assert.Equal(0, library.Worker.Pending);
        }

        [AvaloniaFact]
        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        public void AWriteThatThrowsOnTheWorker_RaisesWriteFailedOnceOnTheUiThread_AndTheNextDebounceTriesAgainWithoutRaisingAgain() {
            using var library = new LibraryFixture();
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(library.NewTempDir());
            UseSnapshotNodes(library);
            Guid id = library.Service.Create("a", VanillaPresetName, "{}", 0, null).Id;
            int raised = 0;
            library.Service.WriteFailed += (_, _) => raised++;
            library.Worker.RunInline = false;
            SqliteConnection.ClearAllPools();
            File.SetUnixFileMode(library.DbPath, UnixFileMode.UserRead);

            Edit(library);
            library.Clock.Advance(GraphLibraryService.Debounce);
            Dispatcher.UIThread.RunJobs();
            library.Worker.RunAll();
            Assert.Equal(0, raised);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, raised);

            Edit(library);
            library.Clock.Advance(GraphLibraryService.Debounce);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, library.Worker.Pending);
            library.Worker.RunAll();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, raised);
            Assert.Equal(0, library.Store.Get(id)!.NodeCount);

            SqliteConnection.ClearAllPools();
            foreach (string file in Directory.GetFiles(library.Root, Path.GetFileName(library.DbPath) + "*"))
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Edit(library);
            library.Clock.Advance(GraphLibraryService.Debounce);
            Dispatcher.UIThread.RunJobs();
            library.Worker.RunAll();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, library.Store.Get(id)!.NodeCount);
            Assert.Equal(1, raised);
        }

        [AvaloniaFact]
        public void AWriteThatThrowsOutsideTheFilter_IsLogged_AlertsOnce_AndTheChainStillWritesTheNextGraph() {
            using var library = new LibraryFixture();
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(library.NewTempDir());
            UseSnapshotNodes(library);
            Guid id = library.Service.Create("a", VanillaPresetName, "{}", 0, null).Id;
            int raised = 0;
            library.Service.WriteFailed += (_, _) => raised++;
            library.Worker.RunInline = false;

            Edit(library);
            library.Service.FlushNow();
            library.Worker.RunInline = true;
            library.Store.Delete(id);
            library.Worker.RunAll();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(nameof(InvalidOperationException), File.ReadAllText(ErrorLogging.LogFilePath));
            Assert.Equal(1, raised);

            Guid next = library.Service.Create("b", VanillaPresetName, "{}", 0, null).Id;
            Edit(library);
            library.Service.FlushNow();
            library.Worker.RunAll();

            Assert.Equal(2, library.Store.Get(next)!.NodeCount);
        }

        [AvaloniaFact]
        public void SaveOverAWriteThatThrowsOutsideTheFilter_ReturnsFalse_AndRecordsNoVersion() {
            using var library = new LibraryFixture();
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(library.NewTempDir());
            UseSnapshotNodes(library);
            Guid id = library.Service.Create("a", VanillaPresetName, "{}", 0, null).Id;
            Edit(library);
            library.Store.Delete(id);

            Assert.False(library.Service.Save());
            Assert.Empty(library.Store.Versions(id));
        }

        [AvaloniaFact]
        public void FlushAndWait_WithAWorkerThatNeverRuns_ReturnsFalseWithinTheBound_WithoutBlockingTheDispatcher() {
            using var library = new LibraryFixture();
            UseSnapshotNodes(library);
            library.Service.Create("a", VanillaPresetName, "{}", 0, null);
            library.Worker.RunInline = false;
            Edit(library);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool drained = library.Service.FlushAndWait();
            watch.Stop();

            Assert.False(drained);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3.5), watch.Elapsed.ToString());
            bool ran = false;
            Dispatcher.UIThread.Post(() => ran = true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ran);
        }

        [AvaloniaFact]
        public void OpeningAnotherGraphAfterAnEdit_WaitsForThePreviousRowsWrite() {
            using var library = new LibraryFixture();
            UseSnapshotNodes(library);
            Guid previous = library.Service.Create("a", VanillaPresetName, "{}", 0, null).Id;
            Guid other = library.Store.Create("b", VanillaPresetName, "{}", 0, null, library.Service.Now).Id;
            library.Worker.RunInline = false;
            Edit(library);

            using var openEntered = new ManualResetEventSlim();
            Func<GraphSnapshot?> takeSnapshot = library.Service.Snapshot!;
            library.Service.Snapshot = () => {
                openEntered.Set();
                return takeSnapshot();
            };

            Task helper = Task.Run(() => {
                Assert.True(openEntered.Wait(TimeSpan.FromSeconds(5)), "Open never entered");
                while (library.Worker.Pending == 0)
                    Thread.Yield();
                library.Worker.RunAll();
            });
            library.Service.Open(other);
            int written = library.Store.Get(previous)!.NodeCount;
            helper.Wait();

            Assert.Equal(1, written);
            Assert.Equal(other, library.Service.OpenGraphId);
        }

        [AvaloniaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task OpeningAGraphInANewWindowAfterTheEditedOneWasClosed_ShowsItsNodesAndKeepsBothRows(bool reopenTheEditedOne) {
            using var library = new LibraryFixture();
            MainWindow first = await NewWindowAsync(library);
            first.Settings!.ShowWelcomeAtLaunch = false;
            DataCache cache = first.DataCache!;
            first.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "iron-plate"), System.Drawing.Point.Empty);
            first.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "copper-plate"), new System.Drawing.Point(300, 0));
            first.SaveVersion();
            Guid edited = library.Service.OpenGraphId!.Value;
            await first.NewGraphAsync();
            Guid other = library.Service.OpenGraphId!.Value;
            first.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "stone"), System.Drawing.Point.Empty);
            first.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "coal"), new System.Drawing.Point(300, 0));
            first.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "wood"), new System.Drawing.Point(600, 0));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            string otherData = library.Store.Get(other)!.Data;
            string editedData = library.Store.Get(edited)!.Data;
            await first.OpenLibraryGraphAsync(edited);
            first.GraphCanvas.Viewer.Graph.CreateSupplierNode(Pair(cache, "stone"), new System.Drawing.Point(0, 300));
            library.Clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            editedData = library.Store.Get(edited)!.Data;
            ShellContext context = first.ShellContext!;
            first.Close();

            MainWindow second = ShellBootstrapper.CreateWindow(context);
            library.Track(second);
            Guid target = reopenTheEditedOne ? edited : other;
            string targetData = reopenTheEditedOne ? editedData : otherData;
            await second.OpenLibraryGraphAsync(target);
            Dispatcher.UIThread.RunJobs();
            library.Clock.Advance(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(library.Store.Get(target)!.NodeCount, second.GraphCanvas.Viewer.NodeElements.Count);
            Assert.NotEmpty(second.GraphCanvas.Viewer.NodeElements);
            Assert.Equal(targetData, library.Store.Get(target)!.Data);
            Assert.Equal(editedData, library.Store.Get(edited)!.Data);
            Assert.Equal(otherData, library.Store.Get(other)!.Data);
        }
    }
}
