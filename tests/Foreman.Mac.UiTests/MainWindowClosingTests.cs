using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Canvas;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DrawingPoint = System.Drawing.Point;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class MainWindowClosingTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        [AvaloniaFact]
        public async Task Close_WithChangesSinceSave_ShowsNoAlert_RowDataEqualsTheClosedGraph() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
            using var library = new LibraryFixture();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.Library = library.Service;
            window.ApplyLoadedSettings();
            window.SaveVersion();
            Guid id = library.Service.OpenGraphId!.Value;

            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);
            Assert.True(window.ViewModel.IsDirty);
            string expected = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(window.GraphCanvas.Viewer, cache), writeIndented: true);

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.IsVisible);
            Assert.Empty(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
            Assert.Equal(expected, library.Store.Get(id)!.Data);
            Assert.Equal(1, library.Store.Get(id)!.NodeCount);
        }

        [AvaloniaFact]
        public void Close_WithAPlacementFlashASheetAndThePausedCapsuleInFlight_DetachesWithoutThrowing() {
            using var library = new LibraryFixture();
            ShellFixture shell = ShellFixture.Open();
            MainWindow window = shell.Window;
            window.Library = library.Service;
            window.OpenBrowser(new AddRequest(new DrawingPoint(100, 100), AddScope.Items, SeedText: "iron plate"));
            shell.BrowserVm.SelectCell(shell.BrowserVm.Sections[0].Cells[0]);
            shell.BrowserVm.Place(NodeType.Supplier);
            Assert.NotNull(window.GraphCanvas.PlacementFlashNode);
            _ = Dialogs.ShowWarningAsync(window, "Headline", "Message");
            window.GraphCanvas.Viewer.IsPaused = true;
            Dispatcher.UIThread.RunJobs();

            window.Close();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
        }
    }
}
