using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Foreman.Mac.Kit.Composites;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    //A host mounted as a content-spanning overlay (MainWindow's row-1 grid) sits on top of real sibling
    //controls in z-order; its own empty background must stay out of hit-testing until a frame is open,
    //without blocking a Surface child (an always-visible trigger like ZoomStepper's readout) either way.
    public sealed class PopoverHostTests {
        private static (Window Window, PopoverHost Host, Border Sibling) Layered() {
            var sibling = new Border { Width = 400, Height = 300, Background = Brushes.Transparent };
            var host = new PopoverHost();
            var grid = new Grid();
            grid.Children.Add(sibling);
            grid.Children.Add(host);

            var window = new Window { Width = 400, Height = 300, Content = grid };
            window.Show();
            return (window, host, sibling);
        }

        [AvaloniaFact]
        public void Constructed_HasNoBackground() {
            var host = new PopoverHost();
            Assert.Null(host.Background);
        }

        [AvaloniaFact]
        public void WithNothingOpen_APressOverTheHostReachesTheSiblingBehindIt() {
            (Window window, _, Border sibling) = Layered();
            int presses = 0;
            sibling.PointerPressed += (_, _) => presses++;

            window.MouseDown(new Point(200, 150), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, presses);
        }

        //A trigger placed permanently in Surface (ZoomStepper's readout, a toolbar anchor) must stay
        //clickable whether or not any popover is currently open.
        [AvaloniaFact]
        public void WithNothingOpen_APressOverASurfaceChildStillReachesIt() {
            (Window window, PopoverHost host, _) = Layered();
            var trigger = new Border { Width = 40, Height = 20, Background = Brushes.Transparent };
            Canvas.SetLeft(trigger, 10);
            Canvas.SetTop(trigger, 10);
            host.Surface.Children.Add(trigger);
            int presses = 0;
            trigger.PointerPressed += (_, _) => presses++;

            window.MouseDown(new Point(20, 20), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, presses);
        }

        [AvaloniaFact]
        public void OpeningAFrame_GivesTheHostABackground_SoAnOutsidePressIsCaught() {
            (Window window, PopoverHost host, _) = Layered();
            var anchor = new Border { Width = 20, Height = 20 };
            host.Surface.Children.Add(anchor);
            var popover = new PopoverFrame { Anchor = anchor, Content = new TextBlock { Text = "probe" } };
            host.Surface.Children.Add(popover);

            popover.IsOpen = true;

            Assert.NotNull(host.Background);
        }

        [AvaloniaFact]
        public void ClosingTheOnlyOpenFrame_RemovesTheHostsBackgroundAgain() {
            (Window window, PopoverHost host, _) = Layered();
            var anchor = new Border { Width = 20, Height = 20 };
            host.Surface.Children.Add(anchor);
            var popover = new PopoverFrame { Anchor = anchor, Content = new TextBlock { Text = "probe" } };
            host.Surface.Children.Add(popover);
            popover.IsOpen = true;

            popover.IsOpen = false;

            Assert.Null(host.Background);
        }

        //A frame removed from Surface while still open (a consumer's Remove, or a direct
        //Surface.Children mutation) must drop the host's hit surface too, not just a later IsOpen flip.
        [AvaloniaFact]
        public void RemovingAnOpenFrameFromSurface_RemovesTheHostsBackground_AndAPressReachesTheSiblingBehind() {
            (Window window, PopoverHost host, Border sibling) = Layered();
            var anchor = new Border { Width = 20, Height = 20 };
            host.Surface.Children.Add(anchor);
            var popover = new PopoverFrame { Anchor = anchor, Content = new TextBlock { Text = "probe" } };
            host.Surface.Children.Add(popover);
            popover.IsOpen = true;
            int presses = 0;
            sibling.PointerPressed += (_, _) => presses++;

            host.Surface.Children.Remove(popover);

            Assert.Null(host.Background);
            window.MouseDown(new Point(200, 150), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, presses);
        }
    }
}
