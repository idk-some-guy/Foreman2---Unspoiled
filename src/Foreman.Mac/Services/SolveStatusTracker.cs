using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Composites;
using Foreman.Models.Solver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Foreman.Mac.Services {
    internal sealed class SolveStatusTracker(TimeProvider time) {
        private readonly object gate = new();
        private ITimer? graceTimer;
        private int generation;
        private bool inFlight;
        private SolveStatusState state;
        private SolveFailure? failure;

        public SolveStatusState State { get { lock (gate) return state; } }
        public SolveFailure? Failure { get { lock (gate) return failure; } }

        public event EventHandler? Changed;

        public void OnSolveStarted() {
            lock (gate) {
                if (inFlight)
                    return;
                DisarmGrace();
                inFlight = true;
                int armed = ++generation;
                graceTimer = time.CreateTimer(_ => OnGraceElapsed(armed), null, Motion.StatePanelDelay, Timeout.InfiniteTimeSpan);
            }
        }

        public void OnSolveEnded(IReadOnlyList<SolveFailure> failures) {
            bool changed;
            lock (gate) {
                DisarmGrace();
                inFlight = false;
                SolveFailure? next = failures.Count > 0 ? failures[0] : null;
                changed = Apply(next is null ? SolveStatusState.Idle : SolveStatusState.Failed, next);
            }
            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnSolveFailed(IReadOnlyList<SolveFailure> failures) {
            bool changed;
            lock (gate) {
                DisarmGrace();
                inFlight = false;
                changed = Apply(SolveStatusState.Failed, failures.Count > 0 ? failures[0] : null);
            }
            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnSolveCancelled() {
            bool changed;
            lock (gate) {
                DisarmGrace();
                inFlight = false;
                changed = Apply(SolveStatusState.Idle, null);
            }
            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Disarm() {
            lock (gate)
                DisarmGrace();
        }

        private void OnGraceElapsed(int armed) {
            bool changed;
            lock (gate) {
                if (armed != generation)
                    return;
                changed = Apply(SolveStatusState.Solving, failure);
            }
            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        private void DisarmGrace() {
            generation++;
            graceTimer?.Dispose();
            graceTimer = null;
        }

        private bool Apply(SolveStatusState nextState, SolveFailure? nextFailure) {
            if (nextState == state && SameFailure(failure, nextFailure))
                return false;
            state = nextState;
            failure = nextFailure;
            return true;
        }

        private static bool SameFailure(SolveFailure? a, SolveFailure? b) =>
            a is null ? b is null : b is not null && a.Reason == b.Reason && a.NodeIds.SequenceEqual(b.NodeIds);
    }
}
