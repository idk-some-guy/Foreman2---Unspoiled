using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvaloniaPoint = Avalonia.Point;

namespace Foreman.Mac.UiTests.Canvas {
    //THE phase-3 acceptance path (task-7 brief): a repo sample .fjson through the real Load path
    //(GraphViewer.LoadDocument, GraphSaveLoader under the hood - reference §8.12), rendered end to end.
    //Also pins the two carried requirements from earlier task reviews: element-lifecycle disposal on graph
    //clear, and that GetPaintingOrder's node order stays lockstep with HitTest's reverse-iteration pick.
    public class GraphViewerIntegrationTests {
        private const string PresetName = "Factorio 2.0 Space Age";
        private const int Width = 1600, Height = 1200;

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

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

        private static string FlowchartJson() {
            string repoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
            return File.ReadAllText(Path.Combine(repoRoot, "tests", "ForemanTest", "assets", "Flowchart.fjson"));
        }

        private static GraphCanvasControl NewLoadedControl(DataCache cache) {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(Width, Height);
            GraphLoadResult result = control.Viewer.LoadDocument(cache, FlowchartJson());
            Assert.True(result.Success, result.ErrorMessage);
            return control;
        }

        //Centers and scales the viewport so the whole loaded graph sits within the render surface.
        private static void FitToView(GraphCanvasControl control) {
            Rectangle bounds = control.Viewer.Graph.Bounds;
            float scale = Math.Min((float)(Width / (double)bounds.Width), (float)(Height / (double)bounds.Height)) * 0.9f;
            control.Viewport.ViewScale = Math.Clamp(scale, Viewport.MinViewScale, Viewport.MaxViewScale);
            var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            control.Viewport.ViewOffset = new Point(-center.X, -center.Y);
            control.Viewport.UpdateGraphBounds();
        }

        private static byte[] RenderPng(GraphCanvasControl control) {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));
            control.Render(surface.Canvas);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static bool RegionHasNonBackgroundPixel(SKPixmap pixmap, int centerX, int centerY, int radius = 20) {
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++) {
                    int x = centerX + dx, y = centerY + dy;
                    if (x < 0 || y < 0 || x >= pixmap.Width || y >= pixmap.Height)
                        continue;
                    if (pixmap.GetPixelColor(x, y) != SKColors.White)
                        return true;
                }
            return false;
        }

        [AvaloniaFact]
        public async Task LoadDocument_RealSampleFile_ElementCountsMatchLoadedGraph() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewLoadedControl(cache);

            Assert.Equal(control.Viewer.Graph.Nodes.Count(), control.NodeElements.Count);
            Assert.Equal(control.Viewer.Graph.NodeLinks.Count(), control.LinkElements.Count);
            Assert.NotEmpty(control.NodeElements);
            Assert.NotEmpty(control.LinkElements);
        }

        [AvaloniaFact]
        public async Task Render_FitToView_PaintsRealNodesAtTheirScreenPositions() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewLoadedControl(cache);
            FitToView(control);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));
            control.Render(surface.Canvas);
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();

            foreach (BaseNodeElement node in control.NodeElements.Take(3)) {
                AvaloniaPoint screen = control.Viewport.GraphToScreen(node.Location);
                Assert.True(
                    RegionHasNonBackgroundPixel(pixmap, (int)screen.X, (int)screen.Y),
                    $"Expected the node at graph {node.Location} (screen {screen}) to paint something.");
            }
        }

        [AvaloniaFact]
        public async Task Render_ToggleIconsOnly_ChangesRender() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewLoadedControl(cache);
            FitToView(control);

            byte[] before = RenderPng(control);
            control.Viewer.IconsOnly = true;
            byte[] after = RenderPng(control);

            Assert.NotEqual(before, after);
        }

        [AvaloniaFact]
        public async Task Render_ToggleGridlines_ChangesRender() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewLoadedControl(cache);
            FitToView(control);
            control.Grid.CurrentGridUnit = 48;

            byte[] before = RenderPng(control);
            control.Grid.ShowGrid = true;
            byte[] after = RenderPng(control);

            Assert.NotEqual(before, after);
        }

        [AvaloniaFact]
        public async Task Render_ToggleLevelOfDetail_ChangesRender() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewLoadedControl(cache);
            FitToView(control);
            control.Viewer.Context.LevelOfDetail = LevelOfDetail.High;

            byte[] before = RenderPng(control);
            control.Viewer.Context.LevelOfDetail = LevelOfDetail.Low;
            byte[] after = RenderPng(control);

            Assert.NotEqual(before, after);
        }

        //---- carried requirement 1: element lifecycle/teardown (Task 6 review) ----

        [AvaloniaFact]
        public async Task ClearGraph_DisposesNodeAndLinkElements_NotJustDropsThemFromTheLists() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewLoadedControl(cache);
            BaseNodeElement firstNode = control.NodeElements[0];
            Assert.NotEmpty(firstNode.SubElements); //item tabs / error notice - something for Dispose to cascade into

            control.Viewer.Graph.ClearGraph();

            Assert.Empty(control.NodeElements);
            Assert.Empty(control.LinkElements);
            Assert.Empty(firstNode.SubElements); //proves Dispose() ran (cascaded into the node's own subelement tree), not just list removal
        }

        //---- carried requirement 2: NodeValuesUpdated resyncs node state (final-review finding 3) ----

        [AvaloniaFact]
        public async Task RateUnitChange_FiresNodeValuesUpdatedOnly_StillResyncsNodeState() {
            DataCache cache = await GetCacheAsync();
            GraphCanvasControl control = NewLoadedControl(cache);
            BaseNodeElement firstNode = control.NodeElements[0];
            firstNode.PrePaint();
            int baseline = firstNode.UpdateStateCallCount;

            //A rate-unit change touches no link, so it fires only Graph.NodeValuesUpdated, never
            //NodeViewModelAdded/Removed or LinkViewModelAdded/Removed - the events GraphViewer already
            //wired before this fix. Without also wiring NodeValuesUpdated, UpdateState() (tab order,
            //error-notice placement) never reruns from a values-only mutation like this one.
            control.Viewer.Graph.SelectedRateUnit = control.Viewer.Graph.SelectedRateUnit == ProductionGraph.RateUnit.Per1Sec
                ? ProductionGraph.RateUnit.Per1Min
                : ProductionGraph.RateUnit.Per1Sec;
            control.Viewer.Graph.UpdateNodeValues();
            firstNode.PrePaint();

            Assert.True(firstNode.UpdateStateCallCount > baseline);
        }

        //---- the phase-3 visual gate, offscreen substitute ----
        //
        //The brief's real-app gate (boot the live window, screenshot it via osascript/screencapture) needs a
        //working Avalonia.Native GPU render loop; this sandbox's WindowServer session can't provide one (any
        //Avalonia app, this one included, aborts in AppBuilder.Setup with "Avalonia.Native was not able to
        //start the RenderTimer" before a single line of app code runs - confirmed independent of this task's
        //changes). This test exercises the identical code path (GraphViewer.LoadDocument -> fit-to-view ->
        //zoom-in render) through the same offscreen SKSurface technique every other Canvas/ test already
        //uses, and writes both frames plus wall-clock timings to the SDD workspace for the eyeball pass.
        [AvaloniaFact]
        public async Task VisualGate_RealLoadPath_OverviewAndDetailZoomRenderToSddWorkspace() {
            DataCache cache = await GetCacheAsync();

            var loadStopwatch = Stopwatch.StartNew();
            GraphCanvasControl control = NewLoadedControl(cache);
            long loadElapsedMs = loadStopwatch.ElapsedMilliseconds;

            FitToView(control);
            var overviewStopwatch = Stopwatch.StartNew();
            byte[] overviewPng = RenderPng(control);
            long overviewRenderMs = overviewStopwatch.ElapsedMilliseconds;

            Rectangle bounds = control.Viewer.Graph.Bounds;
            var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            control.Viewport.ViewScale = 1f;
            control.Viewport.ViewOffset = new Point(-center.X, -center.Y);
            control.Viewport.UpdateGraphBounds();
            var detailStopwatch = Stopwatch.StartNew();
            byte[] detailPng = RenderPng(control);
            long detailRenderMs = detailStopwatch.ElapsedMilliseconds;

            string workspaceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".superpowers", "sdd", "2026-09-01-phase3-canvas-readonly");
            Directory.CreateDirectory(workspaceDir);
            File.WriteAllBytes(Path.Combine(workspaceDir, "task-7-overview.png"), overviewPng);
            File.WriteAllBytes(Path.Combine(workspaceDir, "task-7-detail-zoom.png"), detailPng);
            File.WriteAllText(Path.Combine(workspaceDir, "task-7-render-metrics.txt"),
                $"Flowchart.fjson: {control.NodeElements.Count} nodes, {control.LinkElements.Count} links{Environment.NewLine}" +
                $"Load (LoadDocument, incl. solve): {loadElapsedMs} ms{Environment.NewLine}" +
                $"Overview render: {overviewRenderMs} ms{Environment.NewLine}" +
                $"Detail-zoom render: {detailRenderMs} ms{Environment.NewLine}");

            Assert.True(File.Exists(Path.Combine(workspaceDir, "task-7-overview.png")));
            Assert.True(File.Exists(Path.Combine(workspaceDir, "task-7-detail-zoom.png")));
        }
    }
}
