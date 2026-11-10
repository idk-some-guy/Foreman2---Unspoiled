using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Foreman.Mac.Kit.Composites;
using System;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
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
        public void Surface_StaysAboveChildrenAddedAfterIt() {
            var host = new PopoverHost();
            var content = new Border { Width = 200, Height = 100, Background = Brushes.White };
            host.Children.Add(content);

            Assert.True(host.Surface.ZIndex > content.ZIndex);
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

        //The headless platform never raises window activation events, so this drives Deactivated directly.
        [AvaloniaFact]
        public void TopLevelDeactivating_ClosesTheOpenFrame_AndReturnsFocusToTheAnchor() {
            var anchor = new Button { Width = 80, Height = 22 };
            var host = new PopoverHost();
            host.Surface.Children.Add(anchor);
            var popover = new PopoverFrame { Anchor = anchor, Content = new TextBlock { Text = "probe" } };
            host.Surface.Children.Add(popover);

            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();
            popover.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            RaiseDeactivated(window);

            Assert.False(popover.IsOpen);
            Assert.True(anchor.IsFocused);
        }

        private static void RaiseDeactivated(Window window) {
            for (Type? t = window.GetType(); t is not null; t = t.BaseType) {
                FieldInfo? field = t.GetField("Deactivated", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field is null)
                    continue;
                ((EventHandler?)field.GetValue(window))?.Invoke(window, EventArgs.Empty);
                return;
            }
            throw new InvalidOperationException("Window.Deactivated backing field not found.");
        }
    }
}
