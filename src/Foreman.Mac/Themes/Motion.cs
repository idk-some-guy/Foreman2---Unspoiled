using Avalonia.Animation.Easings;
using System;

namespace Foreman.Mac.Themes {
    //Gate fix 11: every animated duration/easing the palette, the "Add to Graph" browser and the radial
    //menu use traces back here, so retuning one retunes all of them together.
    public static class Motion {
        public static readonly TimeSpan CrossFadeDuration = TimeSpan.FromMilliseconds(220);
        public static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(12);
        public const int StaggerCap = 10;
        public static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(120);
        public static readonly TimeSpan RadialOpenDuration = TimeSpan.FromMilliseconds(160);
        public static readonly TimeSpan RadialCloseDuration = TimeSpan.FromMilliseconds(120);
        public const double RadialOpenScale = 0.9;

        public static Easing CrossFadeEasing => new CubicEaseInOut();
        public static Easing EntranceEasing => new CubicEaseOut();
        public static Easing RadialOpenEasing => new CubicEaseOut();

        //A row/cell beyond StaggerCap enters with the capped (not ever-growing) delay.
        public static TimeSpan StaggerDelay(int index) => StaggerStep * Math.Min(Math.Max(index, 0), StaggerCap);
    }
}
