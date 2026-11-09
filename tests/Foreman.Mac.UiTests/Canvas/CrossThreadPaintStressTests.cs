using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Final-review C1: MainWindow hands ImageExportWindow the LIVE GraphViewer (MainWindow.axaml.cs's
    //`new ImageExportWindow(GraphCanvas.Viewer)`), so ImageExportWindow.ExportAsync paints on the UI thread
    //while GraphCanvasControl.Render paints the same viewer on the compositor's render thread - both
    //walking the exact same BaseLinkElement/PassthroughNodeElement/ShapeAnnotationElement instances at
    //once. GraphicsStuff.RentStrokePaint/RentFillPaint/RentPath (the fix for those three classes) back
    //every one of their Draw() calls with [ThreadStatic] scratch objects instead of shared per-instance
    //fields, and a race there (SKPath.Reset colliding with DrawPath is a native-side race) can only be
    //demonstrated by two real OS threads painting concurrently - a single-threaded test can't observe it.
    public class CrossThreadPaintStressTests {
        private const string PresetName = "Factorio 2.0 Space Age";
        private const int Width = 800, Height = 600;
        private const int IterationsPerThread = 150;

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

        //Same demo graph PaintPathAllocationTests uses (65 nodes/106 links, one passthrough node), with its
        //passthrough node's simple-draw + highlight branches forced on so both of PassthroughNodeElement's
        //rented-paint call sites actually run, plus a filled/bordered shape annotation so
        //ShapeAnnotationElement's two rented paints run too. BaseLinkElement's rented path/paint get heavy
        //exercise for free from the 106 links.
        private static GraphViewer NewStressViewer(DataCache cache) {
            var viewer = new GraphViewer(new Viewport(Width, Height), new GridManager());
            GraphLoadResult result = viewer.LoadDocument(cache, FlowchartJson());
            Assert.True(result.Success, result.ErrorMessage);

            foreach (BaseNodeElement element in viewer.NodeElements) {
                if (element.ViewModel is not IPassthroughNodeViewModel)
                    continue;
                if (viewer.Session.Editor.RequestNodeController(element.ViewModel.Id) is PassthroughNodeController controller)
                    controller.SetSimpleDraw(true);
                element.Highlighted = true;
            }

            var shape = new ShapeAnnotationElement(new System.Drawing.Point(0, 0), 200, 150) {
                FillColor = SKColors.CornflowerBlue,
                BorderColor = SKColors.Black,
                BorderWidth = 4,
            };
            viewer.AddAnnotationElement(shape);

            System.Drawing.Rectangle bounds = viewer.Graph.Bounds;
            float scale = Math.Min((float)(Width / (double)bounds.Width), (float)(Height / (double)bounds.Height)) * 0.9f;
            viewer.Viewport.ViewScale = Math.Clamp(scale, Viewport.MinViewScale, Viewport.MaxViewScale);
            var center = new System.Drawing.Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            viewer.Viewport.ViewOffset = new System.Drawing.Point(-center.X, -center.Y);
            viewer.Viewport.UpdateGraphBounds();
            return viewer;
        }

        //Mirrors the real setup exactly: one shared GraphViewer, one thread standing in for the
        //compositor's render loop (fullGraph: false, the GraphCanvasControl.Render path), the other for
        //ImageExportWindow.ExportAsync's direct call (fullGraph: true). Bounded iteration count keeps this
        //from turning into a flaky timing-dependent test - the race either reproduces reliably within a
        //couple hundred concurrent paints against the old per-instance fields, or it doesn't reproduce at
        //all under the ThreadStatic fix; there's no useful middle ground from running longer.
        [AvaloniaFact]
        public async Task TwoThreadsPaintingSharedViewer_NoCrashAndBothSurfacesValid() {
            DataCache cache = await GetCacheAsync();
            GraphViewer viewer = NewStressViewer(cache);

            using SKSurface canvasSurface = SKSurface.Create(new SKImageInfo(Width, Height));
            using SKSurface exportSurface = SKSurface.Create(new SKImageInfo(Width, Height));

            Exception? canvasThreadException = null;
            Exception? exportThreadException = null;

            var canvasThread = new Thread(() => {
                try {
                    for (int i = 0; i < IterationsPerThread; i++)
                        viewer.Paint(canvasSurface.Canvas, fullGraph: false);
                } catch (Exception ex) {
                    canvasThreadException = ex;
                }
            }) { IsBackground = true };

            var exportThread = new Thread(() => {
                try {
                    for (int i = 0; i < IterationsPerThread; i++)
                        viewer.Paint(exportSurface.Canvas, fullGraph: true, clearBackground: false);
                } catch (Exception ex) {
                    exportThreadException = ex;
                }
            }) { IsBackground = true };

            canvasThread.Start();
            exportThread.Start();
            bool canvasJoined = canvasThread.Join(TimeSpan.FromSeconds(30));
            bool exportJoined = exportThread.Join(TimeSpan.FromSeconds(30));

            Assert.True(canvasJoined, "Canvas-thread paint loop did not complete within the timeout.");
            Assert.True(exportJoined, "Export-thread paint loop did not complete within the timeout.");
            Assert.Null(canvasThreadException);
            Assert.Null(exportThreadException);

            using SKImage canvasImage = canvasSurface.Snapshot();
            using SKImage exportImage = exportSurface.Snapshot();
            using SKPixmap canvasPixmap = canvasImage.PeekPixels();
            using SKPixmap exportPixmap = exportImage.PeekPixels();
            Assert.True(canvasPixmap.GetPixelSpan().Length > 0);
            Assert.True(exportPixmap.GetPixelSpan().Length > 0);
        }
    }
}
