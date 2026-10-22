using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.UiTests.Inspector;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using AvColor = Avalonia.Media.Color;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    public sealed class FindMatchRingTests {
        private const int Half = 200;

        private static async Task<(MainWindow Window, List<BaseNodeElement> Nodes)> WindowWithItems(params string[] items) {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await InspectorTestSupport.GetCacheAsync();
            var nodes = new List<BaseNodeElement>();
            for (int i = 0; i < items.Length; i++) {
                var pair = new ItemQualityPair(cache.Items[items[i]], cache.DefaultQuality!);
                NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new DrawingPoint(0, i * 200));
                nodes.Add(window.GraphCanvas.Viewer.NodeElementDictionary[id]);
            }
            Dispatcher.UIThread.RunJobs();
            foreach (BaseNodeElement node in nodes)
                node.PrePaint();
            return (window, nodes);
        }

        private static SKRect GraphBounds(BaseNodeElement node) {
            DrawingPoint centre = node.LocalToGraph(DrawingPoint.Empty);
            return SKRect.Create(centre.X - (node.Width / 2f), centre.Y - (node.Height / 2f), node.Width, node.Height);
        }

        private static SKRect IconBox(BaseNodeElement node, NodeElementContext context) {
            DrawingPoint centre = node.LocalToGraph(DrawingPoint.Empty);
            float half = context.IconsDrawSize / 2f;
            return new SKRect(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half);
        }

        private static void AssertRect(SKRect expected, SKRect actual) {
            Assert.Equal(expected.Left, actual.Left, 0.5);
            Assert.Equal(expected.Top, actual.Top, 0.5);
            Assert.Equal(expected.Right, actual.Right, 0.5);
            Assert.Equal(expected.Bottom, actual.Bottom, 0.5);
        }

        private sealed class TestAppearance(AvColor accent) : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => accent;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.NoPreference;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        private static SKSurface Render(BaseNodeElement node, NodeDrawingStyle style, float scale = 1) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(TokenBridge.Current.CanvasBackground);
            surface.Canvas.Translate(Half, Half);
            surface.Canvas.Scale(scale);
            node.PrePaint();
            node.Paint(surface.Canvas, style);
            return surface;
        }

        private static List<SKColor> Run(SKSurface surface, float graphX, float graphY, int length) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return [.. Enumerable.Range(0, length).Select(dx => pixmap.GetPixelColor(Half + (int)Math.Floor(graphX) + dx, Half + (int)Math.Floor(graphY)))];
        }

        private static SKColor[,] Pixels(SKSurface surface) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            var pixels = new SKColor[pixmap.Width, pixmap.Height];
            for (int x = 0; x < pixmap.Width; x++)
                for (int y = 0; y < pixmap.Height; y++)
                    pixels[x, y] = pixmap.GetPixelColor(x, y);
            return pixels;
        }

        private static double Coverage(SKColor pixel, SKColor ring, SKColor ground) {
            static int Distance(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
            return Math.Clamp((double)Distance(pixel, ground) / Distance(ring, ground), 0, 1);
        }

        private static double[] BandThickness(SKColor[,] pixels, SKColor ring, SKColor ground, int x0, int x1, int y0, int y1) =>
            [.. Enumerable.Range(x0, x1 - x0).Select(x => Enumerable.Range(y0, y1 - y0).Sum(y => Coverage(pixels[x, y], ring, ground)))];

        private static BaseNodeElement OneNode(out CanvasFixture fx) {
            fx = CanvasFixture.WithVanillaAndOneNode(out _);
            return fx.Viewer.NodeElements.Single();
        }

        [AvaloniaFact]
        public async Task Plate_ThreeMatches_LaneARingInflated3_Stroke2_FindMatchRing_Dash5And3_Radius13() {
            (MainWindow window, List<BaseNodeElement> nodes) = await WindowWithItems("iron-plate", "copper-plate", "steel-plate", "iron-ore");

            window.Toolbar.SearchField.Text = "plate";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([true, true, true, false], nodes.Select(n => n.IsFindMatch));
            foreach (BaseNodeElement match in nodes.Take(3)) {
                match.PrePaint();
                RingGeometry ring = Assert.NotNull(match.LaneARing());
                AssertRect(SKRect.Inflate(GraphBounds(match), 3, 3), ring.Rect);
                Assert.Equal(2f, ring.StrokeWidth);
                Assert.Equal(TokenBridge.Current.FindMatchRing, ring.Color);
                Assert.Equal(255, ring.Color.Alpha);
                Assert.Equal([5f, 3f], ring.Dash ?? []);
                Assert.Equal(13f, ring.Radius);
            }
            Assert.Null(nodes[3].LaneARing());
        }

        [AvaloniaFact]
        public async Task CurrentMatch_BorderRingIsNodeSelected_OthersAreNot() {
            (MainWindow window, List<BaseNodeElement> nodes) = await WindowWithItems("iron-plate", "copper-plate", "steel-plate");

            window.Toolbar.SearchField.Text = "plate";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);

            BaseNodeElement current = Assert.Single(window.GraphCanvas.Viewer.SelectedNodes);
            Assert.Same(nodes[1], current);
            Assert.Equal(TokenBridge.Current.NodeSelected, current.BorderRing().Color);
            foreach (BaseNodeElement other in nodes.Where(n => !ReferenceEquals(n, current))) {
                Assert.True(other.IsFindMatch);
                Assert.NotEqual(TokenBridge.Current.NodeSelected, other.BorderRing().Color);
            }
        }

        [AvaloniaFact]
        public async Task EscapeOnce_ClearsIsFindMatchEverywhere_SameFrame() {
            (MainWindow window, List<BaseNodeElement> nodes) = await WindowWithItems("iron-plate", "copper-plate", "iron-ore");
            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "plate";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, nodes.Count(n => n.IsFindMatch));

            CanvasKeys.KeyDownOn(window.Toolbar.SearchField, Key.Escape);

            Assert.All(nodes, n => Assert.False(n.IsFindMatch));
            Assert.All(nodes, n => Assert.Null(n.LaneARing()));
        }

        [AvaloniaFact]
        public void ViewScale0Point1_SimpleStyle_RingBandIs2ScreenPixels() {
            BaseNodeElement node = OneNode(out CanvasFixture fx);
            using (fx) {
                fx.Viewer.Viewport.ViewScale = 0.1f;
                Assert.Equal(NodeDrawingStyle.Simple, fx.Viewer.StyleFor(1, fx.Viewer.Viewport.ViewScale, false));
                node.IsFindMatch = true;
                SKColor ring = TokenBridge.Current.FindMatchRing;

                using SKSurface surface = Render(node, NodeDrawingStyle.Simple, 0.1f);

                SKRect bounds = GraphBounds(node);
                SKColor[,] pixels = Pixels(surface);
                int x0 = Half + (int)(bounds.Left * 0.1f) + 3;
                int y = (int)(Half + ((bounds.Top - 3) * 0.1f));
                Assert.InRange(BandThickness(pixels, ring, TokenBridge.Current.CanvasBackground, x0, x0 + 12, y - 2, y + 2).Max(), 1.8, 2.2);
            }
        }

        [AvaloniaFact]
        public void ViewScale0Point5_RingIs2ScreenPixelsThick_Dash5On3Off_RectAndRadiusStayGraphSpace() {
            BaseNodeElement node = OneNode(out CanvasFixture fx);
            using (fx) {
                fx.Viewer.Viewport.ViewScale = 0.5f;
                node.IsFindMatch = true;
                SKColor ring = TokenBridge.Current.FindMatchRing;

                using SKSurface surface = Render(node, NodeDrawingStyle.Regular, 0.5f);

                RingGeometry geometry = Assert.NotNull(node.LaneARing());
                Assert.Equal(4f, geometry.StrokeWidth);
                Assert.Equal([10f, 6f], geometry.Dash ?? []);
                Assert.Equal(13f, geometry.Radius);
                AssertRect(SKRect.Inflate(GraphBounds(node), 3, 3), geometry.Rect);

                SKRect bounds = GraphBounds(node);
                SKColor[,] pixels = Pixels(surface);
                int x0 = Half + (int)(bounds.Left * 0.5f) + 10, x1 = x0 + 16;
                int y = (int)(Half + ((bounds.Top - 3) * 0.5f));
                double[] band = BandThickness(pixels, ring, TokenBridge.Current.CanvasBackground, x0, x1, y - 2, y + 2);
                Assert.InRange(band.Max(), 1.8, 2.2);
                Assert.InRange(band.Sum(), 18, 22);
                Assert.Equal(2, Enumerable.Range(1, band.Length - 1).Count(i => band[i - 1] < 1 && band[i] >= 1));
            }
        }

        [AvaloniaFact]
        public void FindDash_RebuildsPerScale_AndDisposesTheReplacedEffect() {
            FieldInfo field = typeof(NodeCardPainter).GetField("findDash", BindingFlags.NonPublic | BindingFlags.Static)!;
            static SKPathEffect EffectOf(FieldInfo field) {
                object cache = field.GetValue(null)!;
                return (SKPathEffect)cache.GetType().GetProperty("Effect")!.GetValue(cache)!;
            }

            float[] full = NodeCardPainter.FindDash(5, 3, 1);
            SKPathEffect first = EffectOf(field);
            Assert.Same(full, NodeCardPainter.FindDash(5, 3, 1));

            float[] half = NodeCardPainter.FindDash(10, 6, 0.5f);

            Assert.NotEqual(full, half);
            Assert.Equal(IntPtr.Zero, first.Handle);
            Assert.NotEqual(IntPtr.Zero, EffectOf(field).Handle);
        }

        [AvaloniaFact]
        public void IconView_RingRadiusIs8Plus3_AroundTheIconBox() {
            BaseNodeElement node = OneNode(out CanvasFixture fx);
            using (fx) {
                node.IsFindMatch = true;
                fx.Viewer.IconsOnly = true;
                node.PrePaint();

                RingGeometry ring = Assert.NotNull(node.LaneARing());

                AssertRect(SKRect.Inflate(IconBox(node, fx.Viewer.Context), 3, 3), ring.Rect);
                Assert.Equal(11f, ring.Radius);
                Assert.Equal(2f, ring.StrokeWidth);
            }
        }

        [AvaloniaFact]
        public void PlacementFlashOnAMatch_DrawsOverTheFindRingInLaneA_FocusStaysInLaneB() {
            BaseNodeElement node = OneNode(out CanvasFixture fx);
            using (fx) {
                App app = (App)Application.Current!;
                try {
                    app.InstallAppearance(new TestAppearance(AvColor.FromRgb(0x2E, 0x7D, 0x32)));
                    app.Hub.Refresh();
                    SKColor accent = TokenBridge.Current.Accent;
                    SKColor ring = TokenBridge.Current.FindMatchRing;
                    Assert.NotEqual(ring, accent);
                    node.IsFindMatch = true;
                    node.ShowsKeyboardFocus = true;
                    SKRect bounds = GraphBounds(node);
                    float laneA = bounds.Top - 3.5f, laneB = bounds.Top - 8.5f;

                    using SKSurface findOnly = Render(node, NodeDrawingStyle.Regular);
                    List<SKColor> before = Run(findOnly, bounds.Left + 14, laneA, 16);
                    Assert.DoesNotContain(accent, before);
                    Assert.Contains(ring, before);
                    Assert.Contains(before, c => c != ring);

                    node.IsPlacementFlashing = true;
                    node.PlacementFlashOpacity = 1;
                    using SKSurface flashing = Render(node, NodeDrawingStyle.Regular);

                    Assert.All(Run(flashing, bounds.Left + 14, laneA, 16), c => Assert.Equal(accent, c));
                    Assert.Equal(TokenBridge.Current.FocusRing, Run(flashing, bounds.Left + 14, laneB, 1).Single());
                    Assert.True(node.FocusRing()!.Value.Rect.Top < node.PlacementFlashRing().Rect.Top);
                } finally {
                    app.InstallAppearance(null);
                    TokenBridge.Refresh();
                }
            }
        }

        [AvaloniaFact]
        public async Task NonMatches_AreNotDimmed() {
            (MainWindow window, List<BaseNodeElement> nodes) = await WindowWithItems("iron-plate", "iron-ore");

            window.Toolbar.SearchField.Text = "plate";
            Dispatcher.UIThread.RunJobs();

            Assert.True(nodes[0].IsFindMatch);
            Assert.False(nodes[1].IsFindMatch);
            Assert.All(nodes, n => Assert.Equal(1f, n.ContentOpacity));
        }
    }
}
