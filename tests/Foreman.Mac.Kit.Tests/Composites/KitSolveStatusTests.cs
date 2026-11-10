using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;
using SpinnerControl = Foreman.Mac.Kit.Controls.Spinner;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitSolveStatusTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme Theme(string key) {
            Assert.True(Application.Current!.TryGetResource(key, null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static SolveStatus NewStatus(SolveStatusState state = SolveStatusState.Idle) =>
            new() { Theme = Theme("Kit.SolveStatus"), State = state };

        private static Window Shown(SolveStatus status) {
            var window = new Window { Width = 200, Height = 100, Content = status };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static (Window Window, PopoverHost Host) ShownInHost(SolveStatus status) {
            var host = new PopoverHost { Width = 300, Height = 200 };
            host.Surface.Children.Add(status);
            var window = new Window { Width = 300, Height = 200, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, host);
        }

        private static T Part<T>(SolveStatus status, string name) where T : Control =>
            status.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

        private static Panel Slot(SolveStatus status) => Part<Panel>(status, "PART_Slot");
        private static SpinnerControl Spinner(SolveStatus status) => Part<SpinnerControl>(status, "PART_Spinner");
        private static Button Triangle(SolveStatus status) => Part<Button>(status, "PART_Triangle");
        private static Path Glyph(SolveStatus status) => Part<Path>(status, "PART_Glyph");
        private static Viewbox GlyphBox(SolveStatus status) => Part<Viewbox>(status, "PART_GlyphViewbox");

        private static StatePanel ErrorPanel(bool withShowNode) {
            var row = new ButtonRow { Theme = Theme("Kit.ButtonRow") };
            if (withShowNode) {
                var showNode = new Button { Theme = Theme("Kit.Button"), Content = "Show Node" };
                ButtonRow.SetRole(showNode, ButtonRowRole.Other);
                row.Items.Add(showNode);
            }
            var tryAgain = new Button { Theme = Theme("Kit.Button"), Content = "Try Again" };
            ButtonRow.SetRole(tryAgain, ButtonRowRole.Default);
            row.Items.Add(tryAgain);
            return new StatePanel {
                Theme = Theme("Kit.StatePanel"),
                Shape = StatePanelShape.Error,
                ForceCompact = true,
                Headline = "Couldn’t solve this graph",
                Sentence = "The solver stopped before it found a result.",
                Buttons = row,
            };
        }

        [AvaloniaFact]
        public void KitSolveStatus_IsAControlThemeTargetingSolveStatus() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(SolveStatus), Theme("Kit.SolveStatus").TargetType);
        }

        [AvaloniaFact]
        public void Idle_CollapsesToWidthZero_AndShowsNothing() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus();
            Shown(status);

            Assert.Equal(0, status.Bounds.Width);
            Assert.False(Spinner(status).IsVisible);
            Assert.False(Triangle(status).IsVisible);
        }

        [AvaloniaFact]
        public void Solving_Spinner14TimesStep_In16Slot() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Solving);
            Shown(status);

            Assert.Equal(new Size(16, 16), Slot(status).Bounds.Size);
            SpinnerControl spinner = Spinner(status);
            Assert.True(spinner.IsVisible);
            Assert.Equal(14, spinner.Diameter);
            Assert.Equal(1.5, spinner.StrokeWidth);
            Assert.Equal(new Size(14, 14), spinner.Bounds.Size);
            Assert.Equal(1, spinner.Bounds.X, 3);
            Assert.Equal(1, spinner.Bounds.Y, 3);
            Assert.False(Triangle(status).IsVisible);
        }

        [AvaloniaFact]
        public void Failed_AlertGlyph16Warning_InTheSlot() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            Shown(status);

            Assert.Equal(new Size(16, 16), Slot(status).Bounds.Size);
            Assert.True(Triangle(status).IsVisible);
            Assert.Equal(new Size(16, 16), GlyphBox(status).Bounds.Size);
            Path glyph = Glyph(status);
            Geometry alert = Assert.IsAssignableFrom<Geometry>(Application.Current!.FindResource("Kit.Glyph.Alert.16"));
            Assert.Equal(alert.ToString(), glyph.Data?.ToString());
            Assert.Equal(fixture.Brush("Foreman.Color.Warning").ToImmutable(), glyph.Stroke?.ToImmutable());
            Assert.False(Spinner(status).IsVisible);
        }

        [AvaloniaFact]
        public void StateSwaps_AreInstant_WithNoFadeOnEitherPart() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus();
            Shown(status);

            status.State = SolveStatusState.Solving;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Spinner(status).IsVisible);
            Assert.Equal(1, Spinner(status).Opacity);
            Assert.Empty(Spinner(status).Transitions ?? []);

            status.State = SolveStatusState.Failed;
            Dispatcher.UIThread.RunJobs();
            Assert.False(Spinner(status).IsVisible);
            Assert.True(Triangle(status).IsVisible);
            Assert.Equal(1, Triangle(status).Opacity);
            Assert.Empty(Triangle(status).Transitions ?? []);

            status.State = SolveStatusState.Idle;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, status.Bounds.Width);
        }

        [AvaloniaFact]
        public void Solving_SpinnerAnimatesOnlyWhileVisible() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus();
            Shown(status);
            Assert.DoesNotContain("animating", Spinner(status).Classes);

            status.State = SolveStatusState.Solving;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("animating", Spinner(status).Classes);

            status.State = SolveStatusState.Failed;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("animating", Spinner(status).Classes);
        }

        [AvaloniaFact]
        public void Triangle_IsBorderless_NotATabStop_AndTakesNoFocusOnPress() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            Shown(status);

            Button triangle = Triangle(status);
            Assert.False(triangle.IsTabStop);
            Assert.False(triangle.Focusable);
            Assert.Equal(new Thickness(0), triangle.BorderThickness);
        }

        [AvaloniaFact]
        public void Triangle_OpensAPopoverFrame_NotASheet_HostingTheSuppliedErrorPanel() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            StatePanel panel = ErrorPanel(withShowNode: true);
            status.ErrorContent = panel;
            (Window window, PopoverHost host) = ShownInHost(status);

            new ControlState(Triangle(status), window).Click();

            Assert.True(status.IsErrorPopoverOpen);
            PopoverFrame frame = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(frame.IsOpen);
            Assert.Same(host.Surface, frame.GetVisualParent());
            Assert.Same(panel, frame.Content);
            Assert.Same(status, frame.Anchor);
            Assert.Equal(272, frame.PanelWidth);
            Assert.Empty(window.GetVisualDescendants().OfType<SheetFrame>());
            Assert.Empty(window.GetVisualDescendants().OfType<Scrim>());
            Assert.Contains(frame, panel.GetVisualAncestors());
        }

        [AvaloniaFact]
        public void Triangle_Invoke_OpensTheSamePopover() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: false);
            (Window window, _) = ShownInHost(status);

            var invoke = (Avalonia.Automation.Provider.IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(Triangle(status));
            invoke.Invoke();
            Dispatcher.UIThread.RunJobs();

            Assert.True(status.IsErrorPopoverOpen);
            Assert.True(window.GetVisualDescendants().OfType<PopoverFrame>().Single().IsOpen);
        }

        [AvaloniaFact]
        public void Escape_ClosesThePopover_RaisesErrorDismissedOnce_LeavesTheStateFailed() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: false);
            (Window window, _) = ShownInHost(status);
            int dismissed = 0;
            status.ErrorDismissed += (_, _) => dismissed++;
            new ControlState(Triangle(status), window).Click();
            PopoverFrame frame = window.GetVisualDescendants().OfType<PopoverFrame>().Single();

            frame.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(150);

            Assert.False(frame.IsOpen);
            Assert.False(status.IsErrorPopoverOpen);
            Assert.Equal(SolveStatusState.Failed, status.State);
            Assert.True(Triangle(status).IsVisible);
            Assert.Equal(1, dismissed);
        }

        [AvaloniaFact]
        public void OutsidePress_ClosesThePopover_RaisesErrorDismissedOnce() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: false);
            (Window window, _) = ShownInHost(status);
            int dismissed = 0;
            status.ErrorDismissed += (_, _) => dismissed++;
            new ControlState(Triangle(status), window).Click();
            Assert.True(status.IsErrorPopoverOpen);

            window.MouseDown(new Point(295, 195), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Point(295, 195), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(150);

            Assert.False(status.IsErrorPopoverOpen);
            Assert.Equal(SolveStatusState.Failed, status.State);
            Assert.Equal(1, dismissed);
        }

        [AvaloniaFact]
        public void LeavingFailed_ClosesTheOpenPopover() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: false);
            (Window window, _) = ShownInHost(status);
            new ControlState(Triangle(status), window).Click();
            PopoverFrame frame = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(frame.IsOpen);

            status.State = SolveStatusState.Idle;
            AnimationClock.Settle(150);

            Assert.False(status.IsErrorPopoverOpen);
            Assert.False(frame.IsOpen);
        }

        [AvaloniaFact]
        public void IsErrorPopoverOpen_WhileNotFailed_StaysClosed() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Solving);
            (Window window, _) = ShownInHost(status);

            status.IsErrorPopoverOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(status.IsErrorPopoverOpen);
            Assert.Empty(window.GetVisualDescendants().OfType<PopoverFrame>());
        }

        [AvaloniaFact]
        public void Opening_WithoutAHostOrAnAncestor_Throws() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            Shown(status);

            var ex = Assert.Throws<InvalidOperationException>(() => status.IsErrorPopoverOpen = true);
            Assert.Contains("SolveStatus", ex.Message);
        }

        [AvaloniaFact]
        public void Opening_UsesTheSuppliedHost_WhenNoAncestorIsOne() {
            using var fixture = new KitFixture();
            var host = new PopoverHost { Width = 300, Height = 200 };
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: false);
            status.PopoverHost = host;
            var layout = new Grid { Width = 300, Height = 200 };
            layout.Children.Add(status);
            layout.Children.Add(host);
            var window = new Window { Width = 300, Height = 200, Content = layout };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            status.IsErrorPopoverOpen = true;
            Dispatcher.UIThread.RunJobs();

            PopoverFrame frame = Assert.IsType<PopoverFrame>(Assert.Single(host.Surface.Children));
            Assert.True(frame.IsOpen);
            Assert.Same(status, frame.Anchor);
            window.Close();
        }

        [AvaloniaFact]
        public void ClosingTheWindow_WithThePopoverOpen_DoesNotThrow_AndRemovesIt() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: false);
            (Window window, PopoverHost host) = ShownInHost(status);
            new ControlState(Triangle(status), window).Click();
            PopoverFrame frame = window.GetVisualDescendants().OfType<PopoverFrame>().Single();

            Exception? caught = null;
            try {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            } catch (Exception ex) {
                caught = ex;
            }

            Assert.True(caught is null, caught?.ToString());
            Assert.DoesNotContain(frame, host.Surface.Children);
        }

        [AvaloniaFact]
        public void Pumping_OpenThenClose_SettlesToABoundedLayoutCount() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: true);
            (Window window, _) = ShownInHost(status);
            new ControlState(Triangle(status), window).Click();
            PopoverFrame frame = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            int layouts = 0;
            frame.LayoutUpdated += (_, _) => layouts++;

            AnimationClock.Settle();
            Assert.True(frame.IsOpen);
            status.State = SolveStatusState.Idle;
            AnimationClock.Settle();

            Assert.False(frame.IsOpen);
            Assert.True(layouts <= 40, $"SolveStatus's popover laid out {layouts} times settling one open/close pair.");
        }

        [AvaloniaFact]
        public void Pumping_SteadyState_AddsNoLayoutPasses() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Solving);
            Shown(status);
            AnimationClock.Settle();
            int layouts = 0;
            status.LayoutUpdated += (_, _) => layouts++;

            AnimationClock.Settle(300);

            Assert.True(layouts <= 2, $"A steady spinner re-laid out {layouts} times in 300 ms.");
            Assert.True(status.Bounds.Width > 0);
        }

        [AvaloniaFact]
        public void Spinner_IsAProgressIndicatorNamedSolvingGraph() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Solving);
            Shown(status);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(Spinner(status));
            Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
            Assert.Equal("Solving graph", peer.GetName());
        }

        [AvaloniaFact]
        public void Triangle_IsAButtonNamedSolveFailed_WithHelpText() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            Shown(status);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(Triangle(status));
            Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
            Assert.Equal("Solve failed", peer.GetName());
            Assert.Equal("Shows why the graph couldn't be solved.", peer.GetHelpText());
        }

        [AvaloniaFact]
        public void PeerTree_ExposesOnlyThePartOfTheCurrentState() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus();
            Shown(status);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(status);
            Assert.Empty(peer.GetChildren());

            status.State = SolveStatusState.Solving;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Solving graph"], peer.GetChildren().Select(c => c.GetName()));

            status.State = SolveStatusState.Failed;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Solve failed"], peer.GetChildren().Select(c => c.GetName()));
        }

        [AvaloniaFact]
        public void IsErrorPopoverOpen_SetBeforeAttach_OpensOnAttach_WithABoundedLayoutCount() {
            using var fixture = new KitFixture();
            SolveStatus status = NewStatus(SolveStatusState.Failed);
            status.ErrorContent = ErrorPanel(withShowNode: false);
            status.IsErrorPopoverOpen = true;
            var host = new PopoverHost { Width = 300, Height = 200 };
            var window = new Window { Width = 300, Height = 200, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            host.Surface.Children.Add(status);
            Dispatcher.UIThread.RunJobs();
            PopoverFrame frame = window.GetVisualDescendants().OfType<PopoverFrame>().Single();
            int layouts = 0;
            frame.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle();

            Assert.True(frame.IsOpen);
            Assert.Same(status, frame.Anchor);
            Assert.True(layouts <= 40, $"SolveStatus's popover laid out {layouts} times settling one attach.");

            host.Surface.Children.Remove(status);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.DoesNotContain(frame, host.Surface.GetVisualDescendants());
            Assert.False(status.IsErrorPopoverOpen);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class KitSolveStatusScaleTests {
        [AvaloniaFact]
        public void SlotSpinnerAndGlyph_TrackScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SolveStatus", null, out object? theme));
            var status = new SolveStatus { Theme = (ControlTheme)theme!, State = SolveStatusState.Solving };
            fixture.Window.Content = status;
            Dispatcher.UIThread.RunJobs();

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Panel slot = status.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_Slot");
            SpinnerControl spinner = status.GetVisualDescendants().OfType<SpinnerControl>().Single();
            Assert.Equal(new Size(24, 24), slot.Bounds.Size);
            Assert.Equal(21, spinner.Diameter, 3);
            Assert.Equal(new Size(21, 21), spinner.Bounds.Size);

            status.State = SolveStatusState.Failed;
            Dispatcher.UIThread.RunJobs();
            Viewbox box = status.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_GlyphViewbox");
            Assert.Equal(new Size(24, 24), box.Bounds.Size);
        }
    }
}
