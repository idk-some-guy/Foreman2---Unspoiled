using Foreman.DataCaching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Models.Solver {
    // Request, Cancel and SolveNow are callable only from the graph's thread (the context thread).
    public sealed class GraphSolveScheduler {
        private sealed class Run(long version, List<SolveGroup> groups, SynchronizationContext context) {
            public long Version { get; } = version;
            public List<SolveGroup> Groups { get; } = groups;
            public SynchronizationContext Context { get; } = context;
            public CancellationTokenSource Cancellation { get; } = new();
        }

        private readonly ProductionGraph graph;
        private Run? current;

        internal GraphSolveScheduler(ProductionGraph graph) {
            this.graph = graph;
        }

        public TaskScheduler Worker { get; set; } = TaskScheduler.Default;

        public bool IsSolving => current is not null;

        public void Request() {
            if (graph.PauseUpdates)
                return;
            if (graph.SolveContext is not { } context) {
                SolveNow();
                return;
            }

            long version = graph.Version;
            List<SolveGroup> groups = [.. graph.DirtyComponents().Select(graph.BuildSolveGroup)];
            Drop(SolveEndKind.Superseded);

            var run = new Run(version, groups, context);
            current = run;
            graph.RaiseSolveStarted();
            _ = Task.Factory.StartNew(() => Execute(run), CancellationToken.None, TaskCreationOptions.DenyChildAttach, Worker);
        }

        public void Cancel() {
            if (current is not { } run)
                return;
            current = null;
            run.Cancellation.Cancel();
            graph.RaiseSolveEnded(SolveEndKind.Cancelled, []);
        }

        public void SolveNow() {
            Drop(SolveEndKind.Superseded);
            graph.RaiseSolveStarted();
            List<SolveFailure> failures = [];
            bool completed = false;
            try {
                foreach (var component in graph.DirtyComponents()) {
                    SolveGroup group = graph.BuildSolveGroup(component);
                    group.Solver.RunLp(CancellationToken.None);
                    group.Solved = true;
                    if (ProductionGraph.ApplyGroupResult(group) is { } failure)
                        failures.Add(failure);
                }
                completed = true;
            } finally {
                graph.LastSolveFailures = failures;
                graph.RaiseSolveEnded(completed && failures.Count == 0 ? SolveEndKind.Applied : SolveEndKind.Failed, failures);
            }
        }

        private void Drop(SolveEndKind kind) {
            if (current is not { } run)
                return;
            current = null;
            run.Cancellation.Cancel();
            graph.RaiseSolveEnded(kind, []);
        }

        private void Execute(Run run) {
            CancellationToken ct = run.Cancellation.Token;
            try {
                foreach (SolveGroup group in run.Groups) {
                    if (ct.IsCancellationRequested)
                        break;
                    group.Solver.RunLp(ct);
                    group.Solved = true;
                }
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Solve worker failed");
            } finally {
                run.Context.Post(_ => Apply(run), null);
            }
        }

        private void Apply(Run run) {
            if (current != run)
                return;
            current = null;

            if (graph.Version != run.Version) {
                graph.RaiseSolveEnded(SolveEndKind.Superseded, []);
                Request();
                return;
            }

            List<SolveFailure> failures = [];
            bool completed = false;
            int applied = 0;
            try {
                foreach (SolveGroup group in run.Groups) {
                    if (ProductionGraph.ApplyGroupResult(group) is { } failure)
                        failures.Add(failure);
                    applied++;
                }
                graph.RaiseNodeValuesUpdated();
                completed = true;
            } catch (OverflowException ex) {
                ErrorLogging.LogException(ex, "OptimizeGraphNodeValues overflow");
                failures.AddRange(MarkUnapplied(run.Groups.Skip(applied)));
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Solve apply failed");
                failures.AddRange(MarkUnapplied(run.Groups.Skip(applied)));
            } finally {
                graph.LastSolveFailures = failures;
                graph.RaiseSolveEnded(completed && failures.Count == 0 ? SolveEndKind.Applied : SolveEndKind.Failed, failures);
            }
        }

        private static List<SolveFailure> MarkUnapplied(IEnumerable<SolveGroup> groups) {
            List<SolveFailure> failures = [];
            foreach (SolveGroup group in groups) {
                foreach (BaseNode node in group.Nodes) {
                    try {
                        node.MarkSolveStale();
                    } catch (Exception ex) {
                        ErrorLogging.LogException(ex, "Solve apply failed");
                    }
                }
                failures.Add(new SolveFailure([.. group.Nodes.Select(n => n.NodeID)], SolveFailureReason.Unknown));
            }
            return failures;
        }
    }
}
