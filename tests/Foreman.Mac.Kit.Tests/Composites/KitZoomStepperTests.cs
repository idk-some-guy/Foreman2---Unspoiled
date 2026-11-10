using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using System.Windows.Input;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitZoomStepperTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitZoomStepperTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ZoomStepper", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ZoomStepper NewStepper(double value = 100) =>
            new() { Theme = KitZoomStepperTheme(), Value = value };

        private static Window Shown(ZoomStepper stepper) {
            var window = new Window { Width = 200, Height = 100, Content = stepper };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static (Window Window, PopoverHost Host) ShownInHost(ZoomStepper stepper) {
            var host = new PopoverHost { Width = 300, Height = 200 };
            host.Surface.Children.Add(stepper);
            var window = new Window { Width = 300, Height = 200, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, host);
        }

        private static Button Readout(ZoomStepper stepper) =>
            stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Readout");

        private static Button[] MicroButtons(ZoomStepper stepper) => [
            stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Minus"),
            stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Plus"),
        ];

        private static TextBlock ValueText(ZoomStepper stepper) =>
            stepper.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Value");

        private sealed class StubCommand(Action<object?> onExecute) : ICommand {
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => onExecute(parameter);
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }

        [AvaloniaFact]
        public void KitZoomStepper_IsAControlThemeTargetingZoomStepper() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ZoomStepper), KitZoomStepperTheme().TargetType);
        }

        [AvaloniaFact]
        public void Height_ResolvesControlHeightRegular() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            Assert.Equal(22, stepper.Bounds.Height);
        }

        [AvaloniaFact]
        public void Readout_StaysFixedWidth_AcrossTheFullZoomRange() {
            using var fixture = new KitFixture();
            foreach (double value in new[] { 25, 100, 400 }) {
                ZoomStepper stepper = NewStepper(value);
                Shown(stepper);
                Assert.Equal(42, Readout(stepper).Bounds.Width);
            }
        }

        [AvaloniaFact]
        public void Readout_BoxHeight_ResolvesZoomReadoutHeight() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            Assert.Equal(16, Readout(stepper).Bounds.Height);
        }

        [AvaloniaFact]
        public void MicroButtons_AreTwentyBySixteen_WithFourPointRadius() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            Assert.All(MicroButtons(stepper), b => {
                Assert.Equal(20, b.Bounds.Width);
                Assert.Equal(16, b.Bounds.Height);
                Assert.Equal(new CornerRadius(4), b.CornerRadius);
            });
        }

        [AvaloniaFact]
        public void MicroButtons_CarryTheMinusAndPlusFillGlyphs() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            Button[] buttons = MicroButtons(stepper);
            Assert.Equal(2, buttons.Length);
            Geometry minus = Assert.IsAssignableFrom<Geometry>(Application.Current!.FindResource("Kit.Glyph.Minus.12"));
            Geometry plus = Assert.IsAssignableFrom<Geometry>(Application.Current!.FindResource("Kit.Glyph.Plus.12"));
            Path minusPath = buttons[0].GetVisualDescendants().OfType<Path>().Single();
            Path plusPath = buttons[1].GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(minus.ToString(), minusPath.Data?.ToString());
            Assert.Equal(plus.ToString(), plusPath.Data?.ToString());
        }

        [AvaloniaFact]
        public void Readout_Click_InvokesActualSizeCommand_WhenNormal() {
            using var fixture = new KitFixture();
            object? received = "unset";
            ZoomStepper stepper = NewStepper();
            stepper.ActualSizeCommand = new StubCommand(p => received = p);
            Window window = Shown(stepper);

            new ControlState(Readout(stepper), window).Click();

            Assert.Null(received);
        }

        [AvaloniaFact]
        public void Readout_Tooltip_And_AutomationName_NameActualSizeShortcut_WhenNormal() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            Button readout = Readout(stepper);
            Assert.Equal("Actual Size (⌘0)", ToolTip.GetTip(readout));
            Assert.Equal("Actual Size (⌘0)", AutomationProperties.GetName(readout));
        }

        [AvaloniaFact]
        public void Readout_IsFocusable_WithAFocusRing() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            Button readout = Readout(stepper);
            Assert.True(readout.Focusable);
            Assert.True(FocusRing.GetIsEnabled(readout));
        }

        [AvaloniaFact]
        public void Readout_SpaceAndReturn_ActivateActualSize() {
            using var fixture = new KitFixture();
            int invocations = 0;
            ZoomStepper stepper = NewStepper();
            stepper.ActualSizeCommand = new StubCommand(_ => invocations++);
            Window window = Shown(stepper);
            ControlState readoutState = new ControlState(Readout(stepper), window);
            readoutState.Focus();

            readoutState.KeyDown(Key.Space);
            readoutState.KeyUp(Key.Space);
            readoutState.KeyDown(Key.Return);
            readoutState.KeyUp(Key.Return);

            Assert.Equal(2, invocations);
        }

        [AvaloniaFact]
        public void Value_ClampsToTheTwentyFiveToFourHundredRange() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();

            stepper.Value = 10;
            Assert.Equal(25, stepper.Value);

            stepper.Value = 900;
            Assert.Equal(400, stepper.Value);
        }

        private static StatePanel ErrorPanel() {
            Assert.True(Application.Current!.TryGetResource("Kit.StatePanel", null, out object? theme));
            Assert.True(Application.Current!.TryGetResource("Kit.ButtonRow", null, out object? rowTheme));
            Assert.True(Application.Current!.TryGetResource("Kit.Button", null, out object? buttonTheme));
            var row = new ButtonRow { Theme = Assert.IsType<ControlTheme>(rowTheme) };
            var tryAgain = new Button { Theme = Assert.IsType<ControlTheme>(buttonTheme), Content = "Try Again" };
            ButtonRow.SetRole(tryAgain, ButtonRowRole.Default);
            row.Items.Add(tryAgain);
            return new StatePanel { Theme = Assert.IsType<ControlTheme>(theme), Shape = StatePanelShape.Error, ForceCompact = true, Headline = "Couldn\u2019t solve this graph", Buttons = row };
        }

        [AvaloniaFact]
        public void Readout_Idle_ShowsTheNumber_AndTheStatusTakesNoWidth() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper(140);
            Shown(stepper);

            Assert.True(ValueText(stepper).IsVisible);
            Assert.Equal("140 %", ValueText(stepper).Text);
            Assert.Equal(0, stepper.SolveStatus.Bounds.Width);
        }

        [AvaloniaFact]
        public void Readout_NotIdle_ShowsTheStatusInPlaceOfTheNumber_WithoutResizingTheReadout() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper(140);
            Shown(stepper);

            foreach (SolveStatusState state in new[] { SolveStatusState.Solving, SolveStatusState.Failed }) {
                stepper.SolveStatus.State = state;
                Dispatcher.UIThread.RunJobs();

                Assert.False(ValueText(stepper).IsVisible);
                Assert.Equal(16, stepper.SolveStatus.Bounds.Width);
                Assert.Equal(42, Readout(stepper).Bounds.Width);
                Assert.Equal(16, Readout(stepper).Bounds.Height);
            }

            stepper.SolveStatus.State = SolveStatusState.Idle;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ValueText(stepper).IsVisible);
        }

        [AvaloniaFact]
        public void MicroButtons_StayEnabled_WhileSolving() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            stepper.ZoomInCommand = new StubCommand(_ => { });
            stepper.ZoomOutCommand = new StubCommand(_ => { });
            Shown(stepper);

            stepper.SolveStatus.State = SolveStatusState.Solving;
            Dispatcher.UIThread.RunJobs();

            Assert.All(MicroButtons(stepper), b => Assert.True(b.IsEnabled));
        }

        [AvaloniaFact]
        public void ReadoutTooltip_ReadsSolvingOrSolveFailedDashZoom() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper(140);
            Shown(stepper);
            Button readout = Readout(stepper);

            stepper.SolveStatus.State = SolveStatusState.Solving;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Solving \u2014 zoom 140 %", ToolTip.GetTip(readout));
            Assert.Equal("Solving \u2014 zoom 140 %", AutomationProperties.GetName(readout));

            stepper.SolveStatus.State = SolveStatusState.Failed;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Solve failed \u2014 zoom 140 %", ToolTip.GetTip(readout));
            Assert.Equal("Solve failed \u2014 zoom 140 %", AutomationProperties.GetName(readout));

            stepper.SolveStatus.State = SolveStatusState.Idle;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Actual Size (\u23180)", ToolTip.GetTip(readout));
        }

        [AvaloniaFact]
        public void Readout_ClickWhileFailed_OpensTheStatusPopover_InsteadOfActualSize() {
            using var fixture = new KitFixture();
            bool actualSizeInvoked = false;
            ZoomStepper stepper = NewStepper();
            stepper.ActualSizeCommand = new StubCommand(_ => actualSizeInvoked = true);
            stepper.SolveStatus.ErrorContent = ErrorPanel();
            (Window window, _) = ShownInHost(stepper);

            stepper.SolveStatus.State = SolveStatusState.Failed;
            Dispatcher.UIThread.RunJobs();
            new ControlState(Readout(stepper), window).Click();

            Assert.True(stepper.SolveStatus.IsErrorPopoverOpen);
            Assert.False(actualSizeInvoked);
            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(popover.IsOpen);
            Assert.Same(stepper.SolveStatus.ErrorContent, popover.Content);
        }

        [AvaloniaFact]
        public void Readout_ClickWhileSolving_StillInvokesActualSize() {
            using var fixture = new KitFixture();
            bool actualSizeInvoked = false;
            ZoomStepper stepper = NewStepper();
            stepper.ActualSizeCommand = new StubCommand(_ => actualSizeInvoked = true);
            Window window = Shown(stepper);

            stepper.SolveStatus.State = SolveStatusState.Solving;
            Dispatcher.UIThread.RunJobs();
            new ControlState(Readout(stepper), window).Click();

            Assert.True(actualSizeInvoked);
        }

        [AvaloniaFact]
        public void TriangleClickInsideTheReadout_OpensThePopover_WithoutActualSize() {
            using var fixture = new KitFixture();
            bool actualSizeInvoked = false;
            ZoomStepper stepper = NewStepper();
            stepper.ActualSizeCommand = new StubCommand(_ => actualSizeInvoked = true);
            stepper.SolveStatus.ErrorContent = ErrorPanel();
            (Window window, _) = ShownInHost(stepper);

            stepper.SolveStatus.State = SolveStatusState.Failed;
            Dispatcher.UIThread.RunJobs();
            Button triangle = stepper.SolveStatus.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Triangle");
            new ControlState(triangle, window).Click();

            Assert.True(stepper.SolveStatus.IsErrorPopoverOpen);
            Assert.False(actualSizeInvoked);
        }

        [AvaloniaFact]
        public void Retemplating_RehomesTheSameSolveStatusInstance() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Window window = Shown(stepper);
            SolveStatus before = stepper.SolveStatus;
            before.State = SolveStatusState.Solving;

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            stepper.Theme = KitZoomStepperTheme();
            window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(before, Readout(stepper).GetVisualDescendants().OfType<SolveStatus>().Single());
            Assert.Equal(SolveStatusState.Solving, stepper.SolveStatus.State);
        }

        [AvaloniaFact]
        public void AutomationPeer_IsAGroupNamedZoom() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
            Assert.Equal("Zoom", peer.GetName());
        }

        [AvaloniaFact]
        public void AutomationPeer_HasThreeChildren_ZoomOutReadoutZoomIn_InOrder() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper(140);
            Shown(stepper);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            AutomationPeer[] children = [.. peer.GetChildren()];

            Assert.Equal(3, children.Length);
            Assert.Equal("Zoom Out", children[0].GetName());
            Assert.Equal(AutomationProperties.GetName(Readout(stepper)), children[1].GetName());
            Assert.Equal("Zoom In", children[2].GetName());
        }

        [AvaloniaFact]
        public void AutomationPeer_ActualSizeChild_ValueReadsThePercentageInWords() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper(140);
            Shown(stepper);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(stepper);
            AutomationPeer readoutPeer = peer.GetChildren()[1];

            var value = Assert.IsAssignableFrom<IValueProvider>(readoutPeer);
            Assert.Equal("140 percent", value.Value);
        }
    }
}
