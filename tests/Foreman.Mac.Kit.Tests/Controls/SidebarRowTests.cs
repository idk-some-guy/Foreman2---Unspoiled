using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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
    public sealed class SidebarRowTests {
        private static ControlTheme KitSidebarRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.SidebarRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Geometry SampleGlyph() {
            Application.Current!.TryGetResource("Kit.Glyph.Filter.14", null, out object? geometry);
            return Assert.IsAssignableFrom<Geometry>(geometry);
        }

        private static SidebarRow Themed(string label) =>
            new() { Theme = KitSidebarRowTheme(), Label = label, Glyph = SampleGlyph() };

        private static Border Frame(SidebarRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static Border Fill(SidebarRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");

        private static Border SelectionFill(SidebarRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");

        private static TextBlock LabelText(SidebarRow row) =>
            row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");

        private static Path GlyphPath(SidebarRow row) =>
            row.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Glyph");

        [AvaloniaFact]
        public void KitSidebarRow_IsAControlThemeTargetingSidebarRow() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(SidebarRow), KitSidebarRowTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.SidebarRowHeight", Application.Current.ActualThemeVariant, out object? height);
            Assert.Equal(Assert.IsType<double>(height), Frame(row).Height);

            Application.Current!.TryGetResource("Foreman.Size.Glyph17", Application.Current.ActualThemeVariant, out object? glyphSize);
            Assert.Equal(Assert.IsType<double>(glyphSize), GlyphPath(row).Width);
        }

        [AvaloniaFact]
        public void Selected_KeyView_UsesAccentSoftAndAccentTextGlyphAndLabel() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
            row.IsSelected = true;
            row.IsKey = true;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), SelectionFill(row).Background?.ToImmutable());
            Assert.Equal(1, SelectionFill(row).Opacity);
            Assert.Equal(0, Fill(row).Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), LabelText(row).Foreground?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), GlyphPath(row).Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void Selected_NotKeyView_UsesSelectionInactiveAndTextColorGlyph() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
            row.IsSelected = true;
            row.IsKey = false;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.SelectionInactive").ToImmutable(), Fill(row).Background?.ToImmutable());
            Assert.Equal(1, Fill(row).Opacity);
            Assert.Equal(0, SelectionFill(row).Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), LabelText(row).Foreground?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), GlyphPath(row).Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void Normal_LabelIsTextGlyphIsTextSecondary() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), LabelText(row).Foreground?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), GlyphPath(row).Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_UnselectedRow_UsesControlBackgroundHover() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
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
            SidebarRow row = Themed("Assemblers");
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
            SidebarRow row = Themed("Assemblers");
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
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
            fixture.Window.Content = row;

            row.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), row.Opacity);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingOnTheFrameAtTheControlRadius() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
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
    public class SidebarRowScaleTests {
        [AvaloniaFact]
        public void Height_AndIconGap_TrackScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SidebarRow", null, out object? theme));
            Application.Current!.TryGetResource("Kit.Glyph.Filter.14", null, out object? geometry);
            var row = new SidebarRow { Theme = (ControlTheme)theme!, Label = "Assemblers", Glyph = (Geometry)geometry! };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Border frame = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            StackPanel iconRow = row.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_IconRow");
            Assert.Equal(28, frame.Height);
            Assert.Equal(8, iconRow.Spacing);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(42, frame.Height);
            Assert.Equal(12, iconRow.Spacing);
        }
    }
}
