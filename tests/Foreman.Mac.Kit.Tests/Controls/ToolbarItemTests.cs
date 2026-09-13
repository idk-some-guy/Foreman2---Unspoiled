using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public class ToolbarItemTests {
        private static ControlTheme KitToolbarItemTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ToolbarItem", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Geometry RecipeGlyph() {
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph.Recipe.17", null, out object? value));
            return Assert.IsAssignableFrom<Geometry>(value);
        }

        private static ToolbarItem Themed(string label) =>
            new() { Theme = KitToolbarItemTheme(), Glyph = RecipeGlyph(), Label = label };

        [AvaloniaFact]
        public void KitToolbarItem_IsAControlThemeTargetingToolbarItem() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ToolbarItem), KitToolbarItemTheme().TargetType);
        }

        [AvaloniaFact]
        public void NormalState_HeightAndHitTargetWidthComeFromTokens() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            Application.Current!.TryGetResource("Foreman.Size.ToolbarItemHeight", Application.Current.ActualThemeVariant, out object? height);
            Application.Current!.TryGetResource("Foreman.Size.ToolbarItemWidth", Application.Current.ActualThemeVariant, out object? width);

            Assert.Equal(Assert.IsType<double>(height), item.Height);
            Assert.Equal(Assert.IsType<double>(width), item.MinWidth);
        }

        [AvaloniaFact]
        public void NormalState_ForegroundIsTextSecondary() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), item.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_UsesControlBackgroundHoverAndTextForeground() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            fixture.StateOf(item).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), item.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), item.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Pressed_UsesControlBackgroundPressed() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            fixture.StateOf(item).Hover();
            fixture.StateOf(item).Press();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundPressed").ToImmutable(), item.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void On_UsesAccentSoftFillAndAccentForeground() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            item.IsOn = true;

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), item.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), item.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void On_GlyphStrokeIsAccent() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            item.IsOn = true;

            Path path = item.GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), path.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            item.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), item.Opacity);
        }

        [AvaloniaFact]
        public void Label_UsesTheLegacyToolbarItemLabelToken() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            TextBlock text = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Recipe");

            Application.Current!.TryGetResource("Foreman.Type.Legacy.ToolbarItem.Label.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.Legacy.ToolbarItem.Label.FontWeight", Application.Current.ActualThemeVariant, out object? weight);

            Assert.Equal(Assert.IsType<double>(size), text.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), text.FontWeight);
        }

        [AvaloniaFact]
        public void Glyph_FlowsIntoTheTemplatedGlyphPath() {
            using var fixture = new KitFixture();
            Geometry glyph = RecipeGlyph();
            ToolbarItem item = new() { Theme = KitToolbarItemTheme(), Glyph = glyph, Label = "Recipe" };
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            Path path = item.GetVisualDescendants().OfType<Path>().Single();
            Assert.Same(glyph, path.Data);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsTheFocusRing() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            fixture.StateOf(item).Focus();

            Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(item));
        }

        [AvaloniaFact]
        public void SpaceKey_WhileFocused_RaisesClick() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            bool clicked = false;
            item.Click += (_, _) => clicked = true;
            fixture.Window.Content = item;

            fixture.StateOf(item).Focus();
            fixture.StateOf(item).KeyDown(Key.Space);
            fixture.StateOf(item).KeyUp(Key.Space);
            Dispatcher.UIThread.RunJobs();

            Assert.True(clicked);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class ToolbarItemScaleTests {
        [AvaloniaFact]
        public void WidthAndHeight_TrackScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.ToolbarItem", null, out object? theme));
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph.Recipe.17", null, out object? glyph));
            var item = new ToolbarItem { Theme = Assert.IsType<ControlTheme>(theme), Glyph = Assert.IsAssignableFrom<Geometry>(glyph), Label = "Recipe" };
            fixture.Window.Content = item;

            Assert.Equal(40, item.Height);
            Assert.Equal(44, item.MinWidth);

            ScaleContract.Apply(1.5);

            Assert.Equal(60, item.Height);
            Assert.Equal(66, item.MinWidth);
        }

        [AvaloniaFact]
        public void GlyphViewbox_TracksScaleContractWhileThePathStaysAtItsMasterSize() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.ToolbarItem", null, out object? theme));
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph.Recipe.17", null, out object? glyph));
            var item = new ToolbarItem { Theme = Assert.IsType<ControlTheme>(theme), Glyph = Assert.IsAssignableFrom<Geometry>(glyph), Label = "Recipe" };
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            Viewbox box = item.GetVisualDescendants().OfType<Viewbox>().Single();
            Path path = item.GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(17, box.Bounds.Width);
            Assert.Equal(17, path.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(26, box.Bounds.Width);
            Assert.Equal(17, path.Bounds.Width);
        }
    }
}
