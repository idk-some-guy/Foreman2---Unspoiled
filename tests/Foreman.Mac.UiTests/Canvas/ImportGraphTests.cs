using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Covers io-reference.md §2's Import Graph merge algorithm (phase 6 Task 2): ReadGraphPayload's
    //dual-accept (bare fragment or full viewer save), the centroid/grid-align/selection algorithm shared
    //with NodeClipboard's paste path (upstream ProductionGraphViewer.cs:1311-1363), and MainWindow's
    //no-dirty-check wiring.
    public class ImportGraphTests {
        private const int Half = 250;
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

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }
        }

        private static Fixture NewFixture(DataCache cache) {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            control.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            control.Viewer.Context.DCache = cache;
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();
            return new Fixture { Cache = cache, Control = control, Window = window };
        }

        private static RecipeNodeElement AddRecipeNode(Fixture fx, string recipeName, Point location) {
            IRecipe recipe = fx.Cache.Recipes[recipeName];
            var pair = new RecipeQualityPair(recipe, fx.Cache.DefaultQuality!);
            fx.Control.Viewer.Graph.CreateRecipeNode(pair, location);
            var viewModel = fx.Control.Viewer.Session.View.Nodes.OfType<IRecipeNodeViewModel>().Last();
            return (RecipeNodeElement)fx.Control.Viewer.NodeElementDictionary[viewModel.Id];
        }

        //---- dual-accept: bare fragment and full viewer save both import (reference §2) ------------------

        [AvaloniaFact]
        public async Task ImportNodesFromFragment_BareGraphFragment_ImportsIntoTarget() {
            DataCache cache = await GetCacheAsync();
            Fixture source = NewFixture(cache);
            RecipeNodeElement gear = AddRecipeNode(source, "iron-gear-wheel", new Point(60, 0));
            source.Control.Viewer.SetSelection([gear]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture(cache);
            int imported = target.Control.Viewer.ImportNodesFromFragment(cache, fragmentJson, new Point(0, 0), applySolverSettings: true);

            Assert.Equal(1, imported);
            Assert.Single(target.Control.Viewer.NodeElements);
        }

        [AvaloniaFact]
        public async Task ImportNodesFromFragment_FullViewerSave_AlsoImports() {
            DataCache cache = await GetCacheAsync();
            Fixture source = NewFixture(cache);
            AddRecipeNode(source, "iron-gear-wheel", new Point(60, 0));
            string viewerSaveJson = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(source.Control.Viewer, cache));

            Fixture target = NewFixture(cache);
            int imported = target.Control.Viewer.ImportNodesFromFragment(cache, viewerSaveJson, new Point(0, 0), applySolverSettings: true);

            Assert.Equal(1, imported);
            Assert.Single(target.Control.Viewer.NodeElements);
        }

        //---- centroid/grid-aligned rigid offset onto the viewport center (reference §2 step 2-4) ---------

        [AvaloniaFact]
        public async Task ImportNodesFromFragment_MultipleNodes_CentersCentroidOnOrigin_PreservingRelativeLayout() {
            DataCache cache = await GetCacheAsync();
            Fixture source = NewFixture(cache);
            RecipeNodeElement left = AddRecipeNode(source, "iron-gear-wheel", new Point(-120, 0));
            RecipeNodeElement right = AddRecipeNode(source, "iron-gear-wheel", new Point(120, 0));
            source.Control.Viewer.SetSelection([left, right]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture(cache);
            var origin = new Point(0, 500);

            target.Control.Viewer.ImportNodesFromFragment(cache, fragmentJson, origin, applySolverSettings: true);

            List<BaseNodeElement> imported = [.. target.Control.Viewer.NodeElements];
            Assert.Equal(2, imported.Count);
            double centroidX = imported.Average(n => n.ViewModel.Location.X);
            double centroidY = imported.Average(n => n.ViewModel.Location.Y);
            Assert.Equal(origin.X, centroidX, 0.001);
            Assert.Equal(origin.Y, centroidY, 0.001);
            Assert.Equal(240, imported.Max(n => n.ViewModel.Location.X) - imported.Min(n => n.ViewModel.Location.X));
        }

        [AvaloniaFact]
        public async Task ImportNodesFromFragment_AsymmetricNodes_CentroidIntegerTruncation() {
            DataCache cache = await GetCacheAsync();
            Fixture source = NewFixture(cache);
            RecipeNodeElement node1 = AddRecipeNode(source, "iron-gear-wheel", new Point(-100, 0));
            RecipeNodeElement node2 = AddRecipeNode(source, "iron-gear-wheel", new Point(50, 30));
            RecipeNodeElement node3 = AddRecipeNode(source, "iron-gear-wheel", new Point(10, -41));
            source.Control.Viewer.SetSelection([node1, node2, node3]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture(cache);
            var origin = new Point(0, 0);
            target.Control.Viewer.ImportNodesFromFragment(cache, fragmentJson, origin, applySolverSettings: true);

            List<BaseNodeElement> imported = [.. target.Control.Viewer.NodeElements.OrderBy(n => n.ViewModel.Location.X)];
            Assert.Equal(3, imported.Count);
            Assert.Equal(new Point(-87, 3), imported[0].ViewModel.Location);
            Assert.Equal(new Point(23, -38), imported[1].ViewModel.Location);
            Assert.Equal(new Point(63, 33), imported[2].ViewModel.Location);
        }

        //---- selection + highlight (reference §2 step 5) --------------------------------------------------

        [AvaloniaFact]
        public async Task ImportNodesFromFragment_SelectsAndHighlightsOnlyTheImportedNodes() {
            DataCache cache = await GetCacheAsync();
            Fixture target = NewFixture(cache);
            RecipeNodeElement existing = AddRecipeNode(target, "iron-gear-wheel", Point.Empty);
            target.Control.Viewer.SetSelection([existing]);

            Fixture source = NewFixture(cache);
            RecipeNodeElement toImport = AddRecipeNode(source, "iron-gear-wheel", new Point(300, 300));
            source.Control.Viewer.SetSelection([toImport]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            target.Control.Viewer.ImportNodesFromFragment(cache, fragmentJson, new Point(500, 500), applySolverSettings: true);

            BaseNodeElement selected = Assert.Single(target.Control.Viewer.SelectedNodes);
            Assert.NotEqual(existing.ViewModel.Id, selected.ViewModel.Id);
            Assert.True(selected.Highlighted);
            Assert.False(existing.Highlighted);
        }

        //---- merge into a non-empty graph keeps the existing nodes (reference §2) -------------------------

        [AvaloniaFact]
        public async Task ImportNodesFromFragment_MergeIntoNonEmptyGraph_KeepsExistingNodesInPlace() {
            DataCache cache = await GetCacheAsync();
            Fixture target = NewFixture(cache);
            RecipeNodeElement existing = AddRecipeNode(target, "iron-gear-wheel", new Point(10, 10));

            Fixture source = NewFixture(cache);
            RecipeNodeElement toImport = AddRecipeNode(source, "iron-gear-wheel", Point.Empty);
            source.Control.Viewer.SetSelection([toImport]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            target.Control.Viewer.ImportNodesFromFragment(cache, fragmentJson, new Point(500, 500), applySolverSettings: true);

            Assert.Equal(2, target.Control.Viewer.NodeElements.Count);
            Assert.True(target.Control.Viewer.NodeElementDictionary.ContainsKey(existing.ViewModel.Id));
            Assert.Equal(new Point(10, 10), existing.ViewModel.Location);
        }

        [AvaloniaTheory]
        [InlineData("not json at all")]
        [InlineData("")]
        public async Task ImportNodesFromFragment_UnparsableText_NoCrashNoNewNodes(string text) {
            Fixture fx = NewFixture(await GetCacheAsync());

            int imported = fx.Control.Viewer.ImportNodesFromFragment(fx.Cache, text, Point.Empty, applySolverSettings: true);

            Assert.Equal(0, imported);
            Assert.Empty(fx.Control.Viewer.NodeElements);
        }

        //---- MainWindow wiring: no dirty-check, merges rather than replaces (reference §2) ----------------

        [AvaloniaFact]
        public async Task ImportGraphJsonAsync_DoesNotPromptForUnsavedChanges_AndMergesIntoExistingGraph() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var existingPair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(existingPair, new Point(500, 500));
            window.DiscardUnsavedGraphConfirmStub = () => throw new InvalidOperationException("Import must not run the dirty-check prompt");
            window.SaveBeforeContinuingChoiceStub = () => throw new InvalidOperationException("Import must not run the dirty-check prompt");

            var sourceGraph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };
            IRecipe recipe = cache.Recipes.Values.First(r => r.Enabled);
            sourceGraph.CreateRecipeNode(new RecipeQualityPair(recipe, cache.DefaultQuality!), Point.Empty);
            string fragmentJson = GraphSaveCodec.WriteProductionGraphToString(sourceGraph, writeIndented: false);

            await window.ImportGraphJsonAsync(fragmentJson, "/tmp/import-test.fjson");

            Assert.Equal(2, window.GraphCanvas.Viewer.NodeElements.Count);
        }
    }
}
