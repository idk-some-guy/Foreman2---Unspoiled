using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitChooserCellTests {
        private static ControlTheme KitChooserCellTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ChooserCell", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ChooserCell Themed() => new() { Theme = KitChooserCellTheme() };

        private static Border Frame(ChooserCell cell) =>
            cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static Border IconFrame(ChooserCell cell) =>
            cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_IconFrame");

        private static TextBlock Label(ChooserCell cell) =>
            cell.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");

        [AvaloniaFact]
        public void KitChooserCell_IsAControlThemeTargetingChooserCell() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ChooserCell), KitChooserCellTheme().TargetType);
        }

        [AvaloniaFact]
        public void Cell_Is88SquareAtRadius8_NoBackgroundOrBorderAtRest() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(88, cell.Bounds.Width);
            Assert.Equal(88, cell.Bounds.Height);
            Application.Current!.TryGetResource("Foreman.Radius.ChooserCell", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Frame(cell).CornerRadius);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(Frame(cell).Background).Color);
            Assert.Null(Frame(cell).BorderBrush);
        }

        [AvaloniaFact]
        public void Icon_Is40SquareAtRadius8WithBorderStrong() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Border iconFrame = IconFrame(cell);
            Assert.Equal(40, iconFrame.Width);
            Assert.Equal(40, iconFrame.Height);
            Assert.Equal(new CornerRadius(8), iconFrame.CornerRadius);
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), iconFrame.BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), iconFrame.BorderThickness);
        }

        [AvaloniaFact]
        public void Label_IsCaption_MaxTwoLines_HeightAtMostTwentyFour_CharacterEllipsis() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.Label = "Assembling machine 3";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = Label(cell);
            Application.Current!.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Assert.Equal(Assert.IsType<double>(size), label.FontSize);
            Assert.Equal(2, label.MaxLines);
            Assert.True(label.Bounds.Height <= 24);
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), label.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Label_LineHeightTracksTheCaptionToken_SoBothWrappedLinesRenderAtThePinnedHeight() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.Label = "Casting iron plate from molten iron and scrap";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = Label(cell);
            Application.Current!.TryGetResource("Foreman.Type.Caption.LineHeight", Application.Current.ActualThemeVariant, out object? lineHeight);
            double expected = Assert.IsType<double>(lineHeight);
            Assert.Equal(expected, label.LineHeight);

            var typeface = new Typeface(label.FontFamily, label.FontStyle, label.FontWeight);
            var layout = new TextLayout(label.Text ?? "", typeface, label.FontSize, Brushes.Black,
                textAlignment: TextAlignment.Center, textWrapping: TextWrapping.Wrap,
                maxWidth: label.Bounds.Width, maxLines: 2, lineHeight: label.LineHeight);

            Assert.Equal(2, layout.TextLines.Count);
            Assert.All(layout.TextLines, line => Assert.Equal(expected, line.Height));
        }

        [AvaloniaFact]
        public void Tooltip_AndAXTitle_EqualTheFullName_EvenWhenNotTruncated() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.Label = "Iron plate";
            cell.FullName = "Iron plate";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Iron plate", ToolTip.GetTip(cell));
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cell);
            Assert.Equal("Iron plate", peer.GetName());
        }

        [AvaloniaFact]
        public void Tooltip_AndAXTitle_CarryTheFullNameWhenTheLabelWouldTruncate() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.Label = "Casting iron plate from molten iron and scrap";
            cell.FullName = "Casting iron plate from molten iron and scrap";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Casting iron plate from molten iron and scrap", ToolTip.GetTip(cell));
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cell);
            Assert.Equal("Casting iron plate from molten iron and scrap", peer.GetName());
        }

        [AvaloniaFact]
        public void Selected_FillsAccentSoft_LabelTurnsAccentTextAtWeight600() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            cell.IsSelected = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), Frame(cell).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), Label(cell).Foreground?.ToImmutable());
            Application.Current!.TryGetResource("Foreman.Type.Caption.Emphasized.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            Assert.Equal(Assert.IsType<FontWeight>(weight), Label(cell).FontWeight);
        }

        [AvaloniaFact]
        public void NotKeyWindow_ViaInheritedAttachedProperty_SwapsFillToSelectionInactive() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.IsSelected = true;

            var container = new Border { Child = cell };
            container.SetValue(ListHost.IsKeyProperty, false);
            fixture.Window.Content = container;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.SelectionInactive").ToImmutable(), Frame(cell).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void SelectedAndFocused_CarriesBothTheFillAndTheFocusAdorner() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.IsSelected = true;
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            FocusRing.SetShowAlways(cell, true);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(AdornerLayer.GetAdorner(cell));
            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), Frame(cell).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_ShowsControlBackgroundHover_SuppressedWhileSelected() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.Classes.Add("gallery-hover");
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), Frame(cell).Background?.ToImmutable());

            cell.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), Frame(cell).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_Dims() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            fixture.Window.Content = cell;
            cell.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), cell.Opacity);
        }

        [AvaloniaFact]
        public void VoiceOver_InvokeRaisesInvoked() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            bool invoked = false;
            cell.Invoked += (_, _) => invoked = true;

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cell);
            IInvokeProvider? invoke = peer.GetProvider<IInvokeProvider>();
            Assert.NotNull(invoke);
            invoke!.Invoke();

            Assert.True(invoked);
        }

        [AvaloniaFact]
        public void Unavailable_DimsTheFrameToTheUnavailableToken_StaysEnabled_AndReadsUnavailable() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.FullName = "Steel chest";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Frame(cell).Opacity);

            cell.IsUnavailable = true;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Unavailable", Application.Current.ActualThemeVariant, out object? unavailable);
            Assert.Equal(Assert.IsType<double>(unavailable), Frame(cell).Opacity);
            Assert.True(cell.IsEnabled);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cell);
            Assert.True(peer.IsEnabled());
            Assert.Equal("Steel chest", peer.GetName());
            Assert.Equal("Unavailable", peer.GetHelpText());

            cell.IsUnavailable = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Frame(cell).Opacity);
            Assert.NotEqual("Unavailable", peer.GetHelpText());
        }

        private static Viewbox Placeholder(ChooserCell cell) =>
            cell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_Placeholder");

        private static Image IconImage(ChooserCell cell) =>
            cell.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Icon");

        private static bool IsTransparent(IBrush? brush) =>
            brush is null || (brush is ISolidColorBrush solid && solid.Color.A == 0);

        [AvaloniaFact]
        public void IconMissing_DrawsThePictureGlyphAtTwentyCentredInAWellFilledFrame_AndReadsTheLabelAlone() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.Label = "Iron Plate";
            cell.FullName = "Iron Plate";
            cell.IsIconMissing = true;
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(":icon-missing", cell.Classes);
            Viewbox placeholder = Placeholder(cell);
            Assert.True(placeholder.IsVisible);
            Assert.False(IconImage(cell).IsVisible);
            Path glyph = placeholder.GetVisualDescendants().OfType<Path>().Single();
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph.IconMissing.20", null, out object? data));
            Assert.Same(data, glyph.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), glyph.Stroke?.ToImmutable());

            Border iconFrame = IconFrame(cell);
            Assert.Equal(fixture.Brush("Foreman.Color.WellBackground").ToImmutable(), iconFrame.Background?.ToImmutable());
            Point topLeft = placeholder.TranslatePoint(new Point(0, 0), iconFrame) ?? default;
            Assert.Equal(20, placeholder.Bounds.Width, 0.5);
            Assert.Equal(20, placeholder.Bounds.Height, 0.5);
            Assert.Equal((iconFrame.Bounds.Width - 20) / 2, topLeft.X, 0.5);
            Assert.Equal((iconFrame.Bounds.Height - 20) / 2, topLeft.Y, 0.5);

            Assert.Equal("Iron Plate", Label(cell).Text);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cell);
            Assert.Equal("Iron Plate", peer.GetName());
            Assert.DoesNotContain("icon", peer.GetName(), System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("missing", peer.GetName(), System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(peer.GetChildren(), c => c.GetName() is { Length: > 0 } || c.GetAutomationControlType() == AutomationControlType.Image);

            cell.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), glyph.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void PendingIcon_KeepsTheWellTransparent_WithNoPlaceholder() {
            using var fixture = new KitFixture();
            ChooserCell cell = Themed();
            cell.Label = "Iron Plate";
            cell.FullName = "Iron Plate";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(":icon-missing", cell.Classes);
            Assert.False(Placeholder(cell).IsVisible);
            Assert.True(IsTransparent(IconFrame(cell).Background));
            Assert.Equal("Iron Plate", Label(cell).Text);
            Assert.Equal("Iron Plate", ControlAutomationPeer.CreatePeerForElement(cell).GetName());
        }
    }

    [Collection(KitScaleGroup.Name)]
    public sealed class KitChooserCellScaleTests {
        [AvaloniaFact]
        public void CellAndIcon_TrackScaleContract_RadiusAndBorderDoNot() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.ChooserCell", null, out object? theme));
            var cell = new ChooserCell { Theme = (ControlTheme)theme! };
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Border frame = cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Border iconFrame = cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_IconFrame");
            CornerRadius radiusBefore = frame.CornerRadius;

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(132, cell.Bounds.Width);
            Assert.Equal(60, iconFrame.Width);
            Assert.Equal(radiusBefore, frame.CornerRadius);
            Assert.Equal(new CornerRadius(8), iconFrame.CornerRadius);
        }

        [AvaloniaFact]
        public void IconMissing_PlaceholderScalesToThirtyInsideTheSixtyWell() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.ChooserCell", null, out object? theme));
            var cell = new ChooserCell { Theme = (ControlTheme)theme!, IsIconMissing = true };
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Border iconFrame = cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_IconFrame");
            Viewbox placeholder = cell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_Placeholder");
            Assert.Equal(60, iconFrame.Bounds.Width, 0.5);
            Assert.Equal(30, placeholder.Bounds.Width, 0.5);
            Assert.Equal(30, placeholder.Bounds.Height, 0.5);
            Point topLeft = placeholder.TranslatePoint(new Point(0, 0), iconFrame) ?? default;
            Assert.Equal(15, topLeft.X, 0.5);
            Assert.Equal(15, topLeft.Y, 0.5);
        }
    }
}
