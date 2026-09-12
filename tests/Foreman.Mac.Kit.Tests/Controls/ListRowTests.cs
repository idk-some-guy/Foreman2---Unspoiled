using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
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
    public sealed class ListRowTests {
        private static ControlTheme KitListRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ListRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ListRow Themed(string label, string? trailing = null) =>
            new() { Theme = KitListRowTheme(), Label = label, Trailing = trailing };

        private static Border Frame(ListRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static Border Fill(ListRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");

        private static Border SelectionFill(ListRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");

        private static TextBlock LabelText(ListRow row) =>
            row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");

        private static TextBlock TrailingText(ListRow row) =>
            row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Trailing");

        [AvaloniaFact]
        public void KitListRow_IsAControlThemeTargetingListRow() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ListRow), KitListRowTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ListRowHeight", Application.Current.ActualThemeVariant, out object? height);
            Assert.Equal(Assert.IsType<double>(height), Frame(row).Height);

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Frame(row).CornerRadius);
        }

        [AvaloniaFact]
        public void Selected_KeyView_UsesAccentSoftAndAccentTextAt600() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace", "×4");
            row.IsSelected = true;
            row.IsKey = true;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), SelectionFill(row).Background?.ToImmutable());
            Assert.Equal(1, SelectionFill(row).Opacity);
            Assert.Equal(0, Fill(row).Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), LabelText(row).Foreground?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), TrailingText(row).Foreground?.ToImmutable());

            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            Assert.Equal(Assert.IsType<FontWeight>(weight), LabelText(row).FontWeight);
        }

        [AvaloniaFact]
        public void Selected_NotKeyView_UsesSelectionInactiveAndNeutralText() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            row.IsSelected = true;
            row.IsKey = false;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.SelectionInactive").ToImmutable(), Fill(row).Background?.ToImmutable());
            Assert.Equal(1, Fill(row).Opacity);
            Assert.Equal(0, SelectionFill(row).Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), LabelText(row).Foreground?.ToImmutable());

            Application.Current!.TryGetResource("Foreman.Type.Callout.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            Assert.Equal(Assert.IsType<FontWeight>(weight), LabelText(row).FontWeight);
        }

        [AvaloniaFact]
        public void Hover_UnselectedRow_UsesControlBackgroundHover() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            fixture.StateOf(row).Hover();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), Fill(row).Background?.ToImmutable());
            Assert.Equal(1, Fill(row).Opacity);
            Assert.Equal(0, SelectionFill(row).Opacity);
        }

        [AvaloniaFact]
        public void Hover_NeverAppliesToASelectedRow() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            row.IsSelected = true;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            fixture.StateOf(row).Hover();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), SelectionFill(row).Background?.ToImmutable());
            Assert.Equal(1, SelectionFill(row).Opacity);
            Assert.Equal(0, Fill(row).Opacity);
        }

        [AvaloniaFact]
        public void HoverToSelected_CrossFadesFillAndSelectionFillOpacities() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            fixture.StateOf(row).Hover();
            AnimationClock.Settle();
            Assert.Equal(1, Fill(row).Opacity);
            Assert.Equal(0, SelectionFill(row).Opacity);

            row.IsSelected = true;
            AnimationClock.Settle();

            Assert.Equal(0, Fill(row).Opacity);
            Assert.Equal(1, SelectionFill(row).Opacity);
        }

        [AvaloniaFact]
        public void Normal_TrailingIsTextTertiary() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace", "×4");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), TrailingText(row).Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            fixture.Window.Content = row;

            row.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), row.Opacity);
        }

        [AvaloniaFact]
        public void Transitions_FillCrossFadesAtHoverDuration() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            var opacity = Assert.Single(Fill(row).Transitions!.OfType<DoubleTransition>());
            Assert.Equal(Visual.OpacityProperty, opacity.Property);
            Assert.Equal(Motion.HoverDuration, opacity.Duration);

            var selectionOpacity = Assert.Single(SelectionFill(row).Transitions!.OfType<DoubleTransition>());
            Assert.Equal(Visual.OpacityProperty, selectionOpacity.Property);
            Assert.Equal(Motion.HoverDuration, selectionOpacity.Duration);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingOnTheFrameAtTheControlRadius() {
            using var fixture = new KitFixture();
            ListRow row = Themed("Electric furnace");
            fixture.Window.Content = row;

            fixture.StateOf(row).Focus();

            var ring = Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(AdornerLayer.GetAdorner(Frame(row)));
            Assert.Null(AdornerLayer.GetAdorner(row));

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class ListRowScaleTests {
        [AvaloniaFact]
        public void Height_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.ListRow", null, out object? theme));
            var row = new ListRow { Theme = (ControlTheme)theme!, Label = "Electric furnace" };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Border frame = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Assert.Equal(26, frame.Height);

            StackPanel iconRow = row.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_IconRow");
            Assert.Equal(9, iconRow.Spacing);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(39, frame.Height);
            Assert.Equal(13.5, iconRow.Spacing);
        }
    }
}
