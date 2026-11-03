using Avalonia;
using Avalonia.Headless.XUnit;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Serialization;
using SkiaSharp;
using System;
using Xunit;
using static Foreman.Mac.UiTests.support.PixelColors;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public class CanvasPainterTokenTests {
        private const int Half = 200;

        private static void InBothThemes(Action<CanvasTokens> check) {
            App app = (App)Application.Current!;
            try {
                foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark }) {
                    app.ApplyTheme(mode);
                    TokenBridge.Refresh();
                    check(TokenBridge.Current);
                }
            } finally {
                app.ApplyTheme(ThemeMode.System);
                TokenBridge.Refresh();
            }
        }

        private static SKColor Pixel(SKSurface surface, int x, int y) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(Half + x, Half + y);
        }

        private static ShapeAnnotationElement SelectedShape() {
            var shape = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 150, ShapeType = "Rectangle",
                FillColor = new ColorSaveData(0, 0, 0, 0), BorderColor = new ColorSaveData(0, 0, 0, 0), BorderWidth = 0
            });
            shape.IsSelected = true;
            shape.Context = new AnnotationElementContext { ViewScale = () => 0.3f, SelectedAnnotations = [], SelectedNodeCount = () => 0 };
            return shape;
        }

        [AvaloniaFact]
        public void AnnotationChrome_ReadsTokens_InBothAppearances() {
            InBothThemes(tokens => {
                using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
                surface.Canvas.Clear(tokens.CanvasBackground);
                surface.Canvas.Translate(Half, Half);
                SelectedShape().Paint(surface.Canvas, NodeDrawingStyle.Regular);

                Assert.Equal(tokens.WindowBackground.WithAlpha(255), Pixel(surface, -100, -75));
                Assert.Equal(tokens.Accent.WithAlpha(255), Pixel(surface, -100, -91));
                Assert.Equal(tokens.NodeSelected.WithAlpha(255), Pixel(surface, -40, -83));
            });
        }

        [AvaloniaFact]
        public void FloatingTooltip_ReadsTokens_InBothAppearances() {
            InBothThemes(tokens => {
                using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
                surface.Canvas.Clear(tokens.CanvasBackground);
                surface.Canvas.Translate(Half, Half);
                FloatingTooltipRenderer.Draw(surface.Canvas, new TooltipInfo(new Point(20, 20), Direction.None, "Iron plate ore"));

                Assert.Equal(tokens.TooltipMaterialOpaque.WithAlpha(255), Pixel(surface, 20, 24));
                AssertClose(Over(tokens.NodeTabBorder, tokens.CanvasBackground), Pixel(surface, 19, 24));

                using SKSurface pointed = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
                pointed.Canvas.Clear(tokens.CanvasBackground);
                pointed.Canvas.Translate(Half, Half);
                FloatingTooltipRenderer.Draw(pointed.Canvas, new TooltipInfo(new Point(100, 100), Direction.Down, "Iron plate ore"));

                Assert.Equal(tokens.TooltipMaterialOpaque.WithAlpha(255), Pixel(pointed, 100, 96));
            });
        }
    }
}
