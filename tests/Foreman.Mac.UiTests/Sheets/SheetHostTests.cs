using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Themes;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Xunit;

namespace Foreman.Mac.UiTests.Sheets {
    public sealed class SheetHostTests {
        private sealed class ProbeSheet : SheetContent<bool?> {
            public readonly Button Ok = new() { Content = "OK" };
            public readonly TextBox Field = new();

            public ProbeSheet() {
                SheetWidth = 348;
                Title = "Probe";
                DefaultButton = Ok;
                Ok.Click += (_, _) => Complete(true);
                Content = new StackPanel { Children = { Field, Ok } };
            }
        }

        private sealed class LargeProbeSheet : SheetContent<bool?> {
            public LargeProbeSheet() {
                SheetWidth = 780;
                SheetHeight = 540;
                Title = "Large";
                Content = new ScrollViewer { Content = new TextBox() };
            }
        }

        private sealed class CountingCommand : ICommand {
            public int ExecuteCount;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => ExecuteCount++;
        }

        private sealed class CommandProbeSheet : SheetContent<bool?> {
            public readonly TextBox Field = new();

            public CommandProbeSheet(Button defaultButton) {
                SheetWidth = 300;
                Title = "CommandProbe";
                DefaultButton = defaultButton;
                Content = new StackPanel { Children = { Field, defaultButton } };
            }
        }

        private static Border ScrimOf(SheetHost host) =>
            host.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("foreman-sheet-scrim"));

        private static Border PanelOf(SheetHost host) =>
            host.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("foreman-sheet-panel"));

        [AvaloniaFact]
        public async Task ShowAsync_OverlaysScrimAndPanelUnderTheTitlebar_AndBlocksTheContentBeneath() {
            var beneath = new Button { Name = "Beneath" };
            var window = new Window { Width = 800, Height = 600, Content = beneath };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();

            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.True(host.IsOpen);
            Assert.Same(sheet, host.Content);
            Border panel = PanelOf(host);
            Assert.Equal(348, panel.Bounds.Width, 0.5);
            Assert.Equal(0, panel.Bounds.Top, 0.5);
            Assert.Equal((800 - 348) / 2.0, panel.Bounds.Left, 1);
            Assert.False(beneath.IsHitTestVisible);

            sheet.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await task);
            Assert.False(host.IsOpen);
            Assert.Null(host.Content);
        }

        [AvaloniaFact]
        public async Task Enter_TriggersTheDefaultButton_AndEscape_Cancels() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var sheet1 = new ProbeSheet();
            Task<bool?> task1 = host.ShowAsync(sheet1);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet1.Field, Key.Enter);
            Assert.True(await task1);
            AnimationClock.Settle();

            var sheet2 = new ProbeSheet();
            Task<bool?> task2 = host.ShowAsync(sheet2);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet2.Field, Key.Escape);
            Assert.Null(await task2);
            Assert.False(host.IsOpen);
        }

        [AvaloniaFact]
        public void Enter_ExecutesCommandOnlyWhenTheClickHandlerLeavesItUnhandled() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var command = new CountingCommand();
            var button = new Button { Command = command };
            int clickCount = 0;
            button.Click += (_, e) => { clickCount++; e.Handled = true; };
            var sheet = new CommandProbeSheet(button);

            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            CanvasKeys.KeyDownOn(sheet.Field, Key.Enter);

            Assert.Equal(1, clickCount);
            Assert.Equal(0, command.ExecuteCount);
        }

        [AvaloniaFact]
        public void FocusIsTrappedInsideTheSheet() {
            var beneath = new Button { Name = "Beneath" };
            var window = new Window { Width = 800, Height = 600, Content = beneath };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();

            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            Assert.True(sheet.Field.IsFocused);

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(sheet.Ok.IsFocused);

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(sheet.Field.IsFocused);
            Assert.False(beneath.IsFocused);
        }

        [AvaloniaFact]
        public void ScrimClickDoesNotDismiss() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();

            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            RealClick.On(ScrimOf(host));

            Assert.True(host.IsOpen);
        }

        [AvaloniaFact]
        public async Task OpenAndCloseTransitionsAreDeclared() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();

            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Border panel = PanelOf(host);
            Border scrim = ScrimOf(host);

            var openOpacity = Assert.Single(panel.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetOpenDuration.TotalMilliseconds, openOpacity.Duration.TotalMilliseconds);
            var openTransform = Assert.Single(panel.Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Motion.SheetOpenDuration.TotalMilliseconds, openTransform.Duration.TotalMilliseconds);
            var scrimOpenOpacity = Assert.Single(scrim.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetOpenDuration.TotalMilliseconds, scrimOpenOpacity.Duration.TotalMilliseconds);

            Assert.Equal(new Thickness(18, 16, 18, 18), panel.Padding);

            Application.Current!.TryGetResource("Foreman.Shadow.Sheet", ThemeVariant.Dark, out object? darkResource);
            BoxShadow darkShadow = Assert.IsType<BoxShadows>(darkResource)[0];
            Assert.Equal(0, darkShadow.OffsetX);
            Assert.Equal(34, darkShadow.OffsetY);
            Assert.Equal(60, darkShadow.Blur);
            Assert.Equal(-12, darkShadow.Spread);
            Assert.Equal(Color.Parse("#A6000000"), darkShadow.Color);

            Application.Current!.TryGetResource("Foreman.Shadow.Sheet", ThemeVariant.Light, out object? lightResource);
            BoxShadow lightShadow = Assert.IsType<BoxShadows>(lightResource)[0];
            Assert.Equal(0, lightShadow.OffsetX);
            Assert.Equal(34, lightShadow.OffsetY);
            Assert.Equal(60, lightShadow.Blur);
            Assert.Equal(-12, lightShadow.Spread);
            Assert.Equal(Color.Parse("#26000000"), lightShadow.Color);

            sheet.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var closeOpacity = Assert.Single(panel.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetCloseDuration.TotalMilliseconds, closeOpacity.Duration.TotalMilliseconds);
            var closeTransform = Assert.Single(panel.Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Motion.SheetCloseDuration.TotalMilliseconds, closeTransform.Duration.TotalMilliseconds);

            AnimationClock.Settle();
            await task;
        }

        [AvaloniaFact]
        public async Task SecondSheetQueuesUntilTheFirstCloses() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var sheet1 = new ProbeSheet();
            Task<bool?> task1 = host.ShowAsync(sheet1);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(sheet1, host.Content);

            var sheet2 = new ProbeSheet();
            Task<bool?> task2 = host.ShowAsync(sheet2);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(sheet1, host.Content);

            sheet1.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await task1);
            AnimationClock.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.True(host.IsOpen);
            Assert.Same(sheet2, host.Content);

            sheet2.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await task2);
        }

        [AvaloniaFact]
        public void LargeSheetClampsToTheOwnerWithMargins() {
            var window = new Window { Width = 700, Height = 500, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new LargeProbeSheet();

            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Border panel = PanelOf(host);
            Assert.True(panel.Bounds.Width <= 676.5, $"expected width <= 676.5, was {panel.Bounds.Width}");
            Assert.True(panel.Bounds.Height <= 476.5, $"expected height <= 476.5, was {panel.Bounds.Height}");
        }

        [AvaloniaFact]
        public void For_OnAGridRootWindow_OverlaysAsASpanningChildInsteadOfWrapping() {
            var grid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
            var top = new Border { Height = 40 };
            var bottom = new Border();
            Grid.SetRow(top, 0);
            Grid.SetRow(bottom, 1);
            grid.Children.Add(top);
            grid.Children.Add(bottom);
            var window = new Window { Width = 800, Height = 600, Content = grid };
            window.Show();

            SheetHost host = SheetHost.For(window);

            Assert.Same(grid, window.Content);
            Assert.Contains(host, grid.Children);
            Assert.Equal(2, Grid.GetRowSpan(host));

            var sheet = new ProbeSheet();
            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Border panel = PanelOf(host);
            Assert.Equal(0, panel.Bounds.Top, 0.5);
        }

        [AvaloniaFact]
        public void For_OnAMainWindow_OverlaysTheRootGridAndShowsASheet() {
            using ShellFixture fixture = ShellFixture.Open();

            SheetHost host = SheetHost.For(fixture.Window);
            Assert.Same(host, SheetHost.For(fixture.Window));

            //MainWindow calls SheetHost.For on itself before WindowScaler.Track wraps Content, so the host
            //lands as a spanning child of the real root Grid rather than the wrap-in-a-Panel fallback.
            var scaler = Assert.IsType<LayoutTransformControl>(fixture.Window.Content);
            var grid = Assert.IsType<Grid>(scaler.Child);
            Assert.Contains(host, grid.Children);

            var sheet = new ProbeSheet();
            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Assert.True(host.IsOpen);
            Assert.Same(sheet, host.Content);
        }
    }
}
