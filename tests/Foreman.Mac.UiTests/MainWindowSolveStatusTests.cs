using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Models.Solver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class MainWindowSolveStatusTests {
        private const string Sentence = "The solver stopped before it found a result. The rates shown are from the last solve that worked.";

        private static SolveFailure Failure(params int[] ids) => new(ids, SolveFailureReason.Infeasible);

        private static async Task<MainWindow> OpenWithFailingGraphAsync() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenWindowWithGraphAsync();
            ProductionGraph graph = window.GraphCanvas.Viewer.Graph;
            graph.LpSolverFactory = () => new FailingLpSolver();
            foreach (BaseNode node in graph.Nodes)
                node.UpdateState();
            graph.UpdateNodeValues();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static StatePanel PanelOf(SolveStatus status) => Assert.IsType<StatePanel>(status.ErrorContent);

        private static Button[] ButtonsOf(StatePanel panel) => [.. panel.Buttons!.Items.OfType<Button>()];

        [AvaloniaFact]
        public async Task AFailedSolve_FromTheGraph_ReachesBothStatuses_WithTheRuledPanel() {
            MainWindow window = await OpenWithFailingGraphAsync();

            Assert.Equal(SolveStatusState.Failed, window.SolveTracker.State);
            foreach (SolveStatus status in new[] { window.Toolbar.ZoomStepper.SolveStatus, window.TitleBarSolveStatus }) {
                Assert.Equal(SolveStatusState.Failed, status.State);
                StatePanel panel = PanelOf(status);
                Assert.Equal(StatePanelShape.Error, panel.Shape);
                Assert.True(panel.ForceCompact);
                Assert.Equal("Couldn’t solve this graph", panel.Headline);
                Assert.Equal(Sentence, panel.Sentence);
                Assert.Equal(["Show Node", "Try Again"], ButtonsOf(panel).Select(b => (string)b.Content!));
                Assert.Equal("Try Again", (string)ButtonsOf(panel).Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Default).Content!);
            }
            Assert.NotSame(PanelOf(window.Toolbar.ZoomStepper.SolveStatus), PanelOf(window.TitleBarSolveStatus));
        }

        [Fact]
        public void EveryReason_UsesTheFallbackSentence() {
            foreach (SolveFailureReason reason in Enum.GetValues<SolveFailureReason>())
                Assert.Equal(Sentence, Foreman.Mac.Views.SolveErrorPanel.Sentence(reason));
        }

        [AvaloniaFact]
        public async Task ShowNode_OnlyAppearsWhenTheFailureNamesNodes() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();

            window.SolveTracker.OnSolveEnded([Failure()]);

            Assert.Equal(["Try Again"], ButtonsOf(PanelOf(window.Toolbar.ZoomStepper.SolveStatus)).Select(b => (string)b.Content!));
        }

        [AvaloniaFact]
        public async Task ShowNode_SelectsTheWholeFailedGroup_AndClosesThePopover() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();
            var editor = window.GraphCanvas.Viewer.Session.Editor;
            var plate = new ItemQualityPair(window.DataCache!.Items["iron-plate"], window.DataCache.DefaultQuality!);
            NodeId first = editor.CreatePassthroughNode(plate, new System.Drawing.Point(0, 0));
            NodeId second = editor.CreatePassthroughNode(plate, new System.Drawing.Point(400, 250));
            NodeId third = editor.CreatePassthroughNode(plate, new System.Drawing.Point(-900, -600));
            Dispatcher.UIThread.RunJobs();
            Viewport viewport = window.GraphCanvas.Viewport;
            viewport.ViewOffset = new System.Drawing.Point(-20000, -20000);
            viewport.UpdateGraphBounds();
            window.SolveTracker.OnSolveEnded([Failure(first.Value, second.Value)]);
            SolveStatus status = window.Toolbar.ZoomStepper.SolveStatus;
            status.IsErrorPopoverOpen = true;
            Dispatcher.UIThread.RunJobs();
            var elements = window.GraphCanvas.Viewer.NodeElementDictionary.Values;
            System.Drawing.Rectangle group = System.Drawing.Rectangle.Union(
                CanvasTraversal.WorldBounds(elements.Single(e => e.ViewModel.Id == first)),
                CanvasTraversal.WorldBounds(elements.Single(e => e.ViewModel.Id == second)));
            Assert.False(viewport.VisibleGraphBounds.IntersectsWith(group));

            Foreman.Mac.Kit.Motion.ReduceMotion = true;
            try {
                ButtonsOf(PanelOf(status)).Single(b => (string)b.Content! == "Show Node").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            } finally {
                Foreman.Mac.Kit.Motion.ReduceMotion = false;
            }

            int margin = (int)Math.Round(GraphCanvasControl.FindPanMargin / viewport.ViewScale);
            Assert.True(viewport.VisibleGraphBounds.Contains(System.Drawing.Rectangle.Inflate(group, margin, margin)));

            NodeId[] expected = [first, second];
            Assert.Equal(expected, window.GraphCanvas.Viewer.SelectedNodes.Select(n => n.ViewModel.Id).OrderBy(i => i.Value));
            Assert.DoesNotContain(third, window.GraphCanvas.Viewer.SelectedNodes.Select(n => n.ViewModel.Id));
            Assert.False(status.IsErrorPopoverOpen);
        }

        [AvaloniaFact]
        public async Task TryAgain_RerunsTheSolve_AndClosesThePopover() {
            MainWindow window = await OpenWithFailingGraphAsync();
            ProductionGraph graph = window.GraphCanvas.Viewer.Graph;
            int solves = 0;
            graph.SolveStarted += (_, _) => solves++;
            SolveStatus status = window.Toolbar.ZoomStepper.SolveStatus;
            status.IsErrorPopoverOpen = true;
            Dispatcher.UIThread.RunJobs();

            ButtonsOf(PanelOf(status)).Single(b => (string)b.Content! == "Try Again").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, solves);
            Assert.False(status.IsErrorPopoverOpen);
            Assert.Equal(SolveStatusState.Failed, status.State);

            graph.LpSolverFactory = () => new HighsSolver();
            ButtonsOf(PanelOf(status)).Single(b => (string)b.Content! == "Try Again").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(SolveStatusState.Idle, status.State);
            Assert.Null(window.SolveTracker.Failure);
        }

        [AvaloniaFact]
        public async Task EnteringFailed_AnnouncesOnce() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();
            TextBlock announcer = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SearchAnnouncer");
            List<string?> announcements = [];
            announcer.PropertyChanged += (_, e) => {
                if (e.Property == AutomationProperties.NameProperty && e.NewValue is string { Length: > 0 } text)
                    announcements.Add(text);
            };

            window.SolveTracker.OnSolveEnded([Failure(1)]);
            window.SolveTracker.OnSolveEnded([Failure(2)]);
            window.SolveTracker.OnSolveEnded([Failure(2, 3)]);

            Assert.Equal(["Couldn't solve this graph"], announcements);
        }

        private static (ShellFixture Shell, ManualTimeProvider Clock, List<string> Announcements) OpenManual() {
            ShellFixture shell = ShellFixture.Open();
            var clock = new ManualTimeProvider();
            shell.Window.UseSolveTracker(new SolveStatusTracker(clock));
            ManualTaskScheduler.Defer(shell.Graph);
            TextBlock announcer = shell.Window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SearchAnnouncer");
            List<string> announcements = [];
            announcer.PropertyChanged += (_, e) => {
                if (e.Property == AutomationProperties.NameProperty && e.NewValue is string { Length: > 0 } text)
                    announcements.Add(text);
            };
            return (shell, clock, announcements);
        }

        private static BaseNode AddRecipeNode(ShellFixture shell, string recipe, int x) {
            DataCache cache = shell.Window.DataCache!;
            var pair = new RecipeQualityPair(cache.Recipes[recipe], cache.DefaultQuality!);
            BaseNode node = shell.Graph.CreateRecipeNode(pair, new System.Drawing.Point(x, 0));
            node.UpdateState();
            shell.Graph.UpdateNodeValues();
            return node;
        }

        private static void FinishSolve(ShellFixture shell) {
            shell.SolveWorker.RunAll();
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void AnEdit_IsSolvingBeforeTheWorkerRuns_ShowsTheSpinnerAfterTheGrace_AndEndsIdleWhenApplied() {
            (ShellFixture shell, ManualTimeProvider clock, _) = OpenManual();
            using ShellFixture opened = shell;

            AddRecipeNode(shell, "iron-plate", 0);

            Assert.True(shell.Graph.Solver.IsSolving);
            Assert.Equal(SolveStatusState.Idle, shell.Window.TitleBarSolveStatus.State);
            clock.Advance(TimeSpan.FromMilliseconds(400));
            Assert.Equal(SolveStatusState.Solving, shell.Window.SolveTracker.State);
            Assert.Equal(SolveStatusState.Solving, shell.Window.TitleBarSolveStatus.State);

            FinishSolve(shell);

            Assert.False(shell.Graph.Solver.IsSolving);
            Assert.Equal(SolveStatusState.Idle, shell.Window.SolveTracker.State);
            Assert.Equal(SolveStatusState.Idle, shell.Window.TitleBarSolveStatus.State);
        }

        [AvaloniaFact]
        public void CommandPeriod_MidSolve_CancelsThroughTheGraph_AnnouncesOnce_AndALaterRunChangesNothing() {
            (ShellFixture shell, ManualTimeProvider clock, List<string> announcements) = OpenManual();
            using ShellFixture opened = shell;
            BaseNode node = AddRecipeNode(shell, "iron-plate", 0);
            clock.Advance(TimeSpan.FromMilliseconds(400));
            Assert.Equal(SolveStatusState.Solving, shell.Window.SolveTracker.State);
            List<SolveEndKind> ends = [];
            shell.Graph.SolveEnded += (_, e) => ends.Add(e.Kind);

            CanvasKeys.KeyDownOn(shell.Window, Key.OemPeriod, KeyModifiers.Meta);

            Assert.Equal([SolveEndKind.Cancelled], ends);
            Assert.Equal(SolveStatusState.Idle, shell.Window.SolveTracker.State);
            Assert.Equal(["Solve cancelled."], announcements);
            Assert.False(shell.Graph.Solver.IsSolving);
            FinishSolve(shell);
            Assert.Equal([SolveEndKind.Cancelled], ends);
            Assert.Equal(SolveStatusState.Idle, shell.Window.SolveTracker.State);
            Assert.NotEqual(NodeSolveState.Current, node.SolveState);
        }

        [AvaloniaFact]
        public void AnEditMidSolve_EndsSuperseded_ThenApplied_WithTheTrackerSolvingThroughout() {
            (ShellFixture shell, ManualTimeProvider clock, _) = OpenManual();
            using ShellFixture opened = shell;
            AddRecipeNode(shell, "iron-plate", 0);
            clock.Advance(TimeSpan.FromMilliseconds(400));
            List<SolveEndKind> ends = [];
            List<SolveStatusState> states = [];
            shell.Graph.SolveEnded += (_, e) => {
                ends.Add(e.Kind);
                states.Add(shell.Window.SolveTracker.State);
            };

            BaseNode second = AddRecipeNode(shell, "copper-plate", 300);

            Assert.Equal([SolveEndKind.Superseded], ends);
            Assert.Equal([SolveStatusState.Solving], states);
            FinishSolve(shell);

            Assert.Equal([SolveEndKind.Superseded, SolveEndKind.Applied], ends);
            Assert.Equal(SolveStatusState.Idle, shell.Window.SolveTracker.State);
            Assert.Equal(NodeSolveState.Current, second.SolveState);
        }

        [AvaloniaFact]
        public void AFailedEnd_WithNoFailures_ShowsTheFallbackPanelOnBothStatuses() {
            (ShellFixture shell, _, _) = OpenManual();
            using ShellFixture opened = shell;
            List<SolveEndedEventArgs> ends = [];
            shell.Graph.SolveEnded += (_, e) => ends.Add(e);
            shell.Graph.NodeValuesUpdated += (_, _) => throw new InvalidOperationException();

            AddRecipeNode(shell, "iron-plate", 0);
            FinishSolve(shell);

            SolveEndedEventArgs end = Assert.Single(ends);
            Assert.Equal(SolveEndKind.Failed, end.Kind);
            Assert.Empty(end.Failures);
            Assert.Equal(SolveStatusState.Failed, shell.Window.SolveTracker.State);
            Assert.Null(shell.Window.SolveTracker.Failure);
            foreach (SolveStatus status in new[] { shell.Window.Toolbar.ZoomStepper.SolveStatus, shell.Window.TitleBarSolveStatus }) {
                Assert.Equal(SolveStatusState.Failed, status.State);
                StatePanel panel = PanelOf(status);
                Assert.Equal(Foreman.Mac.Views.SolveErrorPanel.Sentence(SolveFailureReason.Unknown), panel.Sentence);
                Assert.Equal(["Try Again"], ButtonsOf(panel).Select(b => (string)b.Content!));
            }
        }

        private static void PauseThroughTheCheckbox(MainWindow window) {
            window.ApplyLoadedSettings();
            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PauseUpdatesCheckbox").IsChecked = true;
        }

        private static void PauseThroughTheMenu(MainWindow window) {
            static IEnumerable<NativeMenuItem> All(NativeMenu menu) {
                foreach (NativeMenuItem item in menu.Items.OfType<NativeMenuItem>()) {
                    yield return item;
                    if (item.Menu is NativeMenu submenu)
                        foreach (NativeMenuItem nested in All(submenu))
                            yield return nested;
                }
            }
            All(NativeMenu.GetMenu(window)!).Single(i => i.Header == "Pause All Calculations").Command!.Execute(null);
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void PausingMidSolve_CancelsItSilently_AndALaterEditLeavesNothingSpinning(bool throughTheMenu) {
            (ShellFixture shell, ManualTimeProvider clock, List<string> announcements) = OpenManual();
            using ShellFixture opened = shell;
            AddRecipeNode(shell, "iron-plate", 0);
            clock.Advance(TimeSpan.FromMilliseconds(400));
            Assert.Equal(SolveStatusState.Solving, shell.Window.SolveTracker.State);

            if (throughTheMenu)
                PauseThroughTheMenu(shell.Window);
            else
                PauseThroughTheCheckbox(shell.Window);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Graph.PauseUpdates);
            AddRecipeNode(shell, "copper-plate", 300);
            FinishSolve(shell);

            Assert.Equal(SolveStatusState.Idle, shell.Window.SolveTracker.State);
            Assert.Equal(SolveStatusState.Idle, shell.Window.TitleBarSolveStatus.State);
            Assert.False(shell.Graph.Solver.IsSolving);
            Assert.Empty(announcements);
        }

        [AvaloniaFact]
        public void TheProductionDefault_AttachesAnAvaloniaContextToTheGraph() {
            var graph = new ProductionGraph();

            UiTestAssemblySetup.ProductionAttachSolveContext(graph);

            Assert.IsType<AvaloniaSynchronizationContext>(graph.SolveContext);
        }
    }
}
