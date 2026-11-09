using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using System.Windows.Input;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class PopUpTests {
        private static ControlTheme KitPopUpTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PopUp", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PopUp Themed(params string[] items) {
            var popup = new PopUp { Theme = KitPopUpTheme() };
            foreach (string item in items)
                popup.Items.Add(item);
            if (items.Length > 0)
                popup.SelectedItem = items[0];
            return popup;
        }

        private static Border Frame(PopUp popup) => popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
        private static Border Indicator(PopUp popup) => popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
        private static Path Chevron(PopUp popup) => popup.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_PulldownChevron");
        private static TextBlock Label(PopUp popup) => popup.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
        private static StackPanel TrailingColumn(PopUp popup) => popup.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_TrailingColumn");
        private static Border IconFrame(PopUp popup) => popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_IconFrame");
        private static Image IconImage(PopUp popup) => popup.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Icon");

        private sealed class TestCommand : ICommand {
            public object? Parameter;
            public bool Invoked;
            public event System.EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { Invoked = true; Parameter = parameter; }
        }

        [AvaloniaFact]
        public void KitPopUp_IsAControlThemeTargetingPopUp() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(PopUp), KitPopUpTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Assert.Equal(Assert.IsType<double>(height), popup.Height);

            Border indicator = Indicator(popup);
            Application.Current!.TryGetResource("Foreman.Size.PopUpIndicator", Application.Current.ActualThemeVariant, out object? indicatorSize);
            Application.Current!.TryGetResource("Foreman.Radius.PopUpIndicator", Application.Current.ActualThemeVariant, out object? indicatorRadius);
            Assert.Equal(Assert.IsType<double>(indicatorSize), indicator.Width);
            Assert.Equal(Assert.IsType<CornerRadius>(indicatorRadius), indicator.CornerRadius);
        }

        [AvaloniaFact]
        public void Indicator_FillIsAccentAndGlyphFillIsTextOnAccent() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Border indicator = Indicator(popup);
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), indicator.Background?.ToImmutable());

            Path glyph = indicator.GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), glyph.Fill?.ToImmutable());
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.PopUpChevrons.16"), glyph.Data);
        }

        [AvaloniaFact]
        public void PopUpChevrons_IsNotTheSharedChevronGeometry() {
            using var fixture = new KitFixture();
            var popUpChevrons = (Geometry)Application.Current!.FindResource("Kit.Glyph.PopUpChevrons.16")!;
            var chevron10 = (Geometry)Application.Current!.FindResource("Kit.Glyph.Chevron.10")!;
            Assert.NotEqual(popUpChevrons.Bounds, chevron10.Bounds);
        }

        [AvaloniaFact]
        public void Pulldown_HasNoIndicatorAndChevronIsTintedTextTertiary() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Add as");
            popup.IsPullDown = true;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Assert.False(Indicator(popup).IsVisible);
            Path chevron = Chevron(popup);
            Assert.True(chevron.IsVisible);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), chevron.Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void TrailingColumnGap_ComesFromTheControlGlyphGapSizeToken() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            popup.Width = 200;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlGlyphGap", Application.Current.ActualThemeVariant, out object? gap);
            TextBlock label = Label(popup);
            double labelRight = Avalonia.VisualExtensions.TranslatePoint(label, new Point(label.Bounds.Width, 0), popup)!.Value.X;
            double trailingLeft = Avalonia.VisualExtensions.TranslatePoint(TrailingColumn(popup), new Point(0, 0), popup)!.Value.X;

            Assert.Equal(labelRight + (double)gap!, trailingLeft, 2);
        }

        [AvaloniaFact]
        public void IndicatorGlyph_StretchIsNoneAndRenderedGeometryMatchesItsUnscaledMaster() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Path glyph = Indicator(popup).GetVisualDescendants().OfType<Path>().Single();
            var master = (Geometry)Application.Current!.FindResource("Kit.Glyph.PopUpChevrons.16")!;

            Assert.Equal(Stretch.None, glyph.Stretch);
            Assert.Equal(master.Bounds.Width, glyph.RenderedGeometry!.Bounds.Width, 2);
            Assert.Equal(master.Bounds.Height, glyph.RenderedGeometry.Bounds.Height, 2);
        }

        [AvaloniaFact]
        public void WiderThanItsText_LabelFillsAndIndicatorHugsTheRightPaddingInset() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            popup.Width = 200;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = Label(popup);
            Point labelLeft = Avalonia.VisualExtensions.TranslatePoint(label, new Point(0, 0), popup)!.Value;
            Assert.Equal(popup.BorderThickness.Left + 10, labelLeft.X, 3);

            Border indicator = Indicator(popup);
            Point indicatorRight = Avalonia.VisualExtensions.TranslatePoint(indicator, new Point(indicator.Bounds.Width, 0), popup)!.Value;
            Assert.Equal(popup.Bounds.Width - popup.BorderThickness.Right - 3, indicatorRight.X, 3);
        }

        [AvaloniaFact]
        public void NoIcon_IconFrameIsHidden_AndTheLeadingInsetIs10() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            popup.Width = 200;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Assert.False(IconFrame(popup).IsVisible);
            double labelLeft = Avalonia.VisualExtensions.TranslatePoint(Label(popup), new Point(0, 0), popup)!.Value.X;
            Assert.Equal(popup.BorderThickness.Left + 10, labelLeft, 3);
        }

        [AvaloniaFact]
        public void WithIcon_LeadingInsetDropsTo6_IconIs16AtRadius3_GapIs6() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Rare");
            popup.Icon = new RenderTargetBitmap(new PixelSize(4, 4));
            popup.Width = 200;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Border iconFrame = IconFrame(popup);
            Assert.True(iconFrame.IsVisible);
            Application.Current!.TryGetResource("Foreman.Size.PopUpLeadingIcon", Application.Current.ActualThemeVariant, out object? iconSize);
            Application.Current!.TryGetResource("Foreman.Radius.PopUpLeadingIcon", Application.Current.ActualThemeVariant, out object? iconRadius);
            Assert.Equal(Assert.IsType<double>(iconSize), iconFrame.Bounds.Width);
            Assert.Equal(Assert.IsType<double>(iconSize), iconFrame.Bounds.Height);
            Assert.Equal(Assert.IsType<CornerRadius>(iconRadius), iconFrame.CornerRadius);
            Assert.Same(popup.Icon, IconImage(popup).Source);

            double iconLeft = Avalonia.VisualExtensions.TranslatePoint(iconFrame, new Point(0, 0), popup)!.Value.X;
            Assert.Equal(popup.BorderThickness.Left + 6, iconLeft, 3);

            Application.Current!.TryGetResource("Foreman.Size.ControlGlyphGap", Application.Current.ActualThemeVariant, out object? gap);
            double iconRight = Avalonia.VisualExtensions.TranslatePoint(iconFrame, new Point(iconFrame.Bounds.Width, 0), popup)!.Value.X;
            double labelLeft = Avalonia.VisualExtensions.TranslatePoint(Label(popup), new Point(0, 0), popup)!.Value.X;
            Assert.Equal(iconRight + (double)gap!, labelLeft, 2);

            Border indicator = Indicator(popup);
            Application.Current!.TryGetResource("Foreman.Size.PopUpIndicator", Application.Current.ActualThemeVariant, out object? indicatorSize);
            Application.Current!.TryGetResource("Foreman.Radius.PopUpIndicator", Application.Current.ActualThemeVariant, out object? indicatorRadius);
            Assert.Equal(Assert.IsType<double>(indicatorSize), indicator.Bounds.Width);
            Assert.Equal(Assert.IsType<CornerRadius>(indicatorRadius), indicator.CornerRadius);
        }

        [AvaloniaFact]
        public void MenuItems_CarryTheSameLeadingIconAtTheMenusOwnInset() {
            using var fixture = new KitFixture();
            var normalIcon = new RenderTargetBitmap(new PixelSize(4, 4));
            var rareIcon = new RenderTargetBitmap(new PixelSize(4, 4));
            PopUp popup = Themed("Normal", "Rare");
            popup.Icon = normalIcon;
            popup.ItemIcons.Add(normalIcon);
            popup.ItemIcons.Add(rareIcon);
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(normalIcon, popup.Menu.Items[0].Icon);
            Assert.Equal(rareIcon, popup.Menu.Items[1].Icon);
        }

        [AvaloniaFact]
        public void Hover_FrameBackgroundIsControlBackgroundHover() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(popup).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), Frame(popup).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Open_FrameBackgroundIsControlBackgroundPressed() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundPressed").ToImmutable(), Frame(popup).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;

            popup.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), popup.Opacity);
        }

        [AvaloniaFact]
        public void ClosedArrowKeys_StepTheAdjacentValueWithoutOpening() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s", "Power / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Down);

            Assert.Equal("Belts / s", popup.SelectedItem);
            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void Space_OpensTheMenuWithTheCurrentValueHighlighted() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            popup.SelectedItem = "Belts / s";
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);

            Assert.True(popup.IsOpen);
            Assert.Equal(1, popup.Menu.HighlightedIndex);
        }

        [AvaloniaFact]
        public void ClosedReturn_OpensTheMenu() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Enter);

            Assert.True(popup.IsOpen);
        }

        [AvaloniaFact]
        public void OpenEnter_CommitsTheHighlightedValueAndCloses() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);
            fixture.StateOf(popup).KeyDown(Key.Down);
            fixture.StateOf(popup).KeyDown(Key.Enter);

            Assert.Equal("Belts / s", popup.SelectedItem);
            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void Escape_ClosesWithoutChangingTheSelectedItem() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);
            fixture.StateOf(popup).KeyDown(Key.Down);
            fixture.StateOf(popup).KeyDown(Key.Escape);

            Assert.Equal("Items / s", popup.SelectedItem);
            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void PullDown_ShowsItsLabelAndRaisesTheCommand() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Add as blueprint", "Add as ghost");
            popup.IsPullDown = true;
            popup.Label = "Add as";
            var command = new TestCommand();
            popup.Command = command;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Add as", Label(popup).Text);

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            popup.Menu.HighlightedIndex = 0;
            popup.Menu.ActivateHighlighted();

            Assert.True(command.Invoked);
            Assert.Equal("Add as blueprint", command.Parameter);
        }

        [AvaloniaFact]
        public void Detaching_ClosesTheFlyout() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            var host = new ContentControl { Content = popup };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);

            host.Content = null;
            Dispatcher.UIThread.RunJobs();

            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void ClosingTheWindow_WithTheFlyoutOpen_DoesNotThrow_AndClosesTheFlyout() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);

            Exception? caught = null;
            try {
                fixture.Window.Close();
                Dispatcher.UIThread.RunJobs();
            } catch (Exception ex) {
                caught = ex;
            }

            Assert.True(caught is null, caught?.ToString());
            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void Opening_RendersEveryRowAndSettlesWithoutRebuilding() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s", "Power / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(300);

            StackPanel items = popup.Menu.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Items");
            Assert.Equal(3, items.Children.Count);
            Assert.True(popup.Menu.Bounds.Width > 0 && popup.Menu.Bounds.Height > 0);

            int rebuilds = 0;
            int layouts = 0;
            items.Children.CollectionChanged += (_, _) => rebuilds++;
            popup.Menu.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, rebuilds);
            Assert.True(layouts <= 2, $"MenuPanel laid out {layouts} times while idle.");
        }

        [AvaloniaFact]
        public void LightDismiss_ClosesTheControlAndTheMenuReopens() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Popup host = popup.GetVisualDescendants().OfType<Popup>().Single();
            Assert.True(host.IsOpen);

            host.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.DoesNotContain(":open", popup.Classes);

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsOpen);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingAtTheControlRadius() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;

            fixture.StateOf(popup).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(popup));
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }

        [AvaloniaFact]
        public void EscapeAfterOpen_ReturnsFocusToTheControlWithARing() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);
            fixture.StateOf(popup).KeyDown(Key.Escape);

            Assert.True(popup.IsKeyboardFocusWithin);
            Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(popup));
        }

        [AvaloniaFact]
        public void LightDismiss_ReturnsFocusToTheControl() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Popup host = popup.GetVisualDescendants().OfType<Popup>().Single();

            host.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(popup.IsKeyboardFocusWithin);
        }

        [AvaloniaFact]
        public void MenuRows_DrawTheirLabelInTheItemsOwnFontFamily_WhenOneIsGiven() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Helvetica Neue", "Menlo", "Plain");
            popup.ItemFontFamilies.Add(new FontFamily("Helvetica Neue"));
            popup.ItemFontFamilies.Add(new FontFamily("Menlo"));
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();
            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(300);

            StackPanel items = popup.Menu.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Items");
            TextBlock[] labels = [.. items.Children.Select(row => row.GetVisualDescendants().OfType<TextBlock>().Single())];
            Assert.Equal(["Helvetica Neue", "Menlo", "Plain"], labels.Select(l => l.Text));
            Assert.Equal("Helvetica Neue", labels[0].FontFamily.Name);
            Assert.Equal("Menlo", labels[1].FontFamily.Name);
            Assert.False(labels[2].IsSet(TextBlock.FontFamilyProperty));
            Assert.Equal(popup.Menu.FontFamily, labels[2].FontFamily);
        }

        [AvaloniaFact]
        public void EmptyList_RendersDisabled_ReadingNoOptions_AndReenablesWhenFilled() {
            using var fixture = new KitFixture();
            PopUp popup = Themed();
            popup.Label = "Quality: Normal";
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Assert.False(popup.IsEnabled);
            Assert.Equal("No options", popup.DisplayText);
            Assert.Equal("No options", Label(popup).Text);

            popup.Items.Add("Normal");
            Assert.True(popup.IsEnabled);
            Assert.Equal("Quality: Normal", popup.DisplayText);

            popup.IsEnabled = false;
            popup.Items.Clear();
            popup.Items.Add("Normal");
            Assert.False(popup.IsEnabled);
        }

        [AvaloniaFact]
        public void DisabledItems_StayListedButCannotBeChosenByPointerOrArrows_AndReadAsDisabled() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Source", "Passthrough", "Output");
            popup.DisabledItems.Add("Passthrough");
            var command = new TestCommand();
            popup.Command = command;
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Down);
            Assert.Equal("Output", popup.SelectedItem);
            fixture.StateOf(popup).KeyDown(Key.Up);
            Assert.Equal("Source", popup.SelectedItem);

            fixture.StateOf(popup).KeyDown(Key.Space);
            Assert.Equal(["Source", "Passthrough", "Output"], popup.Menu.Items.Select(r => r.Text));
            Assert.Equal([true, false, true], popup.Menu.Items.Select(r => r.IsEnabled));
            fixture.StateOf(popup).KeyDown(Key.Down);
            Assert.Equal(2, popup.Menu.HighlightedIndex);

            Border passthrough = popup.Menu.GetVisualDescendants().OfType<Border>()
                .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Passthrough");
            Assert.False(ControlAutomationPeer.CreatePeerForElement(passthrough).IsEnabled());
            command.Invoked = false;
            fixture.StateOf(passthrough).Click();
            Assert.Equal("Source", popup.SelectedItem);
            Assert.False(command.Invoked);

            popup.SelectedItem = "Passthrough";
            popup.IsOpen = false;
            popup.IsOpen = true;
            Assert.Equal(0, popup.Menu.HighlightedIndex);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class PopUpScaleTests {
        [AvaloniaFact]
        public void IndicatorSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.PopUp", null, out object? theme));
            var popup = new PopUp { Theme = Assert.IsType<ControlTheme>(theme) };
            popup.Items.Add("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Border indicator = popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
            Assert.Equal(16, indicator.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(24, indicator.Width);
        }

        [AvaloniaFact]
        public void IndicatorGlyphViewbox_TracksScaleContractWhileThePathStaysAtItsMasterSize() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.PopUp", null, out object? theme));
            var popup = new PopUp { Theme = Assert.IsType<ControlTheme>(theme) };
            popup.Items.Add("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Border indicator = popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
            Viewbox box = indicator.GetVisualDescendants().OfType<Viewbox>().Single();
            Path glyph = indicator.GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(16, box.Bounds.Width);
            Assert.Equal(16, glyph.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(24, box.Bounds.Width);
            Assert.Equal(16, glyph.Bounds.Width);
        }

        [AvaloniaFact]
        public void IndicatorLeftEdge_AtOneFiftyPercentIsLabelRightPlusTheScaledGlyphGap() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.PopUp", null, out object? theme));
            var popup = new PopUp { Theme = Assert.IsType<ControlTheme>(theme), Width = 200 };
            popup.Items.Add("Items / s");
            popup.SelectedItem = "Items / s";
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            TextBlock label = popup.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
            Border indicator = popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
            double labelRight = Avalonia.VisualExtensions.TranslatePoint(label, new Point(label.Bounds.Width, 0), popup)!.Value.X;
            double indicatorLeft = Avalonia.VisualExtensions.TranslatePoint(indicator, new Point(0, 0), popup)!.Value.X;

            Assert.Equal(labelRight + 9, indicatorLeft, 2);
        }
    }
}
