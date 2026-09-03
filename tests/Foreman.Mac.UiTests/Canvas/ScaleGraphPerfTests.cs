using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Phase 7 task 4 (perf-packaging-reference.md §1a): builds a 1000+-node graph to verify the port's scale
    //story against upstream's exact semantics - NodeCountForSimpleView's strict ">" dispatch, visible-rect
    //culling, and one full LP solve at scale - and records the phase's scale baseline (task-4-report.md).
    //
    //Upstream's LOD enum (ProductionGraphViewer.cs:27, {Low,Medium,High}) maps differently here: this
    //port's element detail visibility (assembler/beacon icons, percentage text) uses LevelOfDetail, while
    //the paint dispatch uses NodeDrawingStyle - §1a notes the enum is "unused by the draw-style switch
    //itself". What this port actually has, and
    //what the phase plan's "LOD Low/Medium/High" cashes out to here, is NodeDrawingStyle's three real cost
    //tiers: IconsOnly (bitmap only, cheapest), Simple (background/border, no DetailsDraw), Regular (adds
    //DetailsDraw - icons, module dots, percentage text, the most expensive tier).
    //
    //Captured absolutes (task-4-report.md carries the full baseline; recorded here for a quick regression
    //reference, not asserted on - this machine, one run, and order-dependent across the whole suite: which
    //test happens to touch the solver/JIT/element-tree first affects every number below): graph is 2984
    //nodes (1000 recipe + 1984 supplier/consumer) and 1984 links. Wide-viewport paint: IconsOnly 3.6ms,
    //Simple 7.6ms, Regular 16.7ms. Culled small-viewport paint: 6.4ms vs. an uncalled fullGraph paint (all
    //2984 nodes, PrintStyle): 47.8ms. One full solve of the 1000-node graph: 143.5ms - a cold-solver figure
    //(no prior CreateSolver call in this run primes OR-Tools' native dylib load, reference: Task 1's
    //94ms-11s cold-cache finding), not the warm per-solve cost the app pays after ShellBootstrapper's
    //background warmup completes.
    public class ScaleGraphPerfTests {
        private const string PresetName = "Factorio 2.0 Space Age";
        private const int Width = 1600, Height = 1200;
        private const int RecipeNodeCount = 1000;
        private const int GridColumns = 40;
        private const int CellSpacing = 400;
        private const int WarmupFrames = 2;
        private const int MeasuredFrames = 5;

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static readonly SemaphoreSlim GraphGate = new(1, 1);
        private static GraphViewer? sharedViewer;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(PresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        //Built once and reused across every test below (gate note: keep the harness bounded) - 1000 real
        //recipe nodes, each wrapped in a supplier/consumer pair over one of its own real input/output items
        //(so every CreateLink call is valid against the recipe's actual Inputs/Outputs, no synthetic item
        //matching needed), laid out on a grid spread over a ~16,000x10,000 graph-space area so a normal-zoom
        //viewport only ever sees a fraction of it - the culling case §1a describes.
        private static async Task<GraphViewer> GetLargeGraphViewerAsync() {
            if (sharedViewer is not null)
                return sharedViewer;
            await GraphGate.WaitAsync();
            try {
                sharedViewer ??= BuildLargeGraph(await GetCacheAsync());
            } finally {
                GraphGate.Release();
            }
            return sharedViewer;
        }

        private static GraphViewer BuildLargeGraph(DataCache cache) {
            IQuality quality = cache.DefaultQuality ?? throw new InvalidOperationException("Preset fixture has no default quality.");
            List<IRecipe> recipes = [.. cache.AvailableRecipes.OrderBy(r => r.Name, StringComparer.Ordinal)];
            Assert.True(recipes.Count > 0, "Preset fixture has no available recipes to build the scale graph from.");

            var viewer = new GraphViewer(new Viewport(Width, Height), new GridManager());
            viewer.Graph.DefaultAssemblerQuality = quality;

            for (int i = 0; i < RecipeNodeCount; i++) {
                IRecipe recipe = recipes[i % recipes.Count];
                var location = new Point((i % GridColumns) * CellSpacing, (i / GridColumns) * CellSpacing);
                RecipeNode node = viewer.Graph.CreateRecipeNode(new RecipeQualityPair(recipe, quality), location);

                List<ItemQualityPair> inputs = [.. node.Inputs];
                if (inputs.Count > 0) {
                    ItemQualityPair item = inputs[0];
                    var supplier = viewer.Graph.CreateSupplierNode(item, new Point(location.X - 150, location.Y - 60));
                    viewer.Graph.CreateLink(supplier, node, item);
                }

                List<ItemQualityPair> outputs = [.. node.Outputs];
                if (outputs.Count > 0) {
                    ItemQualityPair item = outputs[0];
                    var consumer = viewer.Graph.CreateConsumerNode(item, new Point(location.X + 150, location.Y + 60));
                    viewer.Graph.CreateLink(node, consumer, item);
                }
            }

            viewer.Viewport.UpdateGraphBounds(viewer.Graph.Bounds);
            return viewer;
        }

        //Centers the viewport on a graph-space point without touching ViewScale, then recomputes
        //VisibleGraphBounds - mirrors NewFittedDemoViewer's own ViewOffset/UpdateGraphBounds pattern
        //(PaintPathAllocationTests.cs), just parameterized on scale/center for this file's two viewport
        //shapes (wide vs. small-area).
        private static void SetViewport(GraphViewer viewer, float scale, Point centerOnGraphPoint) {
            viewer.Viewport.ViewScale = scale;
            viewer.Viewport.ViewOffset = new Point(-centerOnGraphPoint.X, -centerOnGraphPoint.Y);
            viewer.Viewport.UpdateGraphBounds();
        }

        private static Point GraphCenter(GraphViewer viewer) {
            Rectangle bounds = viewer.Graph.Bounds;
            return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        }

        private static string HashPixels(SKSurface surface) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            byte[] hash = SHA256.HashData(pixmap.GetPixelSpan());
            return Convert.ToHexString(hash);
        }

        private static TimeSpan TimePaint(GraphViewer viewer, SKSurface surface, bool fullGraph = false) {
            for (int i = 0; i < WarmupFrames; i++)
                viewer.Paint(surface.Canvas, fullGraph);

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredFrames; i++)
                viewer.Paint(surface.Canvas, fullGraph);
            stopwatch.Stop();
            return stopwatch.Elapsed / MeasuredFrames;
        }

        //---- (a) NodeCountForSimpleView's exact dispatch: visibleElements > threshold, not >= ----

        [AvaloniaFact]
        public async Task SimpleViewSwitch_EngagesStrictlyAboveThreshold_NotAtOrBelow() {
            GraphViewer viewer = await GetLargeGraphViewerAsync();
            //0.25x keeps ViewScale above the separate "< 0.2 forces Simple regardless of count" fallback
            //(§1a) so this test isolates the NodeCountForSimpleView comparison on its own.
            SetViewport(viewer, 0.25f, GraphCenter(viewer));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));

            viewer.NodeCountForSimpleView = int.MaxValue;
            viewer.Paint(surface.Canvas);
            int visibleElements = viewer.NodeElementDictionary.Values.Count(e => e.Visible);
            Assert.True(visibleElements > 0, "Wide viewport should have visible nodes to test the threshold against.");

            viewer.NodeCountForSimpleView = visibleElements + 1;
            viewer.Paint(surface.Canvas);
            string aboveThreshold = HashPixels(surface);

            viewer.NodeCountForSimpleView = visibleElements;
            viewer.Paint(surface.Canvas);
            string atThreshold = HashPixels(surface);

            viewer.NodeCountForSimpleView = visibleElements - 1;
            viewer.Paint(surface.Canvas);
            string belowThreshold = HashPixels(surface);

            //visibleElements > threshold: false when threshold >= visibleElements (Regular, matches
            //"above threshold" configuration since neither engages Simple), true only once threshold drops
            //below visibleElements. Upstream's operator is strict ">" - equality must still render Regular.
            Assert.Equal(aboveThreshold, atThreshold);
            Assert.NotEqual(atThreshold, belowThreshold);
        }

        //---- (b) LOD/style timing (relative sanity only - absolutes go in task-4-report.md) ----

        [AvaloniaFact]
        public async Task WideViewportPaint_IconsOnlyAndSimple_CheaperThanRegular() {
            GraphViewer viewer = await GetLargeGraphViewerAsync();
            SetViewport(viewer, 0.25f, GraphCenter(viewer));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));

            viewer.IconsOnly = true;
            TimeSpan iconsOnly = TimePaint(viewer, surface);
            viewer.IconsOnly = false;

            viewer.NodeCountForSimpleView = 0; //forces Simple regardless of how many nodes are visible
            TimeSpan simple = TimePaint(viewer, surface);

            viewer.NodeCountForSimpleView = int.MaxValue; //forces Regular
            TimeSpan regular = TimePaint(viewer, surface);

            Assert.True(iconsOnly < regular, $"IconsOnly ({iconsOnly.TotalMilliseconds:F3}ms) should be cheaper than Regular ({regular.TotalMilliseconds:F3}ms).");
            Assert.True(simple < regular, $"Simple ({simple.TotalMilliseconds:F3}ms) should be cheaper than Regular ({regular.TotalMilliseconds:F3}ms).");
        }

        [AvaloniaFact]
        public async Task CulledPaint_SmallViewport_CheaperThanFullGraphPaint() {
            GraphViewer viewer = await GetLargeGraphViewerAsync();
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));

            //A handful of nodes near the graph's middle, at a normal (non-zoomed-out) scale - culling
            //should drop everything else from the visibility pass before it ever reaches the style dispatch.
            int midIndex = RecipeNodeCount / 2;
            var smallAreaCenter = new Point((midIndex % GridColumns) * CellSpacing, (midIndex / GridColumns) * CellSpacing);
            SetViewport(viewer, 1f, smallAreaCenter);
            TimeSpan culled = TimePaint(viewer, surface, fullGraph: false);

            //fullGraph:true ignores the viewport entirely (Paint uses Graph.Bounds as the visibility zone -
            //GraphViewer.cs:411) and forces PrintStyle, so every node in the graph draws at full detail.
            TimeSpan fullGraphPaint = TimePaint(viewer, surface, fullGraph: true);

            Assert.True(culled < fullGraphPaint,
                $"Culled small-viewport paint ({culled.TotalMilliseconds:F3}ms) should be far cheaper than an uncalled fullGraph paint ({fullGraphPaint.TotalMilliseconds:F3}ms).");
        }

        //---- (c) one solve at scale (absolute recorded in task-4-report.md, no timing assertion here) ----

        [AvaloniaFact]
        public async Task Solve_1000NodeGraph_CompletesWithoutThrowing() {
            GraphViewer viewer = await GetLargeGraphViewerAsync();

            var stopwatch = Stopwatch.StartNew();
            var exception = Record.Exception(() => viewer.Graph.UpdateNodeValues());
            stopwatch.Stop();

            Assert.Null(exception);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(1),
                $"Solve took {stopwatch.Elapsed} - suspiciously long even as a generous non-flaky ceiling, not a tuned regression gate.");
        }
    }
}
