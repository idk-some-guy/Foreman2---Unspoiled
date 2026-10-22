using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Models.Solver;
using System;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.UiTests.Services {
    public class SolveStatusTrackerTests {
        private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(400);

        private static SolveFailure Failure(params int[] ids) => new(ids, SolveFailureReason.Infeasible);

        private static (SolveStatusTracker Tracker, ManualTimeProvider Clock, List<SolveStatusState> States) NewTracker() {
            var clock = new ManualTimeProvider();
            var tracker = new SolveStatusTracker(clock);
            List<SolveStatusState> states = [];
            tracker.Changed += (_, _) => states.Add(tracker.State);
            return (tracker, clock, states);
        }

        [Fact]
        public void Solve300ms_NeverSetsSolving_Solve600ms_SetsAt400_ClearsAt600() {
            (SolveStatusTracker tracker, ManualTimeProvider clock, List<SolveStatusState> states) = NewTracker();

            tracker.OnSolveStarted();
            clock.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Equal(SolveStatusState.Idle, tracker.State);
            tracker.OnSolveEnded([]);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.Equal(SolveStatusState.Idle, tracker.State);
            Assert.DoesNotContain(SolveStatusState.Solving, states);

            tracker.OnSolveStarted();
            clock.Advance(TimeSpan.FromMilliseconds(399));
            Assert.Equal(SolveStatusState.Idle, tracker.State);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(SolveStatusState.Solving, tracker.State);
            clock.Advance(TimeSpan.FromMilliseconds(200));
            Assert.Equal(SolveStatusState.Solving, tracker.State);
            tracker.OnSolveEnded([]);
            Assert.Equal(SolveStatusState.Idle, tracker.State);
            Assert.Equal([SolveStatusState.Solving, SolveStatusState.Idle], states);
        }

        [Fact]
        public void EndedWithFailures_IsFailed_AndKeepsTheFirstFailure() {
            (SolveStatusTracker tracker, _, _) = NewTracker();
            SolveFailure first = Failure(1, 2);

            tracker.OnSolveStarted();
            tracker.OnSolveEnded([first, Failure(7)]);

            Assert.Equal(SolveStatusState.Failed, tracker.State);
            Assert.Same(first, tracker.Failure);
        }

        [Fact]
        public void NextSuccess_ClearsFailed_AndTheFailure() {
            (SolveStatusTracker tracker, _, _) = NewTracker();
            tracker.OnSolveEnded([Failure(1)]);

            tracker.OnSolveStarted();
            tracker.OnSolveEnded([]);

            Assert.Equal(SolveStatusState.Idle, tracker.State);
            Assert.Null(tracker.Failure);
        }

        [Fact]
        public void FailedStaysFailed_WhileANewSolveIsInsideTheGrace() {
            (SolveStatusTracker tracker, ManualTimeProvider clock, _) = NewTracker();
            tracker.OnSolveEnded([Failure(1)]);

            tracker.OnSolveStarted();
            clock.Advance(TimeSpan.FromMilliseconds(100));

            Assert.Equal(SolveStatusState.Failed, tracker.State);
        }

        [Fact]
        public void SlowSolve_ShowsSolvingOverFailed_ThenFailedAgainWhenItFailsAgain() {
            (SolveStatusTracker tracker, ManualTimeProvider clock, _) = NewTracker();
            tracker.OnSolveEnded([Failure(1)]);

            tracker.OnSolveStarted();
            clock.Advance(Grace);
            Assert.Equal(SolveStatusState.Solving, tracker.State);
            tracker.OnSolveEnded([Failure(1)]);

            Assert.Equal(SolveStatusState.Failed, tracker.State);
        }

        [Fact]
        public void Cancelled_ReturnsToIdle_NeverFailed_AndTheLateEndIsIgnored() {
            (SolveStatusTracker tracker, ManualTimeProvider clock, List<SolveStatusState> states) = NewTracker();
            tracker.OnSolveStarted();
            clock.Advance(Grace);
            Assert.Equal(SolveStatusState.Solving, tracker.State);

            tracker.OnSolveCancelled();
            Assert.Equal(SolveStatusState.Idle, tracker.State);
            tracker.OnSolveEnded([Failure(1)]);

            Assert.Equal(SolveStatusState.Idle, tracker.State);
            Assert.Null(tracker.Failure);
            Assert.DoesNotContain(SolveStatusState.Failed, states);
        }

        [Fact]
        public void Cancelled_InsideTheGrace_NeverShowsSolving() {
            (SolveStatusTracker tracker, ManualTimeProvider clock, List<SolveStatusState> states) = NewTracker();
            tracker.OnSolveStarted();
            clock.Advance(TimeSpan.FromMilliseconds(100));

            tracker.OnSolveCancelled();
            clock.Advance(TimeSpan.FromSeconds(1));

            Assert.Equal(SolveStatusState.Idle, tracker.State);
            Assert.DoesNotContain(SolveStatusState.Solving, states);
        }

        [Fact]
        public void Cancelled_ClearsAnEarlierFailure() {
            (SolveStatusTracker tracker, _, _) = NewTracker();
            tracker.OnSolveEnded([Failure(1)]);

            tracker.OnSolveStarted();
            tracker.OnSolveCancelled();

            Assert.Equal(SolveStatusState.Idle, tracker.State);
            Assert.Null(tracker.Failure);
        }

        [Fact]
        public void AnEndAfterACancelledSolve_IsOnlyIgnoredOnce() {
            (SolveStatusTracker tracker, _, _) = NewTracker();
            tracker.OnSolveStarted();
            tracker.OnSolveCancelled();
            tracker.OnSolveEnded([Failure(1)]);

            tracker.OnSolveStarted();
            tracker.OnSolveEnded([Failure(2)]);

            Assert.Equal(SolveStatusState.Failed, tracker.State);
        }

        [Fact]
        public void SecondStartInsideTheGrace_RestartsTheClock() {
            (SolveStatusTracker tracker, ManualTimeProvider clock, _) = NewTracker();
            tracker.OnSolveStarted();
            clock.Advance(TimeSpan.FromMilliseconds(300));
            tracker.OnSolveEnded([]);
            tracker.OnSolveStarted();

            clock.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Equal(SolveStatusState.Idle, tracker.State);
            clock.Advance(TimeSpan.FromMilliseconds(100));

            Assert.Equal(SolveStatusState.Solving, tracker.State);
        }

        [Fact]
        public void Changed_FiresOnlyWhenTheStateOrTheFailureChanges() {
            (SolveStatusTracker tracker, _, List<SolveStatusState> states) = NewTracker();

            tracker.OnSolveStarted();
            tracker.OnSolveEnded([]);
            tracker.OnSolveCancelled();
            Assert.Empty(states);

            tracker.OnSolveEnded([Failure(1)]);
            tracker.OnSolveEnded([Failure(1)]);
            Assert.Single(states);
            tracker.OnSolveEnded([Failure(2)]);
            Assert.Equal(2, states.Count);
        }

        [Fact]
        public void Disarm_CancelsThePendingGrace() {
            (SolveStatusTracker tracker, ManualTimeProvider clock, List<SolveStatusState> states) = NewTracker();
            tracker.OnSolveStarted();
            Assert.Equal(1, clock.ActiveTimers);

            tracker.Disarm();
            clock.Advance(TimeSpan.FromSeconds(1));

            Assert.Equal(0, clock.ActiveTimers);
            Assert.Empty(states);
        }
    }
}
