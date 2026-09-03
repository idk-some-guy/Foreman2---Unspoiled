using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Canvas;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Phase 7 task 2 (perf-packaging-reference.md §1b): proves the SKPaint-caching pass across the seven
    //audited draw-path files (BaseNodeElement, PassthroughNodeElement, BaseLinkElement, TextAnnotationElement,
    //ShapeAnnotationElement, AnnotationElement, GraphViewer) is allocation-behavior-preserving. The demo graph
    //is the same Flowchart.fjson fixture GraphViewerIntegrationTests' phase-3 acceptance path already
    //established - reusing it here keeps "the established demo graph" the plan calls for literal, not a new
    //one-off fixture.
    public class PaintPathAllocationTests {
        private const string PresetName = "Factorio 2.0 Space Age";
        private const int Width = 1600, Height = 1200;
        private const int WarmupFrames = 5;
        private const int MeasuredFrames = 100;

        //Ceiling for allocated bytes per frame across the 100-frame loop. Pre-caching baseline: 209,113
        //bytes/frame over this same demo graph (65 nodes, 106 links). Post-caching (task-2-report.md): ~187,700.
        //The fix-round's item 4 fold-in (BaseLinkElement's per-link SKPath now reused instead of rebuilt)
        //dropped that further to ~179,200-179,250 bytes/frame, stable across 3 repeated runs.
        //
        //Task 2b converts GraphicsStuff.DrawText/DrawStringAtPoint (~40 call sites) from a fresh SKPaint+
        //SKFont per call to [ThreadStatic]-cached instances mutate-reset per call. Measured 132,536
        //bytes/frame, identical across 4 repeated runs (no jitter observed on this machine).
        //
        //137,000 sits ~4,464 bytes above that baseline (covering run-to-run noise on other machines) and
        //~42,000 bytes below a sanity-check regression: reverting just DrawText's caching back to per-call
        //allocation (while leaving DrawStringAtPoint cached) measured 179,230 bytes/frame, so this ceiling
        //stays tight enough to catch a partial reversion of this change while leaving headroom for jitter.
        private const long MaxBytesPerFrame = 137_000;

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

        private static GraphViewer NewFittedDemoViewer(DataCache cache) {
            var viewer = new GraphViewer(new Viewport(Width, Height), new GridManager());
            GraphLoadResult result = viewer.LoadDocument(cache, FlowchartJson());
            Assert.True(result.Success, result.ErrorMessage);

            System.Drawing.Rectangle bounds = viewer.Graph.Bounds;
            float scale = Math.Min((float)(Width / (double)bounds.Width), (float)(Height / (double)bounds.Height)) * 0.9f;
            viewer.Viewport.ViewScale = Math.Clamp(scale, Viewport.MinViewScale, Viewport.MaxViewScale);
            var center = new System.Drawing.Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            viewer.Viewport.ViewOffset = new System.Drawing.Point(-center.X, -center.Y);
            viewer.Viewport.UpdateGraphBounds();
            return viewer;
        }

        //SHA-256 over the raw decoded pixel bytes (not the PNG-encoded stream) - a straight pixel-identity
        //proof that doesn't depend on the PNG encoder ever staying byte-stable across SkiaSharp versions.
        private static string HashPixels(SKSurface surface) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            byte[] hash = SHA256.HashData(pixmap.GetPixelSpan());
            return Convert.ToHexString(hash);
        }

        [AvaloniaFact]
        public async Task DemoGraphRender_OffscreenPixels_MatchCommittedPreCacheHash() {
            DataCache cache = await GetCacheAsync();
            GraphViewer viewer = NewFittedDemoViewer(cache);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));
            viewer.Paint(surface.Canvas);

            //Captured from the pre-caching render (task-2-report.md records the capture run). Any change to
            //this hash after the caching pass is a pixel-drift STOP, not a value to update casually.
            //Known limitation (undocumented in CI today): this hash is pinned to the text-rendering output of
            //the host's font stack (CoreText on macOS) - a run on a different OS/font-substitution setup would
            //need its own captured value.
            Assert.Equal("2CA20E20330CB0E4938068863C391535B198E0A7396B12D5FE2FB8D17791B2E4", HashPixels(surface));
        }

        [AvaloniaFact]
        public async Task DemoGraphPaint_100FrameLoop_AllocatesUnderPostCacheCeiling() {
            DataCache cache = await GetCacheAsync();
            GraphViewer viewer = NewFittedDemoViewer(cache);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));

            //Warm-up frames absorb JIT and the first-touch element/tab/font lazy-init cost so the measured
            //loop only sees steady-state per-frame draw-path allocation.
            for (int i = 0; i < WarmupFrames; i++)
                viewer.Paint(surface.Canvas);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasuredFrames; i++)
                viewer.Paint(surface.Canvas);
            long after = GC.GetAllocatedBytesForCurrentThread();

            long bytesPerFrame = (after - before) / MeasuredFrames;
            Assert.True(bytesPerFrame < MaxBytesPerFrame,
                $"Expected under {MaxBytesPerFrame:N0} allocated bytes/frame, measured {bytesPerFrame:N0} " +
                $"({viewer.NodeElements.Count} nodes, {viewer.LinkElements.Count} links).");
        }
    }
}
