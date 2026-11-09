using Avalonia.Animation.Easings;
using System;

namespace Foreman.Mac.Canvas {
    internal sealed class ProgressTween(Func<TimeSpan> clock, TimeSpan duration, Easing easing) {
        private readonly TimeSpan start = clock();

        public bool Done => clock() - start >= duration;

        public double Progress => easing.Ease(Math.Clamp((clock() - start) / duration, 0, 1));
    }
}
