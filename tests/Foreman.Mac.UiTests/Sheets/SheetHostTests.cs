using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProgressBar = Foreman.Mac.Kit.Controls.ProgressBar;
using Xunit;

namespace Foreman.Mac.UiTests.Sheets {
    public sealed class SheetHostTests {
        private sealed class ProbeSheet : SheetContent<bool?> {
            public readonly Button Ok = new() { Content = "OK" };
            public readonly TextBox Field = new();

            public ProbeSheet() {
                WidthClass = SheetWidthClass.Standard;
                Title = "Probe";
                DefaultButton = Ok;
                Ok.Click += (_, _) => Complete(true);
                Content = new StackPanel { Children = { Field, Ok } };
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
                WidthClass = SheetWidthClass.Alert;
                Title = "CommandProbe";
                DefaultButton = defaultButton;
                Content = new StackPanel { Children = { Field, defaultButton } };
            }
        }

        private sealed class RowProbeSheet : SheetContent<bool?> {
            public readonly TextBox Field = new();
            public readonly Button CancelButton;
            public readonly Button ExportButton;
            public int Attempts;

            public RowProbeSheet(string title = "Export an image", SheetWidthClass widthClass = SheetWidthClass.Standard) {
                Title = title;
                WidthClass = widthClass;
                CancelButton = RowButton("Cancel", ButtonRowRole.Cancel);
                ExportButton = RowButton("Export", ButtonRowRole.Default);
                CancelButton.Click += (_, _) => Complete(null);
                ExportButton.Click += (_, _) => Attempts++;
                Buttons = new ButtonRow { Theme = KitTheme("Kit.ButtonRow"), Items = { CancelButton, ExportButton } };
                Content = new StackPanel { Children = { Field } };
            }

            public void Start(string sentence, double? progress = null) => ReportProgress(sentence, progress);
            public void Fail(string sentence) => ReportFailure(sentence);
            public void Settled() => ClearActivity();

            private static Button RowButton(string label, ButtonRowRole role) {
                var button = new Button { Theme = KitTheme("Kit.Button"), Content = label };
                ButtonRow.SetRole(button, role);
                return button;
            }
        }

        private static ControlTheme KitTheme(string key) {
            Assert.True(Application.Current!.TryGetResource(key, null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static double Token(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value));
            return Assert.IsType<double>(value);
        }

        private static SheetFrame FrameOf(SheetHost host, Control content) =>
            host.Children.OfType<SheetFrame>().Single(f => f.GetVisualDescendants().Contains(content));

        private static Border PanelOf(SheetFrame frame) =>
            frame.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

        private static Border PanelOf(SheetHost host) => PanelOf(host.Children.OfType<SheetFrame>().First());

        private static Border PanelOf(SheetHost host, Control content) => PanelOf(FrameOf(host, content));

        private static void AssertScrimmedOver(SheetHost host, Control upper, Control lower) {
            SheetFrame upperFrame = FrameOf(host, upper);
            Assert.True(host.Children.IndexOf(upperFrame) > host.Children.IndexOf(FrameOf(host, lower)));
            Assert.True(upperFrame.IsOpen);
            Scrim scrim = upperFrame.GetVisualDescendants().OfType<Scrim>().Single();
            Assert.True(scrim.Bounds.Contains(PanelOf(host, lower).Bounds));
        }

        private static StatePanel StripOf(SheetHost host) => host.GetVisualDescendants().OfType<StatePanel>().Single();

        private static Grid CompactLayoutOf(StatePanel strip) =>
            strip.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_CompactLayout");

        private static double TopIn(Visual visual, Visual root) => visual.TranslatePoint(new Point(0, 0), root)!.Value.Y;

        [AvaloniaFact]
        public async Task ShowAsync_OverlaysScrimAndPanelUnderTheTitlebar_AndBlocksTheContentBeneath() {
            var beneath = new Button { Name = "Beneath" };
            var window = new Window { Width = 800, Height = 600, Content = beneath };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();

            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilSheetOpen(sheet.Ok);

            Assert.True(host.IsOpen);
            Assert.Same(sheet, host.Content);
            Border panel = PanelOf(host);
            double width = Token("Foreman.Metrics.SheetWidthStandard");
            Assert.Equal(width, panel.Bounds.Width, 0.5);
            Assert.Equal(0, panel.Bounds.Top, 0.5);
            Assert.Equal((800 - width) / 2.0, panel.Bounds.Left, 1);
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
        public async Task SheetHost_DockPanelAndStackPanelRootsAreWrapped_AndAPlainPanelRootIsNot() {
            {
                var top = new Border { Height = 40 };
                DockPanel.SetDock(top, Dock.Top);
                var root = new DockPanel { Children = { top, new Border() } };
                await AssertLayoutPanelRootIsCovered(root);
            }
            {
                var root = new StackPanel { Children = { new Border { Height = 40 }, new Border { Height = 200 } } };
                await AssertLayoutPanelRootIsCovered(root);
            }
            {
                var panel = new Panel { Children = { new Border() } };
                var window = new Window { Width = 800, Height = 600, Content = panel };
                window.Show();

                SheetHost host = SheetHost.For(window);

                Assert.Same(panel, window.Content);
                Assert.Contains(host, panel.Children);
            }
        }

        private static async Task AssertLayoutPanelRootIsCovered(Panel root) {
            var window = new Window { Width = 800, Height = 600, Content = root };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();

            Task<bool?> task = host.ShowAsync(sheet);
            AnimationClock.UntilSheetOpen(sheet.Ok);
            AnimationClock.UntilReflowsSettled(window);

            Point origin = host.TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.Equal(new Rect(window.ClientSize), new Rect(origin, host.Bounds.Size));
            Point centre = sheet.Ok.TranslatePoint(new Point(sheet.Ok.Bounds.Width / 2, sheet.Ok.Bounds.Height / 2), window)!.Value;
            Visual? hit = window.InputHitTest(centre) as Visual;
            Assert.True(hit is not null && (ReferenceEquals(hit, sheet.Ok) || sheet.Ok.IsVisualAncestorOf(hit)), $"hit {hit?.GetType().Name ?? "nothing"}");
            Assert.False(root.IsHitTestVisible);

            SheetFixture.Click(window, sheet.Ok);
            Assert.True(await task);
        }

        [AvaloniaFact]
        public void For_OnAMainWindow_OverlaysTheRootGridAndShowsASheet() {
            using ShellFixture fixture = ShellFixture.Open();

            SheetHost host = SheetHost.For(fixture.Window);
            Assert.Same(host, SheetHost.For(fixture.Window));

            var grid = Assert.IsType<Grid>(fixture.Window.Content);
            Assert.Contains(host, grid.Children);

            var sheet = new ProbeSheet();
            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Assert.True(host.IsOpen);
            Assert.Same(sheet, host.Content);
        }

        [AvaloniaFact]
        public async Task ShowStackedAsync_WithNoSheetOpen_BehavesLikeShowAsync() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();

            Task<bool?> task = host.ShowStackedAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Assert.True(host.IsOpen);
            Assert.Same(sheet, host.Content);

            sheet.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await task);
            Assert.False(host.IsOpen);
        }

        [AvaloniaFact]
        public void FocusLeavingTheSheet_ReturnsToTheControlFocusedBeforeIt_NotTheFirstField() {
            var outside = new Button();
            var window = new Window { Width = 800, Height = 600, Content = outside };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();
            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            sheet.Ok.Focus();
            Assert.True(sheet.Ok.IsFocused);

            outside.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.True(sheet.Ok.IsFocused);
            Assert.False(sheet.Field.IsFocused);
        }

        [AvaloniaFact]
        public void FocusLeavingTheSheet_WhenTheLastFocusedControlIsNowHidden_LandsOnTheFirstFocusable() {
            var outside = new Button();
            var window = new Window { Width = 800, Height = 600, Content = outside };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();
            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            sheet.Ok.Focus();
            Assert.True(sheet.Ok.IsFocused);
            sheet.Ok.IsVisible = false;
            Dispatcher.UIThread.RunJobs();

            outside.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.True(sheet.Field.IsFocused);
            Assert.False(outside.IsFocused);
        }

        [AvaloniaFact]
        public async Task ShowStackedAsync_WithASheetAlreadyOpen_PresentsAboveItAndTrapsFocusOnTop() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var underlying = new ProbeSheet();
            Task<bool?> underlyingTask = host.ShowAsync(underlying);
            Dispatcher.UIThread.RunJobs();
            Assert.True(underlying.Field.IsFocused);

            var alert = new ProbeSheet();
            Task<bool?> alertTask = host.ShowStackedAsync(alert);
            Dispatcher.UIThread.RunJobs();

            Assert.True(host.IsOpen);
            Assert.Same(underlying, host.Content);

            AssertScrimmedOver(host, alert, underlying);
            Assert.True(alert.Field.IsFocused);

            CanvasKeys.KeyDownOn(alert.Field, Key.Escape);

            Assert.Null(await alertTask);
            Assert.False(underlyingTask.IsCompleted);
        }

        [AvaloniaFact]
        public async Task ShowStackedAsync_OnDismiss_RestoresFocusAndUndimsTheUnderlyingSheet() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var underlying = new ProbeSheet();
            Task<bool?> underlyingTask = host.ShowAsync(underlying);
            Dispatcher.UIThread.RunJobs();

            var alert = new ProbeSheet();
            Task<bool?> alertTask = host.ShowStackedAsync(alert);
            Dispatcher.UIThread.RunJobs();
            SheetFrame alertFrame = FrameOf(host, alert);

            CanvasKeys.KeyDownOn(alert.Field, Key.Escape);
            await alertTask;
            AnimationClock.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(alertFrame, host.Children);
            Assert.True(underlying.Field.IsFocused);
            Assert.True(host.IsOpen);
            Assert.False(underlyingTask.IsCompleted);

            underlying.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await underlyingTask);
        }

        [AvaloniaFact]
        public async Task ShowStackedAsync_TwoLevelsDeep_EachLayerAboveTheLastAndUnwindsInOrder() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var baseSheet = new ProbeSheet();
            Task<bool?> baseTask = host.ShowAsync(baseSheet);
            Dispatcher.UIThread.RunJobs();

            var layer1 = new ProbeSheet();
            Task<bool?> layer1Task = host.ShowStackedAsync(layer1);
            Dispatcher.UIThread.RunJobs();

            var layer2 = new ProbeSheet();
            Task<bool?> layer2Task = host.ShowStackedAsync(layer2);
            Dispatcher.UIThread.RunJobs();

            AssertScrimmedOver(host, layer1, baseSheet);
            AssertScrimmedOver(host, layer2, layer1);
            SheetFrame layer1Frame = FrameOf(host, layer1);
            SheetFrame layer2Frame = FrameOf(host, layer2);
            Assert.Same(layer2Frame, host.Children[^1]);
            Assert.True(layer2.Field.IsFocused);

            CanvasKeys.KeyDownOn(layer2.Field, Key.Escape);
            Assert.Null(await layer2Task);
            AnimationClock.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(layer2Frame, host.Children);
            AssertScrimmedOver(host, layer1, baseSheet);
            Assert.True(layer1.Field.IsFocused);
            Assert.False(layer1Task.IsCompleted);
            Assert.False(baseTask.IsCompleted);

            CanvasKeys.KeyDownOn(layer1.Field, Key.Escape);
            Assert.Null(await layer1Task);
            AnimationClock.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(layer1Frame, host.Children);
            Assert.True(baseSheet.Field.IsFocused);
            Assert.False(baseTask.IsCompleted);

            baseSheet.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await baseTask);
        }

        [AvaloniaFact]
        public void Enter_WithAStackedLayerOpen_TriggersTheTopLayersDefaultButton_NotTheUnderlyingOnes() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var underlying = new ProbeSheet();
            _ = host.ShowAsync(underlying);
            Dispatcher.UIThread.RunJobs();

            var alert = new ProbeSheet();
            _ = host.ShowStackedAsync(alert);
            Dispatcher.UIThread.RunJobs();

            CanvasKeys.KeyDownOn(alert.Field, Key.Enter);

            Assert.True(alert.Result.IsCompleted);
            Assert.False(underlying.Result.IsCompleted);
        }

        [AvaloniaFact]
        public async Task For_OnAGridRootWindow_RealClickReachesTheSheetsOwnButton() {
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
            var sheet = new ProbeSheet();
            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilSheetOpen(sheet.Ok);

            RealClick.On(sheet.Ok);

            Assert.True(await task);
        }

        [AvaloniaFact]
        public async Task For_OnAMainWindow_RealClickReachesTheSheetsOwnButton_AndTypingReachesItsTextBox() {
            using ShellFixture fixture = ShellFixture.Open();
            SheetHost host = SheetHost.For(fixture.Window);
            var sheet = new ProbeSheet();

            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            Assert.True(sheet.Field.IsFocused);

            fixture.Window.KeyTextInput("x");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("x", sheet.Field.Text);
            AnimationClock.UntilSheetOpen(sheet.Ok);

            RealClick.On(sheet.Ok);

            Assert.True(await task);
        }

        [AvaloniaFact]
        public void For_OnAMainWindow_PanelSitsFlushBelowTheToolbar_TheToolbarStaysLive_AndTheContentBelowStaysBlocked() {
            using ShellFixture fixture = ShellFixture.Open();
            Dispatcher.UIThread.RunJobs();
            MainWindow window = fixture.Window;
            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");

            SheetHost host = SheetHost.For(window);
            var sheet = new ProbeSheet();
            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Border panel = PanelOf(host);
            Assert.Equal(titleBar.Bounds.Bottom, panel.Bounds.Top, 0.5);
            Assert.True(host.ZIndex > titleBar.ZIndex);
            Assert.Contains(window.GraphCanvas.GetVisualAncestors().OfType<Control>(), a => !a.IsHitTestVisible);

            float scale = window.GraphCanvas.Viewport.ViewScale;
            RealClick.On(window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Zoom In"));
            Assert.True(window.GraphCanvas.Viewport.ViewScale > scale);
            Assert.True(host.IsOpen);
            Assert.False(task.IsCompleted);

            SheetFrame frame = host.GetVisualDescendants().OfType<SheetFrame>().Single();
            IFocusManager focus = TopLevel.GetTopLevel(window)!.FocusManager!;
            var focused = Assert.IsAssignableFrom<Control>(focus.GetFocusedElement());
            Assert.Contains(frame, focused.GetVisualAncestors());
            CanvasKeys.KeyDownOn(focused, Key.Tab);
            Assert.Contains(frame, Assert.IsAssignableFrom<Control>(focus.GetFocusedElement()).GetVisualAncestors());

            RealClick.On(window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Graph Summary"));
            Assert.Same(sheet, host.Content);
            GraphSummaryWindow summary = Assert.IsType<GraphSummaryWindow>(window.CurrentGraphSummaryWindow);

            AnimationClock.UntilSheetOpen(sheet.Ok);
            RealClick.On(sheet.Ok);
            AnimationClock.Settle();
            Assert.True(task.IsCompleted);
            Assert.False(host.IsOpen);
            Assert.True(summary.IsVisible);
            summary.Close();
        }

        [AvaloniaFact]
        public async Task ShowAsync_ForAConfirmQueuedBehindASheet_WhenTheWindowCloses_ResolvesToNo() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            Task<bool?> first = host.ShowAsync(new ProbeSheet());
            Dispatcher.UIThread.RunJobs();
            Task<ConfirmChoice> queued = host.ShowAsync(new AlertSheet("Delete", "Sure?", AlertButtons.Destructive, "Delete"));
            Dispatcher.UIThread.RunJobs();

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ConfirmChoice.No, await queued);
            Assert.Null(await first);
        }

        [AvaloniaFact]
        public async Task ShowStackedAsync_ForAConfirmOnAClosedHost_ResolvesToNo() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            window.Close();
            Dispatcher.UIThread.RunJobs();

            ConfirmChoice choice = await host.ShowStackedAsync(new AlertSheet("Delete", "Sure?", AlertButtons.YesNo));

            Assert.Equal(ConfirmChoice.No, choice);
        }

        [AvaloniaFact]
        public async Task ShowAsync_ForTwoSheetsArrivingWhileTheClosedWindowsLastSheetAnimatesOut_ResolvesBoth() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            Task<bool?> first = host.ShowAsync(new ProbeSheet());
            Dispatcher.UIThread.RunJobs();

            window.Close();
            Task<ConfirmChoice> second = host.ShowAsync(new AlertSheet("Delete", "Sure?", AlertButtons.YesNo));
            Task<ConfirmChoice> third = host.ShowAsync(new AlertSheet("Delete", "Sure?", AlertButtons.YesNo));
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.True(second.IsCompleted);
            Assert.True(third.IsCompleted);
            Assert.Equal(ConfirmChoice.No, await second);
            Assert.Equal(ConfirmChoice.No, await third);
            Assert.Null(await first);
        }

        [AvaloniaFact]
        public async Task ShowAsync_ForASheetAlreadyCancelledOnAClosedHost_ResolvesWithoutCancellingTwice() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            var sheet = new SaveFileLoadSheet();
            sheet.Cancel();

            Foreman.DataCaching.SaveFileLoadOutcome? outcome = await host.ShowAsync(sheet);

            Assert.Equal(Foreman.DataCaching.SaveFileLoadOutcome.Cancel, outcome);
        }

        [AvaloniaFact]
        public async Task ShowStackedAsync_ForAConfirmWhileTheClosingWindowsSheetIsStillOpen_ResolvesToNo() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            Task<bool?> first = host.ShowAsync(new ProbeSheet());
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsOpen);

            window.Close();
            Assert.True(host.IsOpen);
            ConfirmChoice choice = await host.ShowStackedAsync(new AlertSheet("Delete", "Sure?", AlertButtons.YesNo));

            Assert.Equal(ConfirmChoice.No, choice);
            Assert.Null(await first);
        }

        [AvaloniaFact]
        public void Sheet_Headline_RendersFromISheetContentTitle_InSentenceCase() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new RowProbeSheet("Export an image");

            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            SheetFrame frame = FrameOf(host, sheet);
            TextBlock headline = frame.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            Assert.True(headline.IsEffectivelyVisible);
            Assert.Equal("Export an image", headline.Text);
            Assert.Contains("headline", headline.Classes);
            Assert.Equal(Token("Foreman.Type.EmphasizedBody.FontSize"), headline.FontSize);
            Assert.Equal(FontWeight.SemiBold, headline.FontWeight);
            Assert.True(TopIn(headline, frame) < TopIn(sheet.Field, frame));
        }

        [AvaloniaFact]
        public void Sheet_WidthClass_ResolvesTheNamedWidthToken() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new RowProbeSheet(widthClass: SheetWidthClass.Wide);

            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Token("Foreman.Metrics.SheetWidthWide"), PanelOf(host, sheet).Bounds.Width, 0.5);
        }

        [AvaloniaFact]
        public void Sheet_InProgress_DisablesTheDefault_KeepsCancelLive_AndShowsTheStageSentence() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new RowProbeSheet();
            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            sheet.Start("Rendering the graph — 62 %.", 0.62);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilFadedIn(StripOf(host));

            Assert.False(sheet.ExportButton.IsEnabled);
            Assert.True(sheet.CancelButton.IsEffectivelyEnabled);
            StatePanel strip = StripOf(host);
            Assert.Equal(StatePanelShape.Loading, strip.Shape);
            Assert.Equal("Rendering the graph — 62 %.", strip.Sentence);
            Assert.True(strip.IsCompact);
            SheetFrame frame = FrameOf(host, sheet);
            Assert.True(TopIn(strip, frame) >= TopIn(sheet.Field, frame) + sheet.Field.Bounds.Height);
            Assert.True(TopIn(strip, frame) + strip.Bounds.Height <= TopIn(sheet.ExportButton, frame));
            Assert.True(strip.Bounds.Width > 0 && strip.Bounds.Height > 0);
            ProgressBar bar = Assert.Single(strip.GetVisualDescendants().OfType<ProgressBar>(), b => b.IsEffectivelyVisible);
            Assert.Equal(0.62, bar.Value, 3);
            Border sheetPanel = PanelOf(host, sheet);
            double contentWidth = sheetPanel.Bounds.Width - sheetPanel.Padding.Left - sheetPanel.Padding.Right - sheetPanel.BorderThickness.Left - sheetPanel.BorderThickness.Right;
            Assert.Equal(Token("Foreman.Metrics.SheetWidthStandard"), sheetPanel.Bounds.Width, 0.5);
            Assert.Equal(contentWidth, strip.Bounds.Width, 0.5);
            Assert.Equal(strip.Bounds.Width, bar.Bounds.Width, 0.5);
            Assert.DoesNotContain(CompactLayoutOf(strip).GetVisualDescendants(), v => v is Foreman.Mac.Kit.Controls.Spinner);

            int layoutPasses = 0;
            host.LayoutUpdated += (_, _) => layoutPasses++;
            AnimationClock.Settle(300);
            Assert.Equal(0, layoutPasses);

            CanvasKeys.KeyDownOn(sheet.Field, Key.Enter);
            Assert.Equal(0, sheet.Attempts);

            double tallest = strip.Bounds.Height;
            sheet.Start("Rendering — 70 %.", 0.7);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(strip, StripOf(host));
            Assert.True(strip.Bounds.Height >= tallest);

            sheet.Settled();
            Assert.True(sheet.ExportButton.IsEnabled);
            Assert.Empty(host.GetVisualDescendants().OfType<StatePanel>());

            sheet.Start("Rendering the graph — 80 %.");
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(strip, StripOf(host));
            Assert.Contains(CompactLayoutOf(StripOf(host)).GetVisualDescendants(), v => v is Foreman.Mac.Kit.Controls.Spinner { IsEffectivelyVisible: true });
            Assert.DoesNotContain(CompactLayoutOf(StripOf(host)).GetVisualDescendants(), v => v is ProgressBar);
            CanvasKeys.KeyDownOn(sheet.Field, Key.Escape);
            Assert.True(task.IsCompleted);
            Assert.Null(task.Result);
        }

        [AvaloniaFact]
        public void Sheet_FailedAction_StaysOpen_AndShowsTheStatePanelErrorShape() {
            var window = new Window { Width = 800, Height = 600, Content = new Button() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new RowProbeSheet();
            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            const string sentence = "Couldn’t import “Space Age” — the data dump from Factorio stopped partway through.";
            sheet.Fail(sentence);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilFadedIn(StripOf(host));

            Assert.True(host.IsOpen);
            Assert.False(task.IsCompleted);
            Assert.True(sheet.Field.IsEffectivelyVisible);
            StatePanel strip = StripOf(host);
            Assert.Equal(StatePanelShape.Error, strip.Shape);
            Assert.Equal(sentence, strip.Sentence);
            Assert.True(strip.IsCompact);
            Assert.Empty(strip.GetVisualDescendants().OfType<Button>());
            Assert.Single(FrameOf(host, sheet).GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Try Again"));
            TextBlock line = CompactLayoutOf(strip).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == sentence);
            Assert.True(line.TextLayout.TextLines.Count > 1);
            Assert.Equal(TextTrimming.CharacterEllipsis, line.TextTrimming);
            Assert.True(strip.Bounds.Height > line.TextLayout.TextLines[0].Height * 1.5);
            Assert.Equal("Try Again", sheet.ExportButton.Content);
            Assert.True(sheet.ExportButton.IsEnabled);
            Assert.True(sheet.CancelButton.IsEffectivelyEnabled);

            CanvasKeys.KeyDownOn(sheet.Field, Key.Enter);
            Assert.Equal(1, sheet.Attempts);
            Assert.False(task.IsCompleted);

            sheet.Settled();
            Assert.Equal("Export", sheet.ExportButton.Content);
            Assert.Empty(host.GetVisualDescendants().OfType<StatePanel>());
        }
    }
}
