using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class ToolbarItemTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

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

        private static Border Fill(ToolbarItem item) =>
            item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HoverFill");

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
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), Fill(item).Background?.ToImmutable());
            Assert.Equal(1, Fill(item).Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), item.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Pressed_UsesControlBackgroundPressed() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            fixture.StateOf(item).Hover();
            fixture.StateOf(item).Press();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundPressed").ToImmutable(), Fill(item).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void On_UsesAccentSoftFillAndAccentForeground() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;

            item.IsOn = true;
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), Fill(item).Background?.ToImmutable());
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
        public void Hover_CrossFadesAnOpacityOverlay_AndNeverAnimatesBackground() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(item.Transitions ?? [], t => t is BrushTransition);
            var restBackground = item.Background?.ToImmutable();
            var hoverTarget = fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable();

            fixture.StateOf(item).Hover();
            //Mid fade: the overlay is still transitioning over Motion.HoverDuration, not swapped in the same pass.
            AnimationClock.Settle(60);
            Assert.Equal(restBackground, item.Background?.ToImmutable());
            double midOpacity = Fill(item).Opacity;
            Assert.NotEqual(0, midOpacity);
            Assert.NotEqual(1, midOpacity);

            AnimationClock.Settle(300);
            Assert.Equal(restBackground, item.Background?.ToImmutable());
            Assert.Equal(1, Fill(item).Opacity);
            Assert.Equal(hoverTarget, Fill(item).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void ReduceMotion_KeepsTheOpacityOnlyHoverFill_BothDirections() {
            //A ControlTheme Setter's x:Static resolves once per merged dictionary, so each direction needs its own fixture (a fresh dictionary) to re-resolve under the current flag.
            Motion.ReduceMotion = true;
            using (var reducedFixture = new KitFixture()) {
                ToolbarItem reduced = Themed("Recipe");
                reducedFixture.Window.Content = reduced;
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(Fill(reduced).Transitions!, t => t is BrushTransition);
                DoubleTransition opacity = Assert.Single(Fill(reduced).Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
                Assert.Equal(Motion.HoverDuration, opacity.Duration);
            }

            Motion.ReduceMotion = false;
            using var restoredFixture = new KitFixture();
            ToolbarItem restored = Themed("Recipe");
            restoredFixture.Window.Content = restored;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(Fill(restored).Transitions!, t => t is BrushTransition);
            DoubleTransition restoredOpacity = Assert.Single(Fill(restored).Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.HoverDuration, restoredOpacity.Duration);
        }

        [AvaloniaFact]
        public void ClassesSubscription_DoesNotAccumulate_AcrossRepeatedTemplateApplication() {
            using var fixture = new KitFixture();
            ControlTheme theme = KitToolbarItemTheme();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            item.Theme = null;
            Dispatcher.UIThread.RunJobs();
            item.Theme = theme;
            Dispatcher.UIThread.RunJobs();

            int baseline = item.LabelVisibilityApplyCount;
            item.Classes.Add("icon-only");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(baseline + 1, item.LabelVisibilityApplyCount);
        }

        [AvaloniaFact]
        public void Detach_DisposesTheLiveLabelOpacitySubscription() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            item.Classes.Add("icon-only");
            Dispatcher.UIThread.RunJobs();
            Assert.True(item.HasLiveLabelOpacitySubscription);

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            Assert.False(item.HasLiveLabelOpacitySubscription);
        }

        [AvaloniaFact]
        public void ClassesSubscription_UnhooksOnDetach_AndRehooksOnReattach() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            int detachedCount = item.LabelVisibilityApplyCount;
            item.Classes.Add("icon-only");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(detachedCount, item.LabelVisibilityApplyCount);

            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            item.Classes.Add("spacer");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(detachedCount + 1, item.LabelVisibilityApplyCount);
        }

        [AvaloniaFact]
        public void IconOnly_LabelCrossFadesOpacity_InsteadOfAHardIsVisibleFlip() {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Recipe");
            Assert.True(label.IsVisible);

            item.Classes.Add("icon-only");
            Dispatcher.UIThread.RunJobs();

            //Fading, not cut: still visible right after the class flips, not yet at zero opacity.
            Assert.True(label.IsVisible);
            DoubleTransition opacity = Assert.Single(label.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.TabLabelCrossFadeDuration, opacity.Duration);

            AnimationClock.Settle();

            Assert.False(label.IsVisible);
            Assert.Equal(0, label.Opacity);
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

        private static Border Fill(ToolbarItem item) =>
            item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HoverFill");

        //Before this fix PART_HoverFill sat inside the padded content Border, inset by ToolbarItemPadding on
        //every side - the fill covered a smaller rect than the item itself, at 100% and even more so at 150%.
        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void Hover_FillCoversTheItemsFullBounds(double scale) {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();
            ScaleContract.Apply(scale);
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(item).Hover();
            AnimationClock.Settle();

            Assert.Equal(item.Bounds.Width, Fill(item).Bounds.Width);
            Assert.Equal(item.Bounds.Height, Fill(item).Bounds.Height);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void On_FillCoversTheItemsFullBounds(double scale) {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();
            ScaleContract.Apply(scale);
            Dispatcher.UIThread.RunJobs();

            item.IsOn = true;
            AnimationClock.Settle();

            Assert.Equal(item.Bounds.Width, Fill(item).Bounds.Width);
            Assert.Equal(item.Bounds.Height, Fill(item).Bounds.Height);
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void Pressed_FillCoversTheItemsFullBounds(double scale) {
            using var fixture = new KitFixture();
            ToolbarItem item = Themed("Recipe");
            fixture.Window.Content = item;
            Dispatcher.UIThread.RunJobs();
            ScaleContract.Apply(scale);
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(item).Hover();
            fixture.StateOf(item).Press();
            AnimationClock.Settle();

            Assert.Equal(item.Bounds.Width, Fill(item).Bounds.Width);
            Assert.Equal(item.Bounds.Height, Fill(item).Bounds.Height);
        }

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
