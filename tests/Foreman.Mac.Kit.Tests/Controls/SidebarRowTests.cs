using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

        internal static WriteableBitmap Art() =>
            new(new PixelSize(18, 18), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        private static Border IconWell(SidebarRow row) =>
            row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_IconWell");

        [AvaloniaFact]
        public void Icon_DrawsItemArtInTheListRowWell_InPlaceOfTheGlyph() {
            using var fixture = new KitFixture();
            WriteableBitmap art = Art();
            var row = new SidebarRow { Theme = KitSidebarRowTheme(), Label = "Logistics", Icon = art };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Border well = IconWell(row);
            Assert.True(well.IsVisible);
            Application.Current!.TryGetResource("Foreman.Size.Glyph18", Application.Current.ActualThemeVariant, out object? size);
            Assert.Equal(Assert.IsType<double>(size), well.Bounds.Width, 0.5);
            Assert.Equal((double)size!, well.Bounds.Height, 0.5);
            Application.Current!.TryGetResource("Foreman.Radius.ListRowIcon", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), well.CornerRadius);
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), well.BorderBrush?.ToImmutable());
            Assert.Same(art, well.GetVisualDescendants().OfType<Image>().Single().Source);
            Assert.False(GlyphPath(row).IsVisible);

            double wellRight = well.TranslatePoint(new Point(well.Bounds.Width, 0), row)!.Value.X;
            double labelLeft = LabelText(row).TranslatePoint(new Point(0, 0), row)!.Value.X;
            Assert.Equal(wellRight + 8, labelLeft, 0.5);
        }

        [AvaloniaFact]
        public void GlyphRow_KeepsItsGlyph_AndShowsNoIconWell() {
            using var fixture = new KitFixture();
            SidebarRow row = Themed("Assemblers");
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Assert.False(IconWell(row).IsVisible);
            Assert.True(GlyphPath(row).IsVisible);
        }

        private const string LongLabel = "Intermediate products with a very long name";

        private static SidebarRow HostedAt(KitFixture fixture, string label, double width) {
            var row = new SidebarRow { Theme = KitSidebarRowTheme(), Label = label, Icon = Art() };
            fixture.Window.Content = new Border { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = row };
            Dispatcher.UIThread.RunJobs();
            return row;
        }

        [AvaloniaFact]
        public void LongLabel_InANarrowSidebar_TrimsAtTheTail_WithTheFullLabelAsItsTooltip() {
            using var fixture = new KitFixture();
            SidebarRow row = HostedAt(fixture, LongLabel, 160);

            TextBlock label = LabelText(row);
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(1, label.MaxLines);
            Assert.Contains(label.TextLayout.TextLines, l => l.HasCollapsed);
            double labelRight = label.TranslatePoint(new Point(label.Bounds.Width, 0), row)!.Value.X;
            Assert.True(labelRight <= row.Bounds.Width + 0.5, $"label right edge {labelRight} past the row's {row.Bounds.Width}");
            Assert.Equal(LongLabel, ToolTip.GetTip(row)?.ToString());
        }

        [AvaloniaFact]
        public void ShortLabel_IsNotTrimmed_AndCarriesNoTooltip() {
            using var fixture = new KitFixture();
            SidebarRow row = HostedAt(fixture, "Ore", 160);

            Assert.DoesNotContain(LabelText(row).TextLayout.TextLines, l => l.HasCollapsed);
            Assert.Null(ToolTip.GetTip(row));
        }

        [AvaloniaFact]
        public void Tooltip_FollowsTheTrim_WhenTheSidebarWidens() {
            using var fixture = new KitFixture();
            SidebarRow row = HostedAt(fixture, LongLabel, 160);
            Assert.Equal(LongLabel, ToolTip.GetTip(row)?.ToString());

            ((Border)fixture.Window.Content!).Width = 1200;
            Dispatcher.UIThread.RunJobs();

            Assert.Null(ToolTip.GetTip(row));
        }

        [AvaloniaFact]
        public void Tooltip_FollowsALabelChange_AtAFixedRowWidth() {
            using var fixture = new KitFixture();
            SidebarRow row = HostedAt(fixture, "Ore", 160);
            double width = row.Bounds.Width;
            Assert.Null(ToolTip.GetTip(row));

            row.Label = LongLabel;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(LongLabel, ToolTip.GetTip(row)?.ToString());
            Assert.Equal(width, row.Bounds.Width);

            row.Label = "Ore";
            Dispatcher.UIThread.RunJobs();
            Assert.Null(ToolTip.GetTip(row));
            Assert.Equal(width, row.Bounds.Width);
        }

        [AvaloniaFact]
        public void TrimmedLabel_IsNotReadTwiceByVoiceOver() {
            using var fixture = new KitFixture();
            SidebarRow row = HostedAt(fixture, LongLabel, 160);
            Assert.Equal(LongLabel, ToolTip.GetTip(row)?.ToString());

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(row);
            Assert.Equal(LongLabel, peer.GetName());
            Assert.NotEqual(peer.GetName(), peer.GetHelpText());
        }

        [AvaloniaFact]
        public void AccessibleName_IsTheFullLabel_WhileTrimmed() {
            using var fixture = new KitFixture();
            SidebarRow row = HostedAt(fixture, LongLabel, 160);
            Assert.Equal(LongLabel, ToolTip.GetTip(row)?.ToString());

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(row);
            Assert.Equal(LongLabel, peer.GetName());

            Avalonia.Automation.AutomationProperties.SetName(row, "Custom");
            Assert.Equal("Custom", peer.GetName());
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
            DockPanel iconRow = row.GetVisualDescendants().OfType<DockPanel>().Single(s => s.Name == "PART_IconRow");
            Assert.Equal(28, frame.Height);
            Assert.Equal(8, iconRow.HorizontalSpacing);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(42, frame.Height);
            Assert.Equal(12, iconRow.HorizontalSpacing);
        }

        [AvaloniaFact]
        public void IconWell_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SidebarRow", null, out object? theme));
            var row = new SidebarRow { Theme = (ControlTheme)theme!, Label = "Logistics", Icon = SidebarRowTests.Art() };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Border well = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_IconWell");
            Assert.Equal(18, well.Bounds.Width, 0.5);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(27, well.Bounds.Width, 0.5);
        }
    }
}
