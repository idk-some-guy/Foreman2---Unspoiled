using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public class GridBackgroundTests {
        private const int Half = 50;
        private static readonly Rectangle Visible = new(-Half, -Half, 2 * Half, 2 * Half);

        private sealed class HighContrastAppearance(PlatformThemeVariant variant) : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.High;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => variant;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        private static string AppSource(params string[] relative) =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Foreman.Mac", Path.Combine(relative)));

        private static SKColor ResolveSk(string key, ThemeVariant variant) {
            AvColor color = ThemeResources.ResolveColor(key, variant, default);
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        private static SKSurface NewSurface(float scale = 1f, float offsetY = 0f) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(TokenBridge.Current.CanvasBackground);
            surface.Canvas.Translate(Half, Half + offsetY);
            surface.Canvas.Scale(scale);
            return surface;
        }

        private static SKColor Pixel(SKSurface surface, int screenX, int screenY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(screenX, screenY);
        }

        private static int CountNot(SKSurface surface, SKColor ground) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            int count = 0;
            for (int y = 0; y < pixmap.Height; y++)
                for (int x = 0; x < pixmap.Width; x++)
                    if (pixmap.GetPixelColor(x, y) != ground)
                        count++;
            return count;
        }

        private static int CountNotOffAxes(SKSurface surface, SKColor ground) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            int count = 0;
            for (int y = 0; y < pixmap.Height; y++)
                for (int x = 0; x < pixmap.Width; x++)
                    if (Math.Abs(x - Half) > 1 && Math.Abs(y - Half) > 1 && pixmap.GetPixelColor(x, y) != ground)
                        count++;
            return count;
        }

        private static bool DotAt(SKSurface surface, int screenX, int screenY, SKColor ground) =>
            Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => Pixel(surface, screenX + dx, screenY + dy))).Any(pixel => pixel != ground);

        private static SKColor Over(SKColor top, SKColor bottom) {
            float a = top.Alpha / 255f;
            byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
            return new SKColor(Mix(top.Red, bottom.Red), Mix(top.Green, bottom.Green), Mix(top.Blue, bottom.Blue), 255);
        }

        [AvaloniaFact]
        public void GridManager_ExposesMinorAndMajorDotPaints_BorderAndBorderStrong_ContrastSwapsToBorderStrongAndNodeTabBorder_NoDrawLineForCells() {
            App app = (App)Application.Current!;
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            AppearanceHub? hub = null;
            try {
                foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark }) {
                    app.ApplyTheme(mode);
                    TokenBridge.Refresh();
                    ThemeVariant variant = Application.Current!.ActualThemeVariant;
                    using var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 10, CurrentMajorGridUnit = 40 };
                    using SKSurface surface = NewSurface();
                    grid.Paint(surface.Canvas, 1f, Visible);

                    Assert.Equal(ResolveSk("Foreman.Color.Border", variant), grid.MinorDotPaint.Color);
                    Assert.Equal(ResolveSk("Foreman.Color.BorderStrong", variant), grid.MajorDotPaint.Color);
                    Assert.False(DotAt(surface, Half + 5, Half + 20, TokenBridge.Current.CanvasBackground), "a dot grid leaves the space between dots on the ground");
                }

                app.ApplyTheme(ThemeMode.Light);
                hub = new AppearanceHub(app, new HighContrastAppearance(PlatformThemeVariant.Light));
                AppearanceHub.Current = hub;
                hub.Refresh();
                TokenBridge.Refresh();
                using var contrastGrid = new GridManager { ShowGrid = true, CurrentGridUnit = 10, CurrentMajorGridUnit = 40 };
                using SKSurface contrastSurface = NewSurface();
                contrastGrid.Paint(contrastSurface.Canvas, 1f, Visible);

                Assert.True(TokenBridge.Current.IncreaseContrast);
                Assert.Equal(ResolveSk("Foreman.Color.BorderStrong", ThemeVariant.Light), contrastGrid.MinorDotPaint.Color);
                Assert.Equal(ResolveSk("Foreman.Color.NodeTabBorderContrast", ThemeVariant.Light), contrastGrid.MajorDotPaint.Color);
            } finally {
                hub?.Dispose();
                AppearanceHub.Current = previousCurrent;
                app.ApplyTheme(ThemeMode.System);
                TokenBridge.Refresh();
            }
        }

        [AvaloniaFact]
        public void MinorUnit6_Zoom1_DrawsNoMinorDots_Zoom2_DoesDraw() {
            SKColor ground = TokenBridge.Current.CanvasBackground;
            using var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 6 };

            using SKSurface atOne = NewSurface();
            grid.Paint(atOne.Canvas, 1f, Visible);
            Assert.Equal(0, CountNotOffAxes(atOne, ground));

            using SKSurface atTwo = NewSurface(scale: 2f);
            grid.Paint(atTwo.Canvas, 2f, new Rectangle(-Half / 2, -Half / 2, Half, Half));
            Assert.True(DotAt(atTwo, Half + 12, Half + 12, ground));
            Assert.False(DotAt(atTwo, Half + 6, Half + 12, ground));
        }

        [AvaloniaFact]
        public void ShowGridlinesOff_NoDotsNoAxes_GroundStays() {
            SKColor ground = TokenBridge.Current.CanvasBackground;
            using var grid = new GridManager { ShowGrid = false, CurrentGridUnit = 10, CurrentMajorGridUnit = 40 };
            using SKSurface off = NewSurface();
            grid.Paint(off.Canvas, 1f, Visible);
            Assert.Equal(0, CountNot(off, ground));

            grid.ShowGrid = true;
            grid.CurrentGridUnit = 0;
            grid.CurrentMajorGridUnit = 0;
            using SKSurface on = NewSurface();
            grid.Paint(on.Canvas, 1f, Visible);
            Assert.True(DotAt(on, Half, Half + 20, ground));
            Assert.True(DotAt(on, Half + 20, Half, ground));
            Assert.False(DotAt(on, Half + 20, Half + 20, ground));
        }

        [AvaloniaFact]
        public void BackgroundColor_IsCanvasBackground_InEachTheme_NoSKColorLiteralInGridManagerOrGraphViewer() {
            var viewer = new GraphViewer(new Viewport(100, 100), new GridManager());

            viewer.ApplyTheme(dark: true);
            Assert.Equal(ResolveSk("Foreman.Color.CanvasBackground", ThemeVariant.Dark), viewer.BackgroundColor);
            using (SKSurface dark = SKSurface.Create(new SKImageInfo(100, 100))) {
                viewer.Paint(dark.Canvas);
                Assert.Equal(viewer.BackgroundColor, Pixel(dark, 5, 5));
            }

            viewer.ApplyTheme(dark: false);
            Assert.Equal(ResolveSk("Foreman.Color.CanvasBackground", ThemeVariant.Light), viewer.BackgroundColor);
            using (SKSurface light = SKSurface.Create(new SKImageInfo(100, 100))) {
                viewer.Paint(light.Canvas);
                Assert.Equal(viewer.BackgroundColor, Pixel(light, 5, 5));
            }

            foreach (string file in new[] { "GridManager.cs", "GraphViewer.cs" }) {
                string text = File.ReadAllText(AppSource("Canvas", file));
                Assert.DoesNotContain("SKColors.", text);
                Assert.DoesNotMatch(@"new SKColor\(\s*\d", text);
            }
        }

        [AvaloniaFact]
        public void Paused_ViewportFrameIsWarning2_CapsuleReadsCalculationsPaused() {
            var control = new GraphCanvasControl();
            var window = new Window { Content = control, Width = 300, Height = 200 };
            window.Show();
            PausedCapsule capsule = control.GetVisualDescendants().OfType<PausedCapsule>().Single();
            Assert.False(capsule.IsVisible);

            control.Viewer.IsPaused = true;

            Assert.True(capsule.IsVisible);
            Assert.Equal("Calculations paused", ((StackPanel)capsule.Content!).Children.OfType<TextBlock>().Single().Text);
            Assert.Equal("Calculations paused", AutomationProperties.GetName(capsule));
            Assert.Equal("Resumes calculations.", AutomationProperties.GetHelpText(capsule));
            Assert.Equal(22, capsule.Height);
            Assert.Equal(12, capsule.Margin.Top);
            using (SKSurface frame = SKSurface.Create(new SKImageInfo(300, 200))) {
                control.Viewport.SetSize(300, 200);
                control.Render(frame.Canvas);
                Assert.Equal(TokenBridge.Current.Warning, Pixel(frame, 0, 100));
                Assert.Equal(TokenBridge.Current.Warning, Pixel(frame, 1, 100));
                Assert.NotEqual(TokenBridge.Current.Warning, Pixel(frame, 2, 100));
            }
            Assert.Equal(TokenBridge.Current.Warning, control.Viewer.PausedFramePaint.Color);
            Assert.Equal(2f, control.Viewer.PausedFramePaint.StrokeWidth);
        }

        [AvaloniaFact]
        public void IncreaseContrastInDark_MinorIsBorderStrong_MajorIsNodeTabBorderContrast() {
            App app = (App)Application.Current!;
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            AppearanceHub? hub = null;
            try {
                app.ApplyTheme(ThemeMode.Dark);
                hub = new AppearanceHub(app, new HighContrastAppearance(PlatformThemeVariant.Dark));
                AppearanceHub.Current = hub;
                hub.Refresh();
                TokenBridge.Refresh();
                using var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 10, CurrentMajorGridUnit = 40 };
                using SKSurface surface = NewSurface();
                grid.Paint(surface.Canvas, 1f, Visible);

                Assert.True(TokenBridge.Current.IncreaseContrast);
                Assert.Equal(ResolveSk("Foreman.Color.BorderStrong", ThemeVariant.Dark), grid.MinorDotPaint.Color);
                Assert.Equal(ResolveSk("Foreman.Color.NodeTabBorderContrast", ThemeVariant.Dark), grid.MajorDotPaint.Color);
            } finally {
                hub?.Dispose();
                AppearanceHub.Current = previousCurrent;
                app.ApplyTheme(ThemeMode.System);
                TokenBridge.Refresh();
            }
        }

        private const int ControlSize = 200;

        private static GraphCanvasControl NewRenderedControl(bool showGrid) {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(ControlSize, ControlSize);
            control.Grid.ShowGrid = showGrid;
            control.Grid.CurrentGridUnit = 20;
            control.Grid.CurrentMajorGridUnit = 0;
            return control;
        }

        private static bool NearlyEqual(SKColor a, SKColor b) =>
            Math.Abs(a.Red - b.Red) <= 1 && Math.Abs(a.Green - b.Green) <= 1 && Math.Abs(a.Blue - b.Blue) <= 1;

        private static bool AnyPixelNear(SKSurface surface, AvaloniaPoint at, SKColor expected, int reach = 1) {
            for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                    if (NearlyEqual(Pixel(surface, (int)Math.Floor(at.X) + dx, (int)Math.Floor(at.Y) + dy), expected))
                        return true;
            return false;
        }

        [AvaloniaFact]
        public void OriginAxes_FollowShowGridlines_DrawnAsBorderStrongOverGroundAtGraphZero() {
            SKColor expected = Over(TokenBridge.Current.BorderStrong, TokenBridge.Current.CanvasBackground);

            GraphCanvasControl on = NewRenderedControl(showGrid: true);
            using SKSurface withGrid = SKSurface.Create(new SKImageInfo(ControlSize, ControlSize));
            on.Render(withGrid.Canvas);
            Assert.True(AnyPixelNear(withGrid, on.Viewport.GraphToScreen(new System.Drawing.Point(0, 10)), expected), "vertical axis at graph x = 0");
            Assert.True(AnyPixelNear(withGrid, on.Viewport.GraphToScreen(new System.Drawing.Point(10, 0)), expected), "horizontal axis at graph y = 0");

            GraphCanvasControl off = NewRenderedControl(showGrid: false);
            using SKSurface withoutGrid = SKSurface.Create(new SKImageInfo(ControlSize, ControlSize));
            off.Render(withoutGrid.Canvas);
            Assert.False(AnyPixelNear(withoutGrid, off.Viewport.GraphToScreen(new System.Drawing.Point(0, 10)), expected));
            Assert.False(AnyPixelNear(withoutGrid, off.Viewport.GraphToScreen(new System.Drawing.Point(10, 0)), expected));
        }

        [AvaloniaFact]
        public void ControlRender_GridOn_DrawsDotAtMinorMultiple_GroundBetweenDots() {
            SKColor ground = TokenBridge.Current.CanvasBackground;
            SKColor full = Over(TokenBridge.Current.Border, ground);
            GraphCanvasControl control = NewRenderedControl(showGrid: true);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(ControlSize, ControlSize));

            control.Render(surface.Canvas);

            AvaloniaPoint dot = control.Viewport.GraphToScreen(new System.Drawing.Point(40, 40));
            int[] xs = Covering(dot.X);
            int[] ys = Covering(dot.Y);
            SKColor[] centre = [.. xs.SelectMany(x => ys.Select(y => Pixel(surface, x, y)))];
            Assert.All(centre, pixel => Assert.True(Near(centre[0], pixel), $"{centre[0]} vs {pixel}"));
            Assert.True(StrictlyBetween(centre[0].Red, ground.Red, full.Red), $"red {centre[0]}");
            Assert.True(StrictlyBetween(centre[0].Green, ground.Green, full.Green), $"green {centre[0]}");
            Assert.True(StrictlyBetween(centre[0].Blue, ground.Blue, full.Blue), $"blue {centre[0]}");
            Assert.Equal(ground, Pixel(surface, xs[0] - 2, ys[0]));
            Assert.Equal(ground, Pixel(surface, xs[^1] + 2, ys[0]));
            Assert.Equal(ground, Pixel(surface, xs[0], ys[0] - 2));
            Assert.Equal(ground, Pixel(surface, xs[0], ys[^1] + 2));
        }

        private static int[] Covering(double coordinate) =>
            [.. Enumerable.Range((int)Math.Floor(coordinate) - 1, 3).Where(i => Math.Abs(i + 0.5 - coordinate) <= 0.5 + 1e-4)];

        private static bool Near(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) <= 1 && Math.Abs(a.Green - b.Green) <= 1 && Math.Abs(a.Blue - b.Blue) <= 1;

        private static bool StrictlyBetween(byte value, byte a, byte b) => value > Math.Min(a, b) && value < Math.Max(a, b);

        [AvaloniaFact]
        public void DotTile_FractionalTranslationAtNonIntegerScale_KeepsDotCentresAndCoverage() {
            SKColor ground = TokenBridge.Current.CanvasBackground;
            foreach (float shift in new[] { 0.25f, 0.5f }) {
                using SKSurface surface = SKSurface.Create(new SKImageInfo(120, 120));
                surface.Canvas.Clear(ground);
                surface.Canvas.Translate(30 + shift, 30 + shift);
                surface.Canvas.Scale(2.2f);
                using var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 8 };
                grid.Paint(surface.Canvas, 1.1f, new Rectangle(-14, -14, 54, 54));

                var coverages = new System.Collections.Generic.List<double>();
                foreach (System.Drawing.Point multiple in new[] { new System.Drawing.Point(8, 8), new System.Drawing.Point(16, 24), new System.Drawing.Point(32, 16) }) {
                    SKPoint device = surface.Canvas.TotalMatrix.MapPoint(multiple.X, multiple.Y);
                    (double cx, double cy, double total) = Centroid(surface, ground, device);
                    Assert.InRange(Math.Abs(cx - device.X), 0, 0.1);
                    Assert.InRange(Math.Abs(cy - device.Y), 0, 0.1);
                    coverages.Add(total);
                }

                Assert.InRange((coverages.Max() - coverages.Min()) / coverages.Max(), 0, 0.1);
            }
        }

        [AvaloniaFact]
        public void DotTile_OneToOneIntegerTranslation_DrawsDotOnTheFourPixelsAroundTheCorner() {
            SKColor ground = TokenBridge.Current.CanvasBackground;
            using SKSurface surface = NewSurface();
            using var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 10 };
            grid.Paint(surface.Canvas, 1f, Visible);

            int x = Half + 20;
            int y = Half + 20;
            SKColor corner = Pixel(surface, x - 1, y - 1);
            Assert.NotEqual(ground, corner);
            Assert.True(Near(corner, Pixel(surface, x, y - 1)));
            Assert.True(Near(corner, Pixel(surface, x - 1, y)));
            Assert.True(Near(corner, Pixel(surface, x, y)));
            Assert.Equal(ground, Pixel(surface, x + 1, y));
            Assert.Equal(ground, Pixel(surface, x, y + 1));
            Assert.Equal(ground, Pixel(surface, x - 2, y - 1));
            Assert.Equal(ground, Pixel(surface, x - 1, y - 2));
        }

        private static (double X, double Y, double Total) Centroid(SKSurface surface, SKColor ground, SKPoint around) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            double sum = 0, sx = 0, sy = 0;
            for (int y = (int)around.Y - 4; y <= (int)around.Y + 4; y++)
                for (int x = (int)around.X - 4; x <= (int)around.X + 4; x++) {
                    SKColor c = pixmap.GetPixelColor(x, y);
                    double weight = Math.Abs(c.Red - ground.Red) + Math.Abs(c.Green - ground.Green) + Math.Abs(c.Blue - ground.Blue);
                    sum += weight;
                    sx += weight * (x + 0.5);
                    sy += weight * (y + 0.5);
                }

            return (sx / sum, sy / sum, sum);
        }

        [AvaloniaFact]
        public void DotTile_TracksDevicePixelDensity_TwiceTheSideOnATwiceScaledCanvas() {
            using var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 10 };
            using SKSurface plain = NewSurface();
            grid.Paint(plain.Canvas, 1f, Visible);
            Assert.Equal(10, grid.MinorTileSide);

            using SKSurface retina = NewSurface(scale: 2f);
            grid.Paint(retina.Canvas, 1f, Visible);
            Assert.Equal(20, grid.MinorTileSide);
        }

        [AvaloniaFact]
        public void Paused_CapsuleSizesFollowScaledTokens_RadiusIsHalfTheHeight() {
            try {
                WindowScaler.SetScale(1.5);
                var control = new GraphCanvasControl();
                var window = new Window { Content = control, Width = 400, Height = 300 };
                window.Show();
                PausedCapsule capsule = control.GetVisualDescendants().OfType<PausedCapsule>().Single();
                control.Viewer.IsPaused = true;
                capsule.UpdateLayout();

                Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
                Application.Current.TryGetResource("Foreman.Size.GlyphMicro", Application.Current.ActualThemeVariant, out object? glyphSize);
                Assert.Equal((double)height!, capsule.Bounds.Height);
                Assert.Equal(33, capsule.Bounds.Height);
                Assert.Equal((double)height / 2, capsule.CornerRadius.TopLeft);
                Viewbox glyph = capsule.GetVisualDescendants().OfType<Viewbox>().Single();
                Assert.Equal((double)glyphSize!, glyph.Bounds.Width);
                Assert.Equal(18, glyph.Bounds.Width);
                TextBlock label = capsule.GetVisualDescendants().OfType<TextBlock>().Single();
                Avalonia.Point origin = label.TranslatePoint(default, capsule)!.Value;
                Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + label.Bounds.Width <= capsule.Bounds.Width && origin.Y + label.Bounds.Height <= capsule.Bounds.Height);
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        [AvaloniaFact]
        public void CanvasGroupValue_GainsCalculationsPaused() {
            var viewer = new GraphViewer(new Viewport(100, 100), new GridManager());
            Assert.Equal("", viewer.PausedValueSuffix);

            viewer.IsPaused = true;

            Assert.Equal(", calculations paused", viewer.PausedValueSuffix);
        }

        [AvaloniaFact]
        public void AxisLockGuide_IsAccentDashed4And4_OneScreenPoint() {
            SKColor ground = TokenBridge.Current.CanvasBackground;
            SKColor accent = TokenBridge.Current.Accent;
            using var grid = new GridManager { LockDragToAxis = true, DragOrigin = new System.Drawing.Point(0, 0) };
            using SKSurface surface = NewSurface(offsetY: 0.5f);

            grid.Paint(surface.Canvas, 1f, Visible, draggedNodeActive: true);

            Assert.Equal(accent, grid.AxisGuidePaint.Color);
            Assert.Equal(1f, grid.AxisGuidePaint.StrokeWidth);
            Assert.NotNull(grid.AxisGuidePaint.PathEffect);
            for (int x = 0; x <= 40; x++)
                Assert.Equal((x / 4) % 2 == 0 ? accent : ground, Pixel(surface, x, Half));

            using SKSurface zoomed = NewSurface(scale: 2f);
            grid.Paint(zoomed.Canvas, 2f, Visible, draggedNodeActive: true);
            Assert.Equal(0.5f, grid.AxisGuidePaint.StrokeWidth);

            using var other = new GridManager { LockDragToAxis = true, DragOrigin = new System.Drawing.Point(0, 0) };
            using SKSurface otherSurface = NewSurface();
            other.Paint(otherSurface.Canvas, 1f, Visible, draggedNodeActive: true);
            Assert.Equal(1f, other.AxisGuidePaint.StrokeWidth);
            Assert.Equal(0.5f, grid.AxisGuidePaint.StrokeWidth);
        }

        [AvaloniaFact]
        public void Lasso_IsAccentStrokeOverAccentSoft() {
            CanvasTokens tokens = TokenBridge.Current;
            var viewer = new GraphViewer(new Viewport(100, 100), new GridManager());
            viewer.Viewport.ViewScale = 1f;
            viewer.SelectionZone = new Rectangle(10, 10, 50, 50);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(100, 100));

            viewer.Paint(surface.Canvas);

            Assert.Equal(tokens.Accent, viewer.LassoStrokePaint.Color);
            Assert.Equal(tokens.AccentSoft, viewer.LassoFillPaint.Color);
            Assert.Equal(1f, viewer.LassoStrokePaint.StrokeWidth);
            SKColor interior = Over(tokens.AccentSoft, viewer.BackgroundColor);
            SKColor drawn = Pixel(surface, 80, 80);
            Assert.True(Math.Abs(interior.Red - drawn.Red) <= 1 && Math.Abs(interior.Green - drawn.Green) <= 1 && Math.Abs(interior.Blue - drawn.Blue) <= 1, $"expected {interior}, got {drawn}");
            Assert.NotEqual(Pixel(surface, 80, 80), Pixel(surface, 60, 80));
            Assert.NotEqual(viewer.BackgroundColor, Pixel(surface, 59, 80));
        }
    }
}
