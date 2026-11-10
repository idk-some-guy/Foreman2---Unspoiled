using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.UiTests.support {
    internal sealed class ManualTimeProvider : TimeProvider {
        private readonly List<ManualTimer> timers = [];
        private TimeSpan now;

        public int ActiveTimers => timers.Count(t => t.IsArmed);

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
            var timer = new ManualTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by) {
            TimeSpan target = now + by;
            while (timers.Where(t => t.IsArmed && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault() is { } next) {
                now = next.Due;
                next.Fire();
            }
            now = target;
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer {
            public bool IsArmed { get; private set; }
            public TimeSpan Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period) {
                IsArmed = dueTime != Timeout.InfiniteTimeSpan;
                Due = owner.now + dueTime;
                return true;
            }

            public void Fire() {
                IsArmed = false;
                callback(state);
            }

            public void Dispose() {
                IsArmed = false;
                owner.timers.Remove(this);
            }

            public ValueTask DisposeAsync() {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
