using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitPopoverFrameTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitPopoverFrameTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PopoverFrame", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PopoverFrame Themed(Control? anchor = null) => new() {
            Theme = KitPopoverFrameTheme(),
            Anchor = anchor,
            Content = new TextBlock { Text = "Show on nodes" },
        };

        private static (Window Window, PopoverHost Host, Button Anchor, PopoverFrame Popover) Shown(double windowWidth = 400, double windowHeight = 300, double anchorX = 150, double anchorY = 100) {
            var anchor = new Button { Width = 80, Height = 22 };
            var host = new PopoverHost { Width = windowWidth, Height = windowHeight };
            Canvas.SetLeft(anchor, anchorX);
            Canvas.SetTop(anchor, anchorY);
            host.Surface.Children.Add(anchor);

            PopoverFrame popover = Themed(anchor);
            host.Surface.Children.Add(popover);

            var window = new Window { Width = windowWidth, Height = windowHeight, Content = host };
            window.Show();
            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            return (window, host, anchor, popover);
        }

        //A tiny fixed-size content (a bare 10x10 Border, not a measured TextBlock) so the panel's DesiredSize
        //is predictable enough to hand-place the anchor for a trailing/leading placement test.
        private static (Window Window, PopoverHost Host, Button Anchor, PopoverFrame Popover) ShownTiny(double windowWidth, double windowHeight, double anchorX, double anchorY) {
            var anchor = new Button { Width = 20, Height = 20 };
            var host = new PopoverHost { Width = windowWidth, Height = windowHeight };
            Canvas.SetLeft(anchor, anchorX);
            Canvas.SetTop(anchor, anchorY);
            host.Surface.Children.Add(anchor);

            PopoverFrame popover = new() { Theme = KitPopoverFrameTheme(), Anchor = anchor, Content = new Border { Width = 10, Height = 10 } };
            host.Surface.Children.Add(popover);

            var window = new Window { Width = windowWidth, Height = windowHeight, Content = host };
            window.Show();
            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            return (window, host, anchor, popover);
        }

        private static Border Panel(PopoverFrame popover) =>
            popover.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

        private static Border ContentClip(PopoverFrame popover) =>
            popover.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_ContentClip");

        private static Border Tail(PopoverFrame popover) =>
            popover.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Tail");

        [AvaloniaFact]
        public void KitPopoverFrame_IsAControlThemeTargetingPopoverFrame() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(PopoverFrame), KitPopoverFrameTheme().TargetType);
        }

        [AvaloniaFact]
        public void Radius_ResolvesToRadiusPopover() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();

            Application.Current!.TryGetResource("Foreman.Radius.Popover", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(radius, Panel(popover).CornerRadius);
        }

        //No PanelWidth set: the panel sizes to its content plus its own padding and border, never a kit-wide default.
        [AvaloniaFact]
        public void PanelWidth_Default_SizesToContent() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = ShownTiny(windowWidth: 400, windowHeight: 300, anchorX: 150, anchorY: 40);

            Assert.Equal(40, Panel(popover).Bounds.Width);
        }

        //A consumer that wants a fixed width (e.g. the View Options popover) sets PanelWidth itself.
        [AvaloniaFact]
        public void PanelWidth_WhenSet_ArrangesThePanelToIt() {
            using var fixture = new KitFixture();
            var anchor = new Button { Width = 80, Height = 22 };
            var host = new PopoverHost { Width = 400, Height = 300 };
            Canvas.SetLeft(anchor, 150);
            Canvas.SetTop(anchor, 100);
            host.Surface.Children.Add(anchor);
            PopoverFrame popover = Themed(anchor);
            popover.PanelWidth = 272;
            host.Surface.Children.Add(popover);
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();
            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(272, Panel(popover).Bounds.Width);
        }

        [AvaloniaFact]
        public void Background_ResolvesToPopoverMaterial() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();

            Assert.Equal(fixture.Brush("Foreman.Color.PopoverMaterial").ToImmutable(), Panel(popover).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void ReduceTransparency_SwapsBackgroundToOpaqueFallback() {
            using var fixture = new KitFixture();
            var hub = new AppearanceHub((Application)Application.Current!, fixture.Appearance);
            fixture.Appearance.ReduceTransparency = true;
            fixture.Appearance.RaiseChanged();

            (_, _, _, PopoverFrame popover) = Shown();

            Assert.Equal(fixture.Brush("Foreman.Color.PopoverMaterialOpaque").ToImmutable(), Panel(popover).Background?.ToImmutable());

            hub.Dispose();
        }

        [AvaloniaFact]
        public void IncreaseContrast_Alone_AlsoSwapsBackgroundToOpaqueFallback() {
            using var fixture = new KitFixture();
            var hub = new AppearanceHub((Application)Application.Current!, fixture.Appearance);
            fixture.Appearance.ContrastPreference = ColorContrastPreference.High;
            fixture.Appearance.RaiseChanged();

            (_, _, _, PopoverFrame popover) = Shown();

            Assert.Equal(fixture.Brush("Foreman.Color.PopoverMaterialOpaque").ToImmutable(), Panel(popover).Background?.ToImmutable());

            hub.Dispose();
        }

        [AvaloniaFact]
        public void Shadow_ResolvesToShadowPopover() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();

            Application.Current!.TryGetResource("Foreman.Shadow.Popover", Application.Current.ActualThemeVariant, out object? shadow);
            Assert.Equal(shadow, Panel(popover).BoxShadow);
        }

        [AvaloniaFact]
        public void ResolvedPlacement_PrefersBelowTheAnchor_WhenItFits() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown(anchorY: 40);

            Assert.Equal(PopoverEdge.Bottom, popover.ResolvedPlacement);
        }

        [AvaloniaFact]
        public void ResolvedPlacement_FallsBackToAbove_WhenBelowDoesNotFit() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown(windowHeight: 260, anchorY: 220);

            Assert.Equal(PopoverEdge.Top, popover.ResolvedPlacement);
        }

        //Content measured only after the frame opened (a body templated on first open) still flips the edge.
        [AvaloniaFact]
        public void ResolvedPlacement_FollowsThePanelsMeasuredSize_WhenTheContentGrowsAfterOpening() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = ShownTiny(windowWidth: 400, windowHeight: 300, anchorX: 150, anchorY: 200);
            Assert.Equal(PopoverEdge.Bottom, popover.ResolvedPlacement);

            ((Border)popover.Content!).Height = 120;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(100);

            Assert.Equal(PopoverEdge.Top, popover.ResolvedPlacement);
        }

        [AvaloniaFact]
        public void ResolvedPlacement_FallsBackToTrailingThenLeading_BeforeClamping() {
            //Anchor hugs the left edge - centering below/above would push the frame off-window on X, so only
            //trailing has room.
            PopoverPlacementResult trailing = PopoverFrame.ResolvePlacement(
                anchor: new Rect(0, 140, 10, 22), window: new Size(600, 300), popover: new Size(160, 120), gap: 6, tailClamp: 26);
            Assert.Equal(PopoverEdge.Right, trailing.Edge);

            //Anchor hugs the right edge - below/above and trailing all run off-window, only leading fits.
            PopoverPlacementResult leading = PopoverFrame.ResolvePlacement(
                anchor: new Rect(590, 140, 10, 22), window: new Size(600, 300), popover: new Size(160, 120), gap: 6, tailClamp: 26);
            Assert.Equal(PopoverEdge.Left, leading.Edge);
        }

        [AvaloniaFact]
        public void ResolvedPlacement_ClampsToTheWindow_WhenNothingFits() {
            PopoverPlacementResult result = PopoverFrame.ResolvePlacement(
                anchor: new Rect(0, 0, 10, 10), window: new Size(120, 90), popover: new Size(160, 120), gap: 6, tailClamp: 26);

            Assert.InRange(result.FramePosition.X, 0, 120);
            Assert.InRange(result.FramePosition.Y, 0, 90);
            Assert.True(result.FramePosition.X + 160 <= 120 || result.FramePosition.X == 0);
        }

        [AvaloniaFact]
        public void TailOffset_ClampsToAtLeast26FromEitherCorner() {
            //Nothing fits (the popover is bigger than the window), so the frame clamps flush to (0,0) and the
            //anchor's own centre - far from the frame's own centre - is what pushes the tail to its clamp.
            PopoverPlacementResult nearLeftCorner = PopoverFrame.ResolvePlacement(
                anchor: new Rect(2, 2, 4, 4), window: new Size(120, 90), popover: new Size(160, 120), gap: 6, tailClamp: 26);
            Assert.Equal(PopoverEdge.Bottom, nearLeftCorner.Edge);
            Assert.Equal(26, nearLeftCorner.TailOffset);

            PopoverPlacementResult nearRightCorner = PopoverFrame.ResolvePlacement(
                anchor: new Rect(196, 2, 4, 4), window: new Size(120, 90), popover: new Size(160, 120), gap: 6, tailClamp: 26);
            Assert.Equal(PopoverEdge.Bottom, nearRightCorner.Edge);
            Assert.Equal(160 - 26, nearRightCorner.TailOffset);
        }

        [AvaloniaFact]
        public void Tail_StrokesOnlyTheTwoEdgesFacingAwayFromTheBody_ForEachResolvedPlacement() {
            using var fixture = new KitFixture();

            void AssertEdge(PopoverFrame popover, PopoverEdge expectedEdge, Thickness expectedThickness, Rect anchorRect, Size windowSize) {
                Assert.Equal(expectedEdge, popover.ResolvedPlacement);
                Border tail = Tail(popover);
                Border panel = Panel(popover);
                Assert.Equal(expectedThickness, tail.BorderThickness);
                Assert.Equal(12, tail.Width);
                Assert.Equal(12, tail.Height);

                PopoverPlacementResult expected = PopoverFrame.ResolvePlacement(anchorRect, windowSize, panel.DesiredSize, 6, 26);
                Point expectedOrigin = expected.RenderOrigin - new Point(6, 6);
                Assert.Equal(expectedOrigin, panel.RenderTransformOrigin.Point);
            }

            //Below: the seam faces down into the panel, so the visible corner points up.
            (_, _, _, PopoverFrame below) = Shown(anchorY: 40);
            AssertEdge(below, PopoverEdge.Bottom, new Thickness(1, 1, 0, 0), new Rect(150, 40, 80, 22), new Size(400, 300));

            //Above: the seam faces up, the visible corner points down.
            (_, _, _, PopoverFrame above) = Shown(windowHeight: 260, anchorY: 220);
            AssertEdge(above, PopoverEdge.Top, new Thickness(0, 0, 1, 1), new Rect(150, 220, 80, 22), new Size(400, 260));

            //Trailing: the seam faces left (toward the anchor), the visible corner points right.
            (_, _, _, PopoverFrame trailing) = ShownTiny(windowWidth: 100, windowHeight: 300, anchorX: 0, anchorY: 140);
            AssertEdge(trailing, PopoverEdge.Right, new Thickness(1, 0, 0, 1), new Rect(0, 140, 20, 20), new Size(100, 300));

            //Leading: the seam faces right, the visible corner points left.
            (_, _, _, PopoverFrame leading) = ShownTiny(windowWidth: 135, windowHeight: 300, anchorX: 110, anchorY: 140);
            AssertEdge(leading, PopoverEdge.Left, new Thickness(0, 1, 1, 0), new Rect(110, 140, 20, 20), new Size(135, 300));
        }

        [AvaloniaFact]
        public void AnchorMoveWithinTheSameEdge_DoesNotReraiseResolvedPlacement_ButTracksTheExactTailOffset() {
            using var fixture = new KitFixture();
            //A window smaller than the popover in every direction forces the nothing-fits fallback (always
            //Bottom, frame.X clamped to 0) for both anchor positions below - only the tail's clamped offset moves.
            var anchor = new Button { Width = 4, Height = 4 };
            var host = new PopoverHost { Width = 120, Height = 90 };
            Canvas.SetLeft(anchor, 2);
            Canvas.SetTop(anchor, 2);
            host.Surface.Children.Add(anchor);
            PopoverFrame popover = Themed(anchor);
            host.Surface.Children.Add(popover);
            var window = new Window { Width = 120, Height = 90, Content = host };
            window.Show();
            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            Border tail = Tail(popover);
            Border panel = Panel(popover);
            Assert.Equal(PopoverEdge.Bottom, popover.ResolvedPlacement);
            Thickness thicknessBefore = tail.BorderThickness;

            int changes = 0;
            popover.PropertyChanged += (_, e) => {
                if (e.Property == PopoverFrame.ResolvedPlacementProperty)
                    changes++;
            };

            Canvas.SetLeft(anchor, 116);
            Dispatcher.UIThread.RunJobs();

            PopoverPlacementResult expected = PopoverFrame.ResolvePlacement(
                new Rect(116, 2, 4, 4), new Size(120, 90), panel.DesiredSize, 6, 26);
            Assert.Equal(PopoverEdge.Bottom, popover.ResolvedPlacement);
            Assert.Equal(0, changes);
            Assert.Equal(new Thickness(expected.TailOffset, 0, 0, 0), tail.Margin);
            Assert.Equal(thicknessBefore, tail.BorderThickness);
        }

        [AvaloniaFact]
        public void Open_TransitionDuration_EqualsMotionPopoverDuration() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();

            Assert.Contains(Panel(popover).Transitions!.OfType<DoubleTransition>(), t => t.Duration == Motion.PopoverDuration);
        }

        [AvaloniaFact]
        public void Open_MovesFocusIntoTheFrame() {
            using var fixture = new KitFixture();
            (Window window, _, _, PopoverFrame popover) = Shown();

            Assert.Same(popover, window.FocusManager?.GetFocusedElement());
        }

        [AvaloniaFact]
        public void Escape_Dismisses_AndReturnsFocusToTheAnchor() {
            using var fixture = new KitFixture();
            (Window window, _, Button anchor, PopoverFrame popover) = Shown();

            int closed = 0;
            popover.Closed += (_, _) => closed++;

            popover.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, closed);
            Assert.False(popover.IsOpen);
            Assert.Same(anchor, window.FocusManager?.GetFocusedElement());
        }

        [AvaloniaFact]
        public void AutomationPeer_IsAPaneCalledPopover_NamedFromTheHostedFormGroupsTitle() {
            using var fixture = new KitFixture();
            var anchor = new Button { Width = 80, Height = 22 };
            var host = new PopoverHost { Width = 400, Height = 300 };
            host.Surface.Children.Add(anchor);
            var group = new FormGroup { Title = "Show on nodes" };
            PopoverFrame popover = new() { Theme = KitPopoverFrameTheme(), Anchor = anchor, Content = group };
            host.Surface.Children.Add(popover);
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();
            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(popover);

            Assert.Equal(AutomationControlType.Pane, peer.GetAutomationControlType());
            Assert.Equal("popover", peer.GetLocalizedControlType());
            Assert.Equal("Show on nodes", peer.GetName());
        }

        [AvaloniaFact]
        public void Pumping_OpenThenClose_SettlesToABoundedRebuildAndFinalOpacity() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();

            Border panel = Panel(popover);
            int layouts = 0;
            popover.LayoutUpdated += (_, _) => layouts++;

            AnimationClock.Settle();
            Assert.Equal(1, panel.Opacity);

            popover.IsOpen = false;
            AnimationClock.Settle();
            Assert.Equal(0, panel.Opacity);

            Assert.True(layouts <= 40, $"PopoverFrame laid out {layouts} times settling one open/close pair.");
        }

        [AvaloniaFact]
        public void Pumping_SteadyStateAfterOpen_RaisesNoFurtherLayout() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();

            AnimationClock.Settle();

            int layouts = 0;
            popover.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, layouts);
        }

        [AvaloniaFact]
        public void CloseThenSettingIsOpenFalse_DoesNotRaiseClosedTwice() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();

            int closed = 0;
            popover.Closed += (_, _) => closed++;

            popover.Close();
            popover.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, closed);
        }

        [AvaloniaFact]
        public void SettingIsOpenFalseDirectly_RaisesClosedOnce_AndReturnsFocusToTheAnchor() {
            using var fixture = new KitFixture();
            (Window window, _, Button anchor, PopoverFrame popover) = Shown();

            int closed = 0;
            popover.Closed += (_, _) => closed++;

            popover.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, closed);
            Assert.Same(anchor, window.FocusManager?.GetFocusedElement());
        }

        [AvaloniaFact]
        public void Open_WithoutAPopoverHostAncestor_Throws() {
            using var fixture = new KitFixture();
            var anchor = new Button { Width = 80, Height = 22 };
            var host = new Canvas { Width = 400, Height = 300 };
            host.Children.Add(anchor);
            PopoverFrame popover = Themed(anchor);
            host.Children.Add(popover);
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();

            Assert.Throws<InvalidOperationException>(() => popover.IsOpen = true);
        }

        [AvaloniaFact]
        public void OutsidePress_ClosesTheOpenFrame_AndRaisesClosed() {
            using var fixture = new KitFixture();
            (Window window, _, _, PopoverFrame popover) = Shown();

            int closed = 0;
            popover.Closed += (_, _) => closed++;

            window.MouseDown(new Point(5, 5), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, closed);
            Assert.False(popover.IsOpen);
        }

        [AvaloniaFact]
        public void OutsidePress_ClosesTheFrame_HidingBothThePanelAndTheTail() {
            using var fixture = new KitFixture();
            (Window window, _, _, PopoverFrame popover) = Shown();
            Border panel = Panel(popover);
            Border tail = Tail(popover);
            AnimationClock.Settle();
            Assert.Equal(1, panel.Opacity);
            Assert.Equal(1, tail.Opacity);

            window.MouseDown(new Point(5, 5), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(0, panel.Opacity);
            Assert.Equal(0, tail.Opacity);
        }

        [AvaloniaFact]
        public void OutsidePress_ThenReopening_RestoresBothThePanelAndTheTail_AcrossRepeatedCycles() {
            using var fixture = new KitFixture();
            (Window window, _, _, PopoverFrame popover) = Shown();
            Border panel = Panel(popover);
            Border tail = Tail(popover);
            AnimationClock.Settle();
            Assert.Equal(1, panel.Opacity);
            Assert.Equal(1, tail.Opacity);

            for (int cycle = 0; cycle < 3; cycle++) {
                window.MouseDown(new Point(5, 5), MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle();
                Assert.False(popover.IsOpen);
                Assert.Equal(0, panel.Opacity);
                Assert.Equal(0, tail.Opacity);

                popover.IsOpen = true;
                AnimationClock.Settle();
                Assert.True(popover.IsOpen);
                Assert.Equal(1, panel.Opacity);
                Assert.Equal(1, tail.Opacity);
                Assert.True(popover.IsHitTestVisible);
            }
        }

        [AvaloniaFact]
        public void RepeatedOpenCloseCycles_EachTimeShowAndHideBothThePanelAndTheTail() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();
            Border panel = Panel(popover);
            Border tail = Tail(popover);
            AnimationClock.Settle();
            Assert.Equal(1, panel.Opacity);
            Assert.Equal(1, tail.Opacity);

            for (int cycle = 0; cycle < 3; cycle++) {
                popover.IsOpen = false;
                AnimationClock.Settle();
                Assert.Equal(0, panel.Opacity);
                Assert.Equal(0, tail.Opacity);

                popover.IsOpen = true;
                AnimationClock.Settle();
                Assert.Equal(1, panel.Opacity);
                Assert.Equal(1, tail.Opacity);
            }
        }

        [AvaloniaFact]
        public void Pumping_RepeatedOpenCloseCycles_StaysBounded() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();
            AnimationClock.Settle();

            int layouts = 0;
            popover.LayoutUpdated += (_, _) => layouts++;

            for (int cycle = 0; cycle < 3; cycle++) {
                popover.IsOpen = false;
                AnimationClock.Settle();
                popover.IsOpen = true;
                AnimationClock.Settle();
            }

            Assert.True(layouts <= 120, $"PopoverFrame laid out {layouts} times across three open/close cycles.");

            layouts = 0;
            AnimationClock.Settle(300);
            Assert.Equal(0, layouts);
        }

        [AvaloniaFact]
        public void OutsidePress_ThatClosesTheFrame_DoesNotReachAControlBeneath() {
            using var fixture = new KitFixture();
            (Window window, PopoverHost host, _, PopoverFrame popover) = Shown();

            int clicked = 0;
            var beneath = new Button { Width = 40, Height = 20 };
            beneath.Click += (_, _) => clicked++;
            Canvas.SetLeft(beneath, 0);
            Canvas.SetTop(beneath, 0);
            host.Surface.Children.Insert(0, beneath);
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(new Point(5, 5), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Point(5, 5), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.IsOpen);
            Assert.Equal(0, clicked);
        }

        [AvaloniaFact]
        public void PressInsideThePanel_DoesNotClose() {
            using var fixture = new KitFixture();
            (Window window, _, _, PopoverFrame popover) = Shown();
            Border panel = Panel(popover);
            Point insidePanel = panel.TranslatePoint(new Point(2, 2), window) ?? default;

            window.MouseDown(insidePanel, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(popover.IsOpen);
        }

        [AvaloniaFact]
        public void Opening_MovesTheFrameToTheTopOfTheHostsSurfaceChildren() {
            using var fixture = new KitFixture();
            var anchorA = new Button { Width = 40, Height = 20 };
            var anchorB = new Button { Width = 40, Height = 20 };
            var host = new PopoverHost { Width = 400, Height = 300 };
            Canvas.SetLeft(anchorA, 10);
            Canvas.SetTop(anchorA, 10);
            Canvas.SetLeft(anchorB, 200);
            Canvas.SetTop(anchorB, 10);
            host.Surface.Children.Add(anchorA);
            host.Surface.Children.Add(anchorB);
            PopoverFrame popoverA = Themed(anchorA);
            PopoverFrame popoverB = Themed(anchorB);
            host.Surface.Children.Add(popoverA);
            host.Surface.Children.Add(popoverB);
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();

            popoverA.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(host.Surface.Children.Count - 1, host.Surface.Children.IndexOf(popoverA));

            popoverA.IsOpen = false;
            popoverB.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(host.Surface.Children.Count - 1, host.Surface.Children.IndexOf(popoverB));
        }

        private static double ScaleOf(Border panel) => Assert.IsAssignableFrom<ITransform>(panel.RenderTransform).Value.M11;

        [AvaloniaFact]
        public void Motion_ClosedPanelSitsAtPopoverOpenScale_AndOpensToOne_OverPopoverDurationWithEntranceEasing() {
            using var fixture = new KitFixture();
            var anchor = new Button { Width = 80, Height = 22 };
            var host = new PopoverHost { Width = 400, Height = 300 };
            host.Surface.Children.Add(anchor);
            PopoverFrame popover = Themed(anchor);
            host.Surface.Children.Add(popover);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            Border panel = Panel(popover);
            Assert.Equal(Motion.PopoverOpenScale, ScaleOf(panel), 3);
            Assert.Equal(panel.RenderTransform!.Value.M11, panel.RenderTransform.Value.M22);

            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.All(panel.Transitions!, t => {
                (TimeSpan duration, Avalonia.Animation.Easings.Easing easing) = t switch {
                    DoubleTransition d => (d.Duration, d.Easing),
                    TransformOperationsTransition o => (o.Duration, o.Easing),
                    _ => throw new Xunit.Sdk.XunitException($"unexpected transition {t}"),
                };
                Assert.Equal(Motion.PopoverDuration, duration);
                Assert.IsType(Motion.EntranceEasing.GetType(), easing);
            });
            AnimationClock.Settle(300);
            Assert.Equal(1, ScaleOf(panel), 3);

            popover.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.All(panel.Transitions!.OfType<DoubleTransition>(), t => Assert.Equal(Motion.PopoverCloseDuration, t.Duration));
            AnimationClock.Settle(300);
            Assert.Equal(Motion.PopoverOpenScale, ScaleOf(panel), 3);
        }

        [AvaloniaFact]
        public void Motion_UnderReduceMotion_ThePanelOnlyFades_AndNeverScales() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            (_, _, _, PopoverFrame popover) = Shown();
            Border panel = Panel(popover);

            DoubleTransition fade = Assert.IsType<DoubleTransition>(Assert.Single(panel.Transitions!));
            Assert.Equal(Visual.OpacityProperty, fade.Property);
            popover.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<DoubleTransition>(Assert.Single(panel.Transitions!));
            AnimationClock.Settle(300);
            Assert.Equal(1, ScaleOf(panel), 3);
        }

        //The radial ring's 0.9 and its easing belong to the ring; the frame's motion never follows radial retuning.
        [Fact]
        public void Motion_TheFrameReferencesNoRadialToken() {
            string dir = AppContext.BaseDirectory;
            while (!System.IO.Directory.Exists(System.IO.Path.Combine(dir, "src", "Foreman.Mac.Kit")))
                dir = System.IO.Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("repo root not found");
            foreach (string file in new[] { "PopoverFrame.cs", "PopoverFrame.axaml" }) {
                string text = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "src", "Foreman.Mac.Kit", "Composites", file));
                Assert.DoesNotContain("Radial", text);
                Assert.DoesNotContain("scale(0.9)", text);
            }
        }

        [AvaloniaFact]
        public void AClosedFrame_IsNoTabStop_AndKeepsItsContentOutOfTabNavigation() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();
            Assert.True(popover.Focusable);
            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(popover));

            popover.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.Focusable);
            Assert.False(KeyboardNavigation.GetIsTabStop(popover));
            Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(popover));

            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popover.Focusable);
            Assert.True(KeyboardNavigation.GetIsTabStop(popover));
            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(popover));
        }

        [AvaloniaFact]
        public void Escape_RaisesCancelledBeforeClosed() {
            using var fixture = new KitFixture();
            (_, _, _, PopoverFrame popover) = Shown();
            var order = new System.Collections.Generic.List<string>();
            popover.Cancelled += (_, _) => order.Add("cancelled");
            popover.Closed += (_, _) => order.Add("closed");

            popover.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["cancelled", "closed"], order);
        }

        private static (Window Window, PopoverHost Host, PopoverFrame Popover) ShownFlush(Point at, double windowWidth = 400, double windowHeight = 300, Control? content = null) {
            var host = new PopoverHost { Width = windowWidth, Height = windowHeight };
            PopoverFrame popover = new() {
                Theme = KitPopoverFrameTheme(),
                Mode = PopoverFrameMode.Flush,
                AnchorPoint = at,
                Content = content ?? new Border { Width = 100, Height = 60 },
            };
            host.Surface.Children.Add(popover);
            var window = new Window { Width = windowWidth, Height = windowHeight, Content = host };
            window.Show();
            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            return (window, host, popover);
        }

        [AvaloniaFact]
        public void Flush_PlacesTheFrameAtItsAnchorPoint_WithoutAnAnchorControl() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30));

            Assert.Null(popover.Anchor);
            Assert.Equal(40, Canvas.GetLeft(popover), 0.5);
            Assert.Equal(30, Canvas.GetTop(popover), 0.5);
        }

        [AvaloniaFact]
        public void Flush_FlipsToTheLeftAndAbove_WhenThePointLeavesNoRoomOnScreen() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(380, 290));
            Size frame = Panel(popover).DesiredSize;

            Assert.Equal(380 - frame.Width, Canvas.GetLeft(popover), 0.5);
            Assert.Equal(290 - frame.Height, Canvas.GetTop(popover), 0.5);
            Assert.True(Canvas.GetLeft(popover) >= 0 && Canvas.GetTop(popover) >= 0);
        }

        [AvaloniaFact]
        public void Flush_DrawsNoTail() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30));

            Assert.False(Tail(popover).IsVisible);
            Assert.Equal(new Thickness(0), Panel(popover).Margin);
        }

        [AvaloniaFact]
        public void Flush_PanelHasZeroPadding_SoTheContentSitsAgainstTheBorder() {
            using var fixture = new KitFixture();
            var content = new Border { Width = 100, Height = 60 };
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30), content: content);
            Border panel = Panel(popover);

            Assert.Equal(new Thickness(0), panel.Padding);
            Point contentOrigin = content.TranslatePoint(new Point(0, 0), panel) ?? new Point(double.NaN, double.NaN);
            Assert.Equal(panel.BorderThickness.Left, contentOrigin.X, 0.5);
            Assert.Equal(panel.BorderThickness.Top, contentOrigin.Y, 0.5);
        }

        [AvaloniaFact]
        public void Flush_PaintsNoMaterialOfItsOwn() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30));

            Assert.Null(Panel(popover).Background);
            Assert.Null(ContentClip(popover).Background);
        }

        [AvaloniaFact]
        public void Flush_LeavesTheShadowBorderUnclipped_AndClipsTheContentAtThePopoverRadius() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30));
            Assert.True(Application.Current!.TryGetResource("Foreman.Radius.Popover", Application.Current.ActualThemeVariant, out object? radius));

            Assert.NotEqual(default, Panel(popover).BoxShadow);
            Assert.False(Panel(popover).ClipToBounds);
            Border clip = ContentClip(popover);
            Assert.True(clip.ClipToBounds);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), clip.CornerRadius);
        }

        [AvaloniaFact]
        public void Flush_DrawsNoBorderOfItsOwn_SoAFrameAroundAPaletteWidthPanelMeasuresThatWidth() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30), content: new Border { Width = 272, Height = 60 });

            Assert.Equal(new Thickness(0), Panel(popover).BorderThickness);
            Assert.Equal(272, Panel(popover).Bounds.Width, 0.5);
        }

        [AvaloniaFact]
        public void Flush_ReturningToAnchoredMode_RestoresPaddingTailAndMaterial() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30));

            popover.Mode = PopoverFrameMode.Anchored;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Thickness(14, 10, 14, 14), Panel(popover).Padding);
            Assert.True(Tail(popover).IsVisible);
            Assert.NotNull(Panel(popover).Background);
            Assert.Equal(new Thickness(1), Panel(popover).BorderThickness);
            Assert.False(ContentClip(popover).ClipToBounds);
        }

        [AvaloniaFact]
        public void Flush_MovingTheAnchorPoint_MovesTheFrame() {
            using var fixture = new KitFixture();
            (_, _, PopoverFrame popover) = ShownFlush(new Point(40, 30));

            popover.AnchorPoint = new Point(120, 90);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(120, Canvas.GetLeft(popover), 0.5);
            Assert.Equal(90, Canvas.GetTop(popover), 0.5);
        }

        [Fact]
        public void ResolvePointPlacement_PrefersBelowTrailing_FlipsEachAxisOnItsOwn_ThenClamps() {
            Size host = new(400, 300);
            Size frame = new(100, 60);

            Assert.Equal(new Point(40, 30), PopoverFrame.ResolvePointPlacement(new Point(40, 30), host, frame));
            Assert.Equal(new Point(280, 30), PopoverFrame.ResolvePointPlacement(new Point(380, 30), host, frame));
            Assert.Equal(new Point(40, 230), PopoverFrame.ResolvePointPlacement(new Point(40, 290), host, frame));
            Assert.Equal(new Point(0, 0), PopoverFrame.ResolvePointPlacement(new Point(50, 40), new Size(80, 50), frame));
        }

        [AvaloniaFact]
        public void Open_LeavesFocusOnContentThatAlreadyHasIt() {
            using var fixture = new KitFixture();
            var field = new TextBox { Width = 100 };
            var host = new PopoverHost { Width = 400, Height = 300 };
            PopoverFrame popover = new() { Theme = KitPopoverFrameTheme(), Mode = PopoverFrameMode.Flush, AnchorPoint = new Point(20, 20), Content = field };
            host.Surface.Children.Add(popover);
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();

            popover.IsOpen = true;
            field.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.Same(field, window.FocusManager?.GetFocusedElement());
        }
    }
}
