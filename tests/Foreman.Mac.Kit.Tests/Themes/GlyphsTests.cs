using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Kit.Appearance;
using System;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Themes {
    public class GlyphsTests {
        private static readonly string[] GlyphNames = [
            "New", "Open", "Save", "Item", "Recipe", "Connect", "Align", "Export", "Summary",
            "View", "View.Fill", "Passthrough", "Beacon", "Beacon.Fill",
            "Chevron", "Search",
            "RadialAdd", "RadialSource", "RadialOutput", "RadialSpoil", "RadialUnspoil",
            "RadialPlant", "RadialUnplant", "RadialCancel",
            "Settings.Presets", "Settings.EnabledObjects", "Settings.General",
            "Settings.Advanced", "Settings.Annotations", "Settings.Appearance",
            "Category.Mods", "Category.Items", "Category.Recipes", "Category.Assemblers",
            "Category.Miners", "Category.Power", "Category.Modules",
            "Sync", "Research", "Revert",
        ];

        private static readonly int[] Sizes = [16, 17, 18, 20];

        private static void MergeKitIntoApplication() {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };
            Application.Current!.Resources.MergedDictionaries.Add(kit);
        }

        private static Geometry ResourceGeometry(string key) {
            Assert.True(Application.Current!.TryGetResource(key, null, out object? value), key);
            return Assert.IsAssignableFrom<Geometry>(value);
        }

        [AvaloniaFact]
        public void AllGlyphKeys_ExistAndParseAsGeometry() {
            MergeKitIntoApplication();

            foreach (string name in GlyphNames)
                foreach (int size in Sizes)
                    ResourceGeometry($"Kit.Glyph.{name}.{size}");
        }

        [AvaloniaFact]
        public void Glyphs_ResearchSet_ShippedAtEverySize() {
            MergeKitIntoApplication();

            foreach (string name in new[] { "Sync", "Research", "Revert" })
                foreach (int size in new[] { 12, 16, 17, 18, 20 })
                    ResourceGeometry($"Kit.Glyph.{name}.{size}");
        }

        public static IEnumerable<object[]> AllKeys() {
            foreach (string name in GlyphNames)
                foreach (int size in Sizes)
                    yield return [name, size];
        }

        [AvaloniaTheory]
        [MemberData(nameof(AllKeys))]
        public void EveryGeometry_FitsTheLiveArea_ForItsDrawnSize(string name, int size) {
            MergeKitIntoApplication();
            Geometry geometry = ResourceGeometry($"Kit.Glyph.{name}.{size}");

            double margin = size * 1.5 / 20.0;
            const double tolerance = 0.5;
            Rect bounds = geometry.Bounds;

            Assert.True(bounds.Left >= margin - tolerance, $"{name}.{size} left {bounds.Left}");
            Assert.True(bounds.Top >= margin - tolerance, $"{name}.{size} top {bounds.Top}");
            Assert.True(bounds.Right <= size - margin + tolerance, $"{name}.{size} right {bounds.Right}");
            Assert.True(bounds.Bottom <= size - margin + tolerance, $"{name}.{size} bottom {bounds.Bottom}");
        }

        [AvaloniaFact]
        public void GlyphIconTheme_StretchIsNoneSoTheDrawingNeverInflatesPastItsMasterLiveArea() {
            MergeKitIntoApplication();
            Assert.True(Application.Current!.TryGetResource("Kit.GlyphIcon", null, out object? theme));
            var controlTheme = Assert.IsType<ControlTheme>(theme);

            var path = new Path { Theme = controlTheme, Data = ResourceGeometry("Kit.Glyph.Recipe.20") };
            path.Classes.Add("size20");
            var window = new Window { Content = path };
            window.Show();

            Assert.Equal(Stretch.None, path.Stretch);
            Assert.Equal(path.Data!.Bounds, path.RenderedGeometry!.Bounds);
        }

        [AvaloniaTheory]
        [InlineData("size16", 16)]
        [InlineData("size17", 17)]
        [InlineData("size18", 18)]
        [InlineData("size20", 20)]
        public void GlyphIconTheme_SizeClassSetsTheMasterBoxRegardlessOfScaleContract(string sizeClass, double masterSize) {
            MergeKitIntoApplication();
            Assert.True(Application.Current!.TryGetResource("Kit.GlyphIcon", null, out object? theme));
            var controlTheme = Assert.IsType<ControlTheme>(theme);

            var path = new Path { Theme = controlTheme, Data = ResourceGeometry($"Kit.Glyph.Recipe.{(int)masterSize}") };
            path.Classes.Add(sizeClass);
            var window = new Window { Content = path };
            window.Show();

            try {
                Assert.Equal(masterSize, path.Width);
                Assert.Equal(masterSize, path.Height);

                ScaleContract.Apply(1.5);
                window.UpdateLayout();

                Assert.Equal(masterSize, path.Width);
                Assert.Equal(masterSize, path.Height);
            } finally {
                ScaleContract.Apply(1.0);
            }
        }

        [AvaloniaFact]
        public void GlyphIconTheme_AppliesOnePointFiveStrokeAndRoundCaps() {
            MergeKitIntoApplication();
            Assert.True(Application.Current!.TryGetResource("Kit.GlyphIcon", null, out object? theme));
            var controlTheme = Assert.IsType<ControlTheme>(theme);

            var path = new Path { Theme = controlTheme, Data = ResourceGeometry("Kit.Glyph.Recipe.20") };
            path.Classes.Add("size20");
            var window = new Window { Content = path };
            window.Show();

            Assert.Equal(1.5, path.StrokeThickness);
            Assert.Equal(PenLineCap.Round, path.StrokeLineCap);
            Assert.Equal(PenLineJoin.Round, path.StrokeJoin);
        }

        [AvaloniaFact]
        public void RecipeTwenty_RendersInHeadlessWindow_WithExpectedBounds() {
            MergeKitIntoApplication();
            Application.Current!.TryGetResource("Kit.GlyphIcon", null, out object? theme);

            var path = new Path {
                Theme = Assert.IsType<ControlTheme>(theme),
                Data = ResourceGeometry("Kit.Glyph.Recipe.20"),
            };
            path.Classes.Add("size20");
            var window = new Window { Content = path };
            window.Show();

            Assert.Equal(20, path.Bounds.Width);
            Assert.Equal(20, path.Bounds.Height);
        }
    }
}
