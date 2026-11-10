using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman.Mac.Canvas;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class PausedCapsuleTests {
        private static PausedCapsule Shown(out Window window) {
            var control = new GraphCanvasControl();
            window = new Window { Content = control, Width = 300, Height = 200 };
            window.Show();
            control.Viewer.IsPaused = true;
            return control.GetVisualDescendants().OfType<PausedCapsule>().Single();
        }

        private static double Token(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            return Assert.IsType<double>(value);
        }

        [AvaloniaFact]
        public void Capsule_PadsFromTheCapsuleSpaceTokens_AndDrawsItsGlyphTwelvePointsWithStroke2Point4_RoundCapped() {
            PausedCapsule capsule = Shown(out Window window);
            try {
                Assert.Equal(8, Token("Foreman.Space.CapsuleLeading"));
                Assert.Equal(10, Token("Foreman.Space.CapsuleTrailing"));
                Assert.Equal(new Thickness(Token("Foreman.Space.CapsuleLeading"), 0, Token("Foreman.Space.CapsuleTrailing"), 0), capsule.Padding);
                Assert.Equal(22, capsule.Height);

                Viewbox box = ((StackPanel)capsule.Content!).Children.OfType<Viewbox>().Single();
                var glyph = (Path)box.Child!;
                window.UpdateLayout();

                Assert.Equal(Token("Foreman.Size.GlyphMicro"), box.Bounds.Width, 0.01);
                Assert.Equal(20, glyph.Width);
                Assert.Equal(2.4, glyph.StrokeThickness, 0.001);
                Assert.Equal(PenLineCap.Round, glyph.StrokeLineCap);
                Assert.Equal(1.44, glyph.StrokeThickness * box.Bounds.Width / glyph.Width, 0.01);
            } finally {
                window.Close();
            }
        }
    }
}
