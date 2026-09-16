using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
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
using System.Reflection;
using System.Threading;
using System.Windows.Input;
using Xunit;
using SpinnerControl = Foreman.Mac.Kit.Controls.Spinner;

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

        //A popover-owning stepper needs a PopoverHost ancestor, like every other popover in the kit.
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

        private static SpinnerControl Spinner(ZoomStepper stepper) =>
            stepper.GetVisualDescendants().OfType<SpinnerControl>().Single(s => s.Name == "PART_Spinner");

        private static Path ErrorGlyph(ZoomStepper stepper) =>
            stepper.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_ErrorGlyph");

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

        [AvaloniaFact]
        public void MicroButtons_StayEnabled_WhileSolving() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            stepper.ZoomInCommand = new StubCommand(_ => { });
            stepper.ZoomOutCommand = new StubCommand(_ => { });
            Shown(stepper);

            stepper.IsSolving = true;
            Dispatcher.UIThread.RunJobs();

            Assert.All(MicroButtons(stepper), b => Assert.True(b.IsEnabled));
        }

        [AvaloniaFact]
        public void Solving_SpinnerResolvesSpinnerSmallSize() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.IsSolving = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(14, Spinner(stepper).Diameter);
        }

        [AvaloniaFact]
        public void Solving_DoesNotSwap_BeforeStatePanelDelay() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.IsSolving = true;
            Dispatcher.UIThread.RunJobs();

            AnimationClock.Settle(200);
            Assert.Equal(0, Spinner(stepper).Opacity);

            AnimationClock.Settle(650);
            Assert.Equal(1, Spinner(stepper).Opacity);
        }

        [AvaloniaFact]
        public void Solving_EndsInsideTheGracePeriod_ShowsNothing() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.IsSolving = true;
            stepper.IsSolving = false;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(650);

            SpinnerControl spinner = Spinner(stepper);
            Assert.Equal(0, spinner.Opacity);
            Assert.False(spinner.IsVisible);
            Assert.Equal(1, ValueText(stepper).Opacity);
        }

        [AvaloniaFact]
        public void Solving_SpinnerFadesOutOverTheCrossFadeDuration_ThenHides() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.IsSolving = true;
            AnimationClock.Settle(650);
            SpinnerControl spinner = Spinner(stepper);
            Assert.True(spinner.IsVisible);
            Assert.Equal(1, spinner.Opacity);

            stepper.IsSolving = false;
            Dispatcher.UIThread.RunJobs();
            //Mid fade-out: the spinner is still visible so the cross-fade actually renders (it wasn't yanked in the same pass).
            AnimationClock.Settle(60);
            Assert.True(spinner.IsVisible);

            AnimationClock.Settle(300);
            Assert.Equal(0, spinner.Opacity);
            Assert.False(spinner.IsVisible);
        }

        [AvaloniaFact]
        public void Solving_PastTheGrace_SpinnerIsVisibleAndAnimating() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.IsSolving = true;
            AnimationClock.Settle(650);

            SpinnerControl spinner = Spinner(stepper);
            Assert.True(spinner.IsVisible);
            Assert.Equal(1, spinner.Opacity);
            Assert.Contains("animating", spinner.Classes);
        }

        //A themeless PART_Spinner builds no template, so it stays zero-sized even once IsVisible/Opacity flip -
        //this pins real layout size, not just the flags, with real elapsed time driving the render clock.
        [AvaloniaFact]
        public void Solving_SetBeforeShow_SpinnerBecomesVisibleAndAnimatingAfterRealTimePastTheGrace() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            stepper.IsSolving = true;
            Shown(stepper);

            Thread.Sleep(650);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            SpinnerControl spinner = Spinner(stepper);
            Assert.True(spinner.IsVisible);
            Assert.Equal(1, spinner.Opacity);
            Assert.Contains("animating", spinner.Classes);
            Assert.True(spinner.Bounds.Width > 0);
            Assert.True(spinner.Bounds.Height > 0);

            //Layout position only (Bounds.Position through each ancestor), not TranslatePoint/TransformToVisual -
            //those fold in the head's own RenderTransform, and a rotated square's AABB grows past its corners
            //at any non-cardinal angle even though the circle it draws does not.
            Arc head = spinner.GetVisualDescendants().OfType<Arc>().Single(a => a.Name == "PART_Head");
            Point headOffset = default;
            for (Visual? v = head; v is not null && v != spinner; v = v.GetVisualParent())
                if (v is Avalonia.Layout.Layoutable layoutable)
                    headOffset += layoutable.Bounds.Position;
            Rect headInSpinner = new(headOffset, head.Bounds.Size);
            Assert.True(new Rect(spinner.Bounds.Size).Contains(headInSpinner),
                $"head's laid-out bounds {headInSpinner} sit outside the spinner's own bounds {spinner.Bounds}");
        }

        [AvaloniaFact]
        public void Solving_AutomationValue_StillContainsThePercentage() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper(140);
            Shown(stepper);

            stepper.IsSolving = true;
            Dispatcher.UIThread.RunJobs();

            Button readout = Readout(stepper);
            Assert.Contains("140 %", AutomationProperties.GetName(readout));
            Assert.Equal("Solving — zoom 140 %", ToolTip.GetTip(readout));
        }

        [AvaloniaFact]
        public void SolveError_AutomationValueAndTooltip_LeadWithSolveFailed() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper(140);
            Shown(stepper);

            stepper.HasSolveError = true;
            Dispatcher.UIThread.RunJobs();

            Button readout = Readout(stepper);
            Assert.Equal("Solve failed — zoom 140 %", ToolTip.GetTip(readout));
            Assert.Equal("Solve failed — zoom 140 %", AutomationProperties.GetName(readout));
        }

        [AvaloniaFact]
        public void StateTransitions_UseTabLabelCrossFadeDuration() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.HasSolveError = true;
            Dispatcher.UIThread.RunJobs();

            var valueFade = Assert.Single(ValueText(stepper).Transitions!.OfType<DoubleTransition>());
            Assert.Equal(Motion.TabLabelCrossFadeDuration, valueFade.Duration);
            var glyphFade = Assert.Single(ErrorGlyph(stepper).Transitions!.OfType<DoubleTransition>());
            Assert.Equal(Motion.TabLabelCrossFadeDuration, glyphFade.Duration);
        }

        [AvaloniaFact]
        public void SolveError_ReadoutShowsAlertGlyph_InErrorColor() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.HasSolveError = true;
            AnimationClock.Settle(150);

            Path glyph = ErrorGlyph(stepper);
            Assert.Equal(1, glyph.Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.Error").ToImmutable(), glyph.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void SolveError_ReadoutClick_OpensThePopoverSlot_InsteadOfActualSize() {
            using var fixture = new KitFixture();
            bool actualSizeInvoked = false;
            ZoomStepper stepper = NewStepper();
            stepper.ActualSizeCommand = new StubCommand(_ => actualSizeInvoked = true);
            stepper.ErrorPopoverContent = new TextBlock { Text = "Beacon count must be 0 or more." };
            (Window window, _) = ShownInHost(stepper);

            stepper.HasSolveError = true;
            Dispatcher.UIThread.RunJobs();
            new ControlState(Readout(stepper), window).Click();

            Assert.True(stepper.IsErrorPopoverOpen);
            Assert.False(actualSizeInvoked);

            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(popover.IsOpen);
            Assert.Same(stepper.ErrorPopoverContent, popover.Content);
            Assert.Same(Readout(stepper), popover.Anchor);
        }

        [AvaloniaFact]
        public void ClearingSolveError_ClosesThePopoverSlot_AndReturnsTheReadout() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            (Window window, _) = ShownInHost(stepper);

            stepper.HasSolveError = true;
            AnimationClock.Settle(150);
            new ControlState(Readout(stepper), window).Click();
            Assert.True(stepper.IsErrorPopoverOpen);

            stepper.HasSolveError = false;
            AnimationClock.Settle(150);

            Assert.False(stepper.IsErrorPopoverOpen);
            Assert.Equal(0, ErrorGlyph(stepper).Opacity);
            Assert.Equal(1, ValueText(stepper).Opacity);
            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.False(popover.IsOpen);
        }

        [AvaloniaFact]
        public void Escape_ClosesTheErrorPopover_ReturnsTheReadout_LeavesHasSolveErrorForTheHost() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            (Window window, _) = ShownInHost(stepper);
            int dismissed = 0;
            stepper.ErrorDismissed += (_, _) => dismissed++;

            stepper.HasSolveError = true;
            AnimationClock.Settle(150);
            new ControlState(Readout(stepper), window).Click();
            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single();

            popover.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(150);

            Assert.False(popover.IsOpen);
            Assert.False(stepper.IsErrorPopoverOpen);
            Assert.True(stepper.HasSolveError);
            Assert.Equal(1, ValueText(stepper).Opacity);
            Assert.Equal(0, ErrorGlyph(stepper).Opacity);
            Assert.Equal(1, dismissed);
        }

        [AvaloniaFact]
        public void OutsidePress_ClosesTheErrorPopover_ReturnsTheReadout_LeavesHasSolveErrorForTheHost() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            (Window window, _) = ShownInHost(stepper);
            int dismissed = 0;
            stepper.ErrorDismissed += (_, _) => dismissed++;

            stepper.HasSolveError = true;
            AnimationClock.Settle(150);
            new ControlState(Readout(stepper), window).Click();
            Assert.True(stepper.IsErrorPopoverOpen);

            window.MouseDown(new Point(295, 195), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Point(295, 195), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(150);

            Assert.False(stepper.IsErrorPopoverOpen);
            Assert.True(stepper.HasSolveError);
            Assert.Equal(1, ValueText(stepper).Opacity);
            Assert.Equal(0, ErrorGlyph(stepper).Opacity);
            Assert.Equal(1, dismissed);
        }

        [AvaloniaFact]
        public void OpeningTheErrorPopover_WithoutAPopoverHostAncestor_Throws() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.HasSolveError = true;
            Dispatcher.UIThread.RunJobs();

            var ex = Assert.Throws<InvalidOperationException>(() => stepper.IsErrorPopoverOpen = true);
            Assert.Contains("ZoomStepper", ex.Message);
        }

        [AvaloniaFact]
        public void OpeningTheErrorPopover_FromTemplateApplied_DoesNotCorruptTheInFlightLayout() {
            using var fixture = new KitFixture();
            var host = new PopoverHost { Width = 300, Height = 200 };
            ZoomStepper stepper = NewStepper();
            stepper.TemplateApplied += (_, _) => {
                stepper.HasSolveError = true;
                stepper.IsErrorPopoverOpen = true;
            };
            host.Surface.Children.Add(stepper);
            var window = new Window { Width = 300, Height = 200, Content = host };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(popover.IsOpen);
        }

        [AvaloniaFact]
        public void ClosingTheWindow_WithTheErrorPopoverOpen_DoesNotThrow_AndRemovesThePopover() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            (Window window, PopoverHost host) = ShownInHost(stepper);

            stepper.HasSolveError = true;
            Dispatcher.UIThread.RunJobs();
            new ControlState(Readout(stepper), window).Click();
            Assert.True(stepper.IsErrorPopoverOpen);
            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(popover.IsOpen);

            Exception? caught = null;
            try {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            } catch (Exception ex) {
                caught = ex;
            }

            Assert.True(caught is null, caught?.ToString());
            Assert.DoesNotContain(popover, host.Surface.Children);
        }

        private sealed class DisposeSpy : IDisposable {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        [AvaloniaFact]
        public void Detach_MidSpinnerFade_DisposesTheOpacitySubscription_AndReattachRestoresTheErrorPopover() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            (Window window, PopoverHost host) = ShownInHost(stepper);

            //Standing in for an in-flight fade's live subscription (Solving -> Normal), without depending on transition timing.
            var spy = new DisposeSpy();
            FieldInfo subField = typeof(ZoomStepper).GetField("spinnerHideSub", BindingFlags.NonPublic | BindingFlags.Instance)!;
            subField.SetValue(stepper, spy);

            host.Surface.Children.Remove(stepper);
            Dispatcher.UIThread.RunJobs();

            Assert.True(spy.Disposed);
            Assert.Null(subField.GetValue(stepper));

            stepper.HasSolveError = true;
            Path errorGlyph = ErrorGlyph(stepper);
            //Simulate the readout's error visuals having been reset while detached.
            errorGlyph.Opacity = 0;

            host.Surface.Children.Add(stepper);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, errorGlyph.Opacity);
        }

        [AvaloniaFact]
        public void Pumping_ErrorPopoverOpenThenClose_SettlesToABoundedRebuildCount() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            (Window window, _) = ShownInHost(stepper);

            stepper.HasSolveError = true;
            Dispatcher.UIThread.RunJobs();
            new ControlState(Readout(stepper), window).Click();

            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            int layouts = 0;
            popover.LayoutUpdated += (_, _) => layouts++;

            AnimationClock.Settle();
            Assert.True(popover.IsOpen);

            stepper.HasSolveError = false;
            AnimationClock.Settle();

            Assert.False(popover.IsOpen);
            Assert.True(layouts <= 40, $"ZoomStepper's error popover laid out {layouts} times settling one open/close pair.");
        }

        [AvaloniaFact]
        public void ReduceMotion_SpinnerSlowsRatherThanStopping() {
            Motion.ReduceMotion = true;
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.IsSolving = true;
            Dispatcher.UIThread.RunJobs();

            SpinnerControl spinner = Spinner(stepper);
            Assert.Equal(Motion.SpinnerReducedMotionDuration, spinner.RotationDuration);
            Assert.Contains("reduce-motion", spinner.Classes);

            //The cross-fade itself is already opacity-only (addendum §08): unchanged with the flag on, same delayed transition as with it off.
            var opacity = Assert.Single(spinner.Transitions!.OfType<DoubleTransition>());
            Assert.Equal(Motion.StatePanelDelay, opacity.Delay);
        }

        [AvaloniaFact]
        public void ReduceMotion_Off_SpinnerUsesTheNormalRotationDuration() {
            using var fixture = new KitFixture();
            ZoomStepper stepper = NewStepper();
            Shown(stepper);

            stepper.IsSolving = true;
            Dispatcher.UIThread.RunJobs();

            SpinnerControl spinner = Spinner(stepper);
            Assert.Equal(Motion.SpinnerDuration, spinner.RotationDuration);
            Assert.DoesNotContain("reduce-motion", spinner.Classes);

            var opacity = Assert.Single(spinner.Transitions!.OfType<DoubleTransition>());
            Assert.Equal(Motion.StatePanelDelay, opacity.Delay);
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
