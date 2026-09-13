using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public class GlyphTests {
        private static ControlTheme KitGlyphTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Geometry GlyphGeometry(string name, int size) {
            Assert.True(Application.Current!.TryGetResource($"Kit.Glyph.{name}.{size}", null, out object? value));
            return Assert.IsAssignableFrom<Geometry>(value);
        }

        private static Path Themed(string name, int size, params string[] classes) {
            var path = new Path { Theme = KitGlyphTheme(), Data = GlyphGeometry(name, size) };
            foreach (string cls in classes)
                path.Classes.Add(cls);
            return path;
        }

        [AvaloniaFact]
        public void KitGlyph_IsAControlThemeTargetingPath() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Path), KitGlyphTheme().TargetType);
        }

        [AvaloniaFact]
        public void KitGlyph_InheritsTheOnePointFiveStrokeAndRoundCapsFromGlyphIcon() {
            using var fixture = new KitFixture();
            var path = Themed("Recipe", 20, "size20");
            fixture.Window.Content = path;

            Assert.Equal(1.5, path.StrokeThickness);
            Assert.Equal(PenLineCap.Round, path.StrokeLineCap);
            Assert.Equal(PenLineJoin.Round, path.StrokeJoin);
        }

        public static IEnumerable<object[]> Sizes() {
            yield return [16, "Foreman.Size.Glyph16"];
            yield return [17, "Foreman.Size.Glyph17"];
            yield return [18, "Foreman.Size.Glyph18"];
            yield return [20, "Foreman.Size.Glyph20"];
        }

        [AvaloniaTheory]
        [MemberData(nameof(Sizes))]
        public void SizeClass_BindsWidthAndHeightToItsScaleContractToken(int size, string sizeKey) {
            using var fixture = new KitFixture();
            var path = Themed("Recipe", size, $"size{size}");
            fixture.Window.Content = path;

            Application.Current!.TryGetResource(sizeKey, Application.Current.ActualThemeVariant, out object? value);

            Assert.Equal(Assert.IsType<double>(value), path.Width);
            Assert.Equal(Assert.IsType<double>(value), path.Height);
        }

        [AvaloniaFact]
        public void SecondaryClass_SetsStrokeToTextSecondaryToken() {
            using var fixture = new KitFixture();
            var path = Themed("Recipe", 20, "size20", "secondary");
            fixture.Window.Content = path;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.TextSecondary")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(path.Stroke).Color);
        }

        [AvaloniaFact]
        public void EmphasisClass_SetsStrokeToTextToken() {
            using var fixture = new KitFixture();
            var path = Themed("Recipe", 20, "size20", "emphasis");
            fixture.Window.Content = path;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.Text")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(path.Stroke).Color);
        }

        [AvaloniaFact]
        public void FillClass_ClearsStrokeAndFillsWithTextTertiaryToken() {
            using var fixture = new KitFixture();
            var path = Themed("View.Fill", 20, "size20", "fill");
            fixture.Window.Content = path;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.TextTertiary")).Color;
            Assert.Null(path.Stroke);
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(path.Fill).Color);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class GlyphScaleTests {
        [AvaloniaFact]
        public void SizeTwentyClass_WidthAndHeight_StayAtTheMasterSizeRegardlessOfScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph", null, out object? theme));
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph.Recipe.20", null, out object? geometry));
            var path = new Path { Theme = Assert.IsType<ControlTheme>(theme), Data = Assert.IsAssignableFrom<Geometry>(geometry) };
            path.Classes.Add("size20");
            fixture.Window.Content = path;

            //The class Setter is a fixed master box; a scaling consumer wraps this Path in a Viewbox.
            ScaleContract.Apply(1.5);

            Assert.Equal(20.0, path.Width);
            Assert.Equal(20.0, path.Height);
        }
    }
}
