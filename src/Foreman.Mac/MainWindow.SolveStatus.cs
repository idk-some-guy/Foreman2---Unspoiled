using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Models.Nodes;
using Foreman.Models.Solver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac {
    public partial class MainWindow {
        private SolveStatusTracker solveTracker = new(TimeProvider.System);
        private SolveStatus titleBarSolveStatus = null!;
        private RelayCommand showSolveErrorCommand = null!;
        private NativeMenuItem showSolveErrorMenuItem = null!;
        private SolveFailure? builtFailure;
        private bool pauseIsCancelling;
        private bool builtPaused;
        private bool builtNeverSolved;
        private SolveStatusState lastSolveState;

        internal SolveStatusTracker SolveTracker => solveTracker;
        internal SolveStatus TitleBarSolveStatus => titleBarSolveStatus;

        internal void UseSolveTracker(SolveStatusTracker tracker) {
            solveTracker.Changed -= OnSolveTrackerChanged;
            solveTracker.Disarm();
            solveTracker = tracker;
            solveTracker.Changed += OnSolveTrackerChanged;
            ApplySolveStatus();
        }

        private void InitializeSolveStatus() {
            titleBarSolveStatus = this.FindControl<SolveStatus>("TitleBarSolveStatus")!;
            titleBarSolveStatus.PopoverHost = contentPopoverHost;
            toolbar.ZoomStepper.SolveStatus.PopoverHost = contentPopoverHost;
            titleBarSolveStatus.IsVisible = !(showToolbar || searchRevealsToolbar);

            showSolveErrorCommand = new RelayCommand(ShowSolveError, () => solveTracker.State == SolveStatusState.Failed);
            showSolveErrorMenuItem = new NativeMenuItem("Show Solve Error") {
                Command = new DeferredMenuCommand(showSolveErrorCommand),
                IsEnabled = false,
            };

            solveTracker.Changed += OnSolveTrackerChanged;
            GraphCanvas.Viewer.PausedChanged += (_, _) => ApplySolveStatus();
            ProductionGraph graph = GraphCanvas.Viewer.Graph;
            graph.SolveStarted += (_, _) => solveTracker.OnSolveStarted();
            graph.SolveEnded += OnGraphSolveEnded;
            Closed += (_, _) => solveTracker.Disarm();
        }

        private void SetCalculationsPaused(bool paused) {
            ProductionGraph graph = GraphCanvas.Viewer.Graph;
            if (GraphCanvas.Viewer.IsPaused == paused)
                return;
            GraphCanvas.Viewer.IsPaused = paused;
            if (!paused) {
                graph.UpdateNodeValues();
                return;
            }
            pauseIsCancelling = true;
            try {
                graph.Solver.Cancel();
            } finally {
                pauseIsCancelling = false;
            }
        }

        private void OnGraphSolveEnded(object? sender, SolveEndedEventArgs e) {
            switch (e.Kind) {
                case SolveEndKind.Applied:
                    solveTracker.OnSolveEnded(e.Failures);
                    break;
                case SolveEndKind.Failed:
                    solveTracker.OnSolveFailed(e.Failures);
                    break;
                case SolveEndKind.Cancelled:
                    solveTracker.OnSolveCancelled();
                    if (!pauseIsCancelling)
                        Announce("Solve cancelled.");
                    break;
            }
        }

        //The grace timer fires on a pool thread.
        private void OnSolveTrackerChanged(object? sender, EventArgs e) {
            if (Dispatcher.UIThread.CheckAccess())
                ApplySolveStatus();
            else
                Dispatcher.UIThread.Post(ApplySolveStatus);
        }

        private void ApplySolveStatus() {
            SolveStatusState state = solveTracker.State;
            SolveFailure? failure = solveTracker.Failure;
            SolveStatus[] statuses = [toolbar.ZoomStepper.SolveStatus, titleBarSolveStatus];

            bool wantsPanel = failure is not null || state == SolveStatusState.Failed;
            bool paused = GraphCanvas.Viewer.IsPaused;
            bool neverSolved = failure is { NodeIds.Count: > 0 } && failure.NodeIds.All(IsNeverSolved);
            if (!ReferenceEquals(failure, builtFailure) || wantsPanel != (statuses[0].ErrorContent is not null)
                || paused != builtPaused || neverSolved != builtNeverSolved) {
                builtFailure = failure;
                builtPaused = paused;
                builtNeverSolved = neverSolved;
                foreach (SolveStatus status in statuses) {
                    SolveStatus owner = status;
                    owner.ErrorContent = !wantsPanel ? null : SolveErrorPanel.Build(failure,
                        showNode: () => { owner.IsErrorPopoverOpen = false; ShowFailedNodes(); },
                        tryAgain: () => {
                            owner.IsErrorPopoverOpen = false;
                            if (paused)
                                GraphCanvas.ResumeCalculations();
                            else
                                GraphCanvas.Viewer.Graph.UpdateNodeValues();
                        },
                        neverSolved, paused);
                }
            }
            foreach (SolveStatus status in statuses)
                status.State = state;

            showSolveErrorMenuItem.IsEnabled = state == SolveStatusState.Failed;
            showSolveErrorCommand.NotifyCanExecuteChanged();
            if (state == SolveStatusState.Failed && lastSolveState != SolveStatusState.Failed)
                Announce(SolveErrorPanel.Announcement);
            lastSolveState = state;
        }

        private bool IsNeverSolved(int nodeId) =>
            GraphCanvas.Viewer.Graph.Nodes.FirstOrDefault(n => n.NodeID == nodeId)?.SolveState == NodeSolveState.NeverSolved;

        private SolveStatus VisibleSolveStatus => showToolbar || searchRevealsToolbar ? toolbar.ZoomStepper.SolveStatus : titleBarSolveStatus;

        private void ShowSolveError() {
            if (solveTracker.State == SolveStatusState.Failed)
                VisibleSolveStatus.IsErrorPopoverOpen = true;
        }

        private void ShowFailedNodes() {
            if (solveTracker.Failure is not { } failure)
                return;
            RevealNodeIds([.. failure.NodeIds]);
        }

        private void RevealNodeIds(HashSet<int> ids) {
            BaseNodeElement[] elements = [.. GraphCanvas.Viewer.NodeElementDictionary.Values.Where(e => ids.Contains(e.ViewModel.Id.Value))];
            if (elements.Length == 0)
                return;
            System.Drawing.Rectangle bounds = elements.Select(CanvasTraversal.WorldBounds).Aggregate(System.Drawing.Rectangle.Union);
            GraphCanvas.Viewer.SetSelection(elements);
            GraphCanvas.PanIntoView(bounds, GraphCanvasControl.FindPanMargin);
            GraphCanvas.RequestRedraw();
        }
    }
}
