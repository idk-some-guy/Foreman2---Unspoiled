using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
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

        //Composites addendum §10/§14: Plus/Minus land as their own 12pt masters, SlotAdd is the pick
        //cell's own name for Plus, one geometry, two keys.
        [AvaloniaFact]
        public void PlusAndMinus_ExistAtTwelvePoints() {
            using var fixture = new KitFixture();
            Assert.NotNull(GlyphGeometry("Plus", 12));
            Assert.NotNull(GlyphGeometry("Minus", 12));
        }

        [AvaloniaFact]
        public void SlotAdd_AndPlus_ResolveToTheSameGeometryBounds() {
            using var fixture = new KitFixture();
            Assert.Equal(GlyphGeometry("Plus", 12).Bounds, GlyphGeometry("SlotAdd", 12).Bounds);
        }

        [AvaloniaFact]
        public void SlotAdd_AndMinus_AreDifferentGeometry() {
            using var fixture = new KitFixture();
            Assert.NotEqual(GlyphGeometry("Minus", 12).Bounds, GlyphGeometry("SlotAdd", 12).Bounds);
        }
    }

    public class CanvasGlyphTests {
        public static IEnumerable<object[]> CanvasMasters() {
            yield return ["Node.Over", "M10,16 V4.5 M5.5,9 L10,4.5 L14.5,9", "M5,8 V2.25 M2.75,4.5 L5,2.25 L7.25,4.5"];
            yield return ["Node.Short", "M10,4 V15.5 M5.5,11 L10,15.5 L14.5,11", "M5,2 V7.75 M2.75,5.5 L5,7.75 L7.25,5.5"];
            yield return ["Node.Error", "M10,4 V11.5 M10,15.6 V15.7", "M5,2 V5.75 M5,7.8 V7.85"];
            yield return ["Node.Key", "M2.8,10 A3.2,3.2 0 1,0 9.2,10 A3.2,3.2 0 1,0 2.8,10 Z M9.2,10 H17 M14,10 V13 M16.5,10 V12.5",
                "M1.4,5 A1.6,1.6 0 1,0 4.6,5 A1.6,1.6 0 1,0 1.4,5 Z M4.6,5 H8.5 M7,5 V6.5 M8.25,5 V6.25"];
            yield return ["Canvas.Paused", "M7.5,5 V15 M12.5,5 V15", null!];
            yield return ["Link.Orthogonal", "M3,15 H9 V5 H17 M7.2,10 A1.8,1.8 0 1,0 10.8,10 A1.8,1.8 0 1,0 7.2,10 Z", null!];
            yield return ["Link.Rounded", "M3,15 C9,15 11,5 17,5", null!];
        }

        private static string RepoRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string? GlyphText(string key) {
            XDocument glyphs = XDocument.Load(System.IO.Path.Combine(RepoRoot, "src", "Foreman.Mac.Kit", "Themes", "Glyphs.axaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            return glyphs.Descendants().SingleOrDefault(e => (string?)e.Attribute(x + "Key") == key)?.Value;
        }

        private static bool TryGeometry(string key, out Geometry? geometry) {
            bool found = Application.Current!.TryGetResource(key, null, out object? value);
            geometry = value as Geometry;
            return found && geometry is not null;
        }

        private static void AssertRectClose(Rect expected, Rect actual, string key) {
            const double tolerance = 0.01;
            Assert.True(Math.Abs(expected.X - actual.X) < tolerance && Math.Abs(expected.Y - actual.Y) < tolerance
                && Math.Abs(expected.Width - actual.Width) < tolerance && Math.Abs(expected.Height - actual.Height) < tolerance,
                $"{key}: expected {expected}, got {actual}");
        }

        [AvaloniaTheory]
        [MemberData(nameof(CanvasMasters))]
        public void CanvasGlyphs_ResolveOnTheTwentyGrid_AndBadgeGlyphsShipATenPointMaster(string name, string addendumPath, string? badgePath) {
            using var fixture = new KitFixture();
            Rect expected = StreamGeometry.Parse(addendumPath).Bounds;
            Assert.Equal(addendumPath, GlyphText($"Kit.Glyph.{name}.20"));
            Assert.Equal(badgePath, GlyphText($"Kit.Glyph.{name}.10"));

            Assert.True(TryGeometry($"Kit.Glyph.{name}.20", out Geometry? master), name);
            AssertRectClose(expected, master!.Bounds, $"{name}.20");
            Assert.True(master.Bounds.Left >= 0 && master.Bounds.Top >= 0 && master.Bounds.Right <= 20 && master.Bounds.Bottom <= 20, name);

            if (badgePath is not null) {
                Assert.True(TryGeometry($"Kit.Glyph.{name}.10", out Geometry? badge), name);
                AssertRectClose(new Rect(expected.X / 2, expected.Y / 2, expected.Width / 2, expected.Height / 2), badge!.Bounds, $"{name}.10");
            } else {
                Assert.False(TryGeometry($"Kit.Glyph.{name}.10", out _), name);
            }
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
