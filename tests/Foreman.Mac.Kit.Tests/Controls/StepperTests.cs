using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Provider;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
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
    public sealed class StepperTests {
        private static ControlTheme KitStepperTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Stepper", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Stepper Themed() => new() { Theme = KitStepperTheme() };

        private static TextBox ValueBox(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_ValueBox");

        private static Button UpButton(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Up");

        private static Button DownButton(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Down");

        private static Border Frame(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static TextBlock UnitText(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Unit");

        [AvaloniaFact]
        public void KitStepper_IsAControlThemeTargetingStepper() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Stepper), KitStepperTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);

            Assert.Equal(Assert.IsType<double>(height), stepper.Height);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Frame(stepper).CornerRadius);
            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), Frame(stepper).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Frame(stepper).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void InnerValueBox_UsesTheKitFieldTemplateNotFluents() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.True(Application.Current!.TryGetResource("Kit.StepperValueField", null, out object? theme));
            Assert.Same(theme, ValueBox(stepper).Theme);

            Border inner = ValueBox(stepper).GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.Equal(new Thickness(0), inner.BorderThickness);
        }

        [AvaloniaFact]
        public void InnerValueBox_ResolvesCalloutFontSizeTransparentBackgroundAndNoFocusRing() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? fontSize);
            Assert.Equal(Assert.IsType<double>(fontSize), box.FontSize);
            Assert.Equal(Brushes.Transparent.Color, Assert.IsAssignableFrom<ISolidColorBrush>(box.Background).Color);

            fixture.StateOf(box).Focus();
            Assert.Null(AdornerLayer.GetAdorner(box));
        }

        [AvaloniaFact]
        public void InitialValue_IsFormattedIntoTheField() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 4;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("4", ValueBox(stepper).Text);
        }

        [AvaloniaFact]
        public void UpButtonClick_IncrementsByIncrement() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 4;
            stepper.Increment = 2;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(UpButton(stepper)).Click();

            Assert.Equal(6, stepper.Value);
            Assert.Equal("6", ValueBox(stepper).Text);
        }

        [AvaloniaFact]
        public void DownButtonClick_DecrementsByIncrement() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 4;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(DownButton(stepper)).Click();

            Assert.Equal(3, stepper.Value);
        }

        [AvaloniaFact]
        public void KeyboardUpDown_StepsByIncrementAndShiftStepsByTen() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 10;
            stepper.Increment = 1;
            stepper.Maximum = 1000;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(ValueBox(stepper)).Focus();
            fixture.StateOf(ValueBox(stepper)).KeyDown(Key.Up);
            Assert.Equal(11, stepper.Value);

            fixture.StateOf(ValueBox(stepper)).KeyDown(Key.Down, KeyModifiers.Shift);
            Assert.Equal(1, stepper.Value);
        }

        [AvaloniaFact]
        public void Value_ClampsToMinimumAndMaximum() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Minimum = 0;
            stepper.Maximum = 5;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            stepper.Value = 100;
            Assert.Equal(5, stepper.Value);

            stepper.Value = -50;
            Assert.Equal(0, stepper.Value);
        }

        [AvaloniaFact]
        public void SetValueDirectly_StillClampsViaCoercion() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Minimum = 0;
            stepper.Maximum = 5;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            stepper.SetValue(Stepper.ValueProperty, 500);

            Assert.Equal(5, stepper.Value);
        }

        [AvaloniaFact]
        public void TypedValue_CommitsOnReturn() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Maximum = 1000;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            box.Text = "42";
            fixture.StateOf(box).Focus();
            fixture.StateOf(box).KeyDown(Key.Enter);

            Assert.Equal(42, stepper.Value);
        }

        [AvaloniaFact]
        public void TypedValue_CommitsOnBlurAndClampsToMaximum() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Maximum = 10;
            var host = new StackPanel();
            var other = new TextBox();
            host.Children.Add(stepper);
            host.Children.Add(other);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            box.Text = "999";
            fixture.StateOf(box).Focus();
            fixture.StateOf(other).Focus();

            Assert.Equal(10, stepper.Value);
        }

        [AvaloniaFact]
        public void Unit_ShowsAfterFieldOnlyWhenSet() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.False(UnitText(stepper).IsVisible);

            stepper.Unit = "items/s";
            Dispatcher.UIThread.RunJobs();

            Assert.True(UnitText(stepper).IsVisible);
            Assert.Equal("items/s", UnitText(stepper).Text);
        }

        [AvaloniaFact]
        public void ArrowCellHover_TurnsTheCellAndGlyphToEmphasis() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Button up = UpButton(stepper);
            Path glyph = up.GetVisualDescendants().OfType<Path>().Single();

            fixture.StateOf(up).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), glyph.Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            stepper.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), stepper.Opacity);
            Assert.Equal(1, ValueBox(stepper).Opacity);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingAroundTheWholeControl() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(ValueBox(stepper)).Focus();

            Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(stepper));
        }

        [AvaloniaFact]
        public void NoUnit_ArrowsSitFlushAtTheTrailingEdgeAndTheFieldFillsTheRest() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Width = 160;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Border frame = Frame(stepper);
            Button up = UpButton(stepper);
            TextBox box = ValueBox(stepper);

            double arrowRight = RightEdgeIn(up, stepper);
            double arrowLeft = LeftEdgeIn(up, stepper);
            double fieldRight = RightEdgeIn(box, stepper);

            Assert.Equal(160 - frame.BorderThickness.Right, arrowRight, 2);
            Assert.Equal(arrowLeft, fieldRight, 2);
        }

        [AvaloniaFact]
        public void WithUnit_ArrowsStayFlushAndTheUnitSitsBetweenTheFieldAndTheArrows() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Width = 160;
            stepper.Unit = "/s";
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Border frame = Frame(stepper);
            Button up = UpButton(stepper);
            TextBox box = ValueBox(stepper);
            TextBlock unit = UnitText(stepper);

            double arrowRight = RightEdgeIn(up, stepper);
            double arrowLeft = LeftEdgeIn(up, stepper);
            double unitRight = RightEdgeIn(unit, stepper);
            double fieldRight = RightEdgeIn(box, stepper);

            Assert.Equal(160 - frame.BorderThickness.Right, arrowRight, 2);
            Assert.Equal(arrowLeft, unitRight + unit.Margin.Right, 2);
            Assert.True(fieldRight < arrowLeft, "the field must shrink to leave room for the unit");
        }

        [AvaloniaFact]
        public void UnitMargin_TracksTheControlGlyphGapToken() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Unit = "/s";
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            TextBlock unit = UnitText(stepper);
            Application.Current!.Resources["Foreman.Size.ControlGlyphGap"] = 20.0;
            try {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(20, unit.Margin.Right);
            } finally {
                Application.Current!.Resources.Remove("Foreman.Size.ControlGlyphGap");
            }
        }

        private static double RightEdgeIn(Visual element, Visual ancestor) =>
            (element.TranslatePoint(new Point(element.Bounds.Width, 0), ancestor) ?? default).X;

        private static double LeftEdgeIn(Visual element, Visual ancestor) =>
            (element.TranslatePoint(new Point(0, 0), ancestor) ?? default).X;

        [AvaloniaFact]
        public void AutomationPeer_IsASpinnerWithRangeValue_AndReadsItsSpokenUnit() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Minimum = 0;
            stepper.Maximum = 50;
            stepper.Increment = 1;
            stepper.Value = 2;
            stepper.SpokenUnit = "pixels";
            Avalonia.Automation.AutomationProperties.SetName(stepper, "Border width");
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            Assert.Equal(AutomationControlType.Spinner, peer.GetAutomationControlType());
            Assert.Equal("stepper", peer.GetLocalizedControlType());
            Assert.Equal("Border width", peer.GetName());
            IRangeValueProvider range = peer.GetProvider<IRangeValueProvider>()!;
            Assert.Equal((2d, 0d, 50d, 1d, 10d), (range.Value, range.Minimum, range.Maximum, range.SmallChange, range.LargeChange));
            Assert.Equal("2 pixels", peer.GetProvider<IValueProvider>()!.Value);

            range.SetValue(14);
            Assert.Equal(14, stepper.Value);
            stepper.SpokenUnit = "points";
            Assert.Equal("14 points", peer.GetProvider<IValueProvider>()!.Value);
            stepper.SpokenUnit = null;
            Assert.Equal("14", peer.GetProvider<IValueProvider>()!.Value);
        }

        [AvaloniaFact]
        public void AutomationPeer_SpokenValue_TrimsTrailingZeros() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Minimum = 0;
            stepper.Maximum = 50;
            stepper.Decimals = 2;
            stepper.Value = 2.5;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            IValueProvider value = peer.GetProvider<IValueProvider>()!;
            Assert.Equal("2.5", value.Value);
            stepper.Value = 2;
            Assert.Equal("2", value.Value);
            stepper.ShowsPlusSign = true;
            Assert.Equal("plus 2", value.Value);
        }

        private static Stepper BorderWidth() {
            Stepper stepper = Themed();
            stepper.Minimum = 0;
            stepper.Maximum = 50;
            stepper.Value = 2;
            stepper.Unit = "px";
            stepper.SpokenUnit = "pixels";
            Avalonia.Automation.AutomationProperties.SetName(stepper, "Border width");
            return stepper;
        }

        [AvaloniaFact]
        public void AccessibleFocus_AfterTab_IsTheStepper_WhileTheFieldKeepsKeyboardFocus() {
            using var fixture = new KitFixture();
            Stepper stepper = BorderWidth();

            AutomationPeer? focus = AccessibleFocus.TabOnto(fixture.Window, stepper);

            Assert.True(ValueBox(stepper).IsFocused);
            Assert.NotNull(focus);
            Assert.Equal(("stepper", "Border width"), (focus.GetLocalizedControlType(), focus.GetName()));
            Assert.Equal(2, focus.GetProvider<IRangeValueProvider>()!.Value);
            Assert.Equal("2 pixels", focus.GetProvider<IValueProvider>()!.Value);
            Assert.True(focus.HasKeyboardFocus());
        }

        [AvaloniaFact]
        public void AutomationPeer_UpArrow_RaisesOneRangeValueChange() {
            using var fixture = new KitFixture();
            Stepper stepper = BorderWidth();
            AutomationPeer focus = AccessibleFocus.TabOnto(fixture.Window, stepper)!;
            var events = AccessibleFocus.Record(focus);

            fixture.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, stepper.Value);
            AutomationPropertyChangedEventArgs change = Assert.Single(events, e => e.Property == RangeValuePatternIdentifiers.ValueProperty);
            Assert.Equal((2d, 3d), ((double)change.OldValue!, (double)change.NewValue!));
        }

        [AvaloniaFact]
        public void Stepper_Empty_ShowsPlaceholderAndNoValue() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 5;
            stepper.Placeholder = "None";
            stepper.Unit = "%";
            stepper.IsEmpty = true;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            Assert.Equal("", box.Text);
            Assert.True(UnitText(stepper).IsVisible);
            Assert.Equal("%", UnitText(stepper).Text);
            Assert.Equal("None", box.PlaceholderText);
            TextBlock placeholder = box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder");
            Assert.True(placeholder.IsVisible);
            Assert.Equal(TextAlignment.Center, placeholder.TextAlignment);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), placeholder.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Stepper_IsWide_RaisesTheFieldMinimumTo72() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();
            Grid columns = stepper.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Columns");
            Assert.Equal(40, columns.ColumnDefinitions[0].MinWidth);

            stepper.IsWide = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(72, columns.ColumnDefinitions[0].MinWidth);

            stepper.IsWide = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(40, columns.ColumnDefinitions[0].MinWidth);
        }

        [AvaloniaFact]
        public void Stepper_EmptyThenStep_BecomesNonEmpty() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 5;
            stepper.Placeholder = "None";
            stepper.IsEmpty = true;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(UpButton(stepper)).Click();

            Assert.False(stepper.IsEmpty);
            Assert.Equal(6, stepper.Value);
            Assert.Equal("6", ValueBox(stepper).Text);
        }

        [AvaloniaFact]
        public void Stepper_EmptyThenAutomationSetValue_BecomesNonEmpty() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Placeholder = "None";
            stepper.IsEmpty = true;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            peer.GetProvider<IRangeValueProvider>()!.SetValue(7);

            Assert.False(stepper.IsEmpty);
            Assert.Equal("7", ValueBox(stepper).Text);
        }

        [AvaloniaFact]
        public void Stepper_Empty_SpeaksThePlaceholderWithTheUnit() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Placeholder = "None";
            stepper.Unit = "%";
            stepper.IsEmpty = true;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            Assert.Equal("None percent", peer.GetProvider<IValueProvider>()!.Value);
        }

        [AvaloniaFact]
        public void Stepper_EmptyThenTypedValue_BecomesNonEmpty() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.IsEmpty = true;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            box.Text = "12";
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

            Assert.False(stepper.IsEmpty);
            Assert.Equal(12, stepper.Value);
        }

        [AvaloniaFact]
        public void Stepper_PlusSign_FormatsAndSpeaks() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Minimum = -50;
            stepper.Value = 2;
            stepper.ShowsPlusSign = true;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("+2", ValueBox(stepper).Text);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            Assert.Equal("plus 2", peer.GetProvider<IValueProvider>()!.Value);

            stepper.Value = -3;
            Assert.Equal("-3", ValueBox(stepper).Text);
            Assert.Equal("-3", peer.GetProvider<IValueProvider>()!.Value);

            stepper.Value = 0;
            Assert.Equal("0", ValueBox(stepper).Text);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class StepperScaleTests {
        private static Grid Columns(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Columns");

        [AvaloniaFact]
        public void Height_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Stepper", null, out object? theme));
            var stepper = new Stepper { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22, stepper.Height);
            Assert.Equal(GridUnitType.Star, Columns(stepper).ColumnDefinitions[0].Width.GridUnitType);
            Assert.Equal(40, Columns(stepper).ColumnDefinitions[0].MinWidth);
            Assert.Equal(15, Columns(stepper).ColumnDefinitions[2].Width.Value);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33, stepper.Height);
            Assert.Equal(GridUnitType.Star, Columns(stepper).ColumnDefinitions[0].Width.GridUnitType);
            Assert.Equal(60, Columns(stepper).ColumnDefinitions[0].MinWidth);
            Assert.Equal(22.5, Columns(stepper).ColumnDefinitions[2].Width.Value);
        }

        [AvaloniaFact]
        public void ChevronViewbox_TracksScaleContractWhileThePathStaysAtItsMasterSize() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Stepper", null, out object? theme));
            var stepper = new Stepper { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Button up = stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Up");
            Path chevron = up.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Glyph");
            Viewbox box = up.GetVisualDescendants().OfType<Viewbox>().Single();
            Assert.Equal(10, box.Bounds.Width);
            Assert.Equal(10, chevron.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(15, box.Bounds.Width);
            Assert.Equal(10, chevron.Bounds.Width);
        }
    }
}
