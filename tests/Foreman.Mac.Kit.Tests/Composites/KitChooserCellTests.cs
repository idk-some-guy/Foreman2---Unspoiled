using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    }
}
