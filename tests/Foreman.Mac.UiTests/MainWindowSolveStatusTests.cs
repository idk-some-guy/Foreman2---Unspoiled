using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
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
    }
}
