using Foreman.Mac.Kit.Appearance;
using System;

namespace Foreman.Mac.Services {
    //The single write path for UiScale: pushes the multiplied token set and notifies every window through
    //AppearanceHub. A window's own minimum-size contract (WindowMinSizeScale) reads CurrentScale/Interpolate
    //from here instead of this class tracking each window itself.
    public static class WindowScaler {
        public const double MinScale = 1.0;
        public const double MaxScale = 2.0;

        private static double currentScale = 1.0;

        public static double CurrentScale => currentScale;

        //App wires this into AppearanceHub.Refresh(), same seam as a theme/accent/contrast change.
        public static event EventHandler? Rescaled;

        public static void SetScale(double scale) {
            currentScale = Math.Clamp(scale, MinScale, MaxScale);
            ScaleContract.Apply(currentScale);
            Rescaled?.Invoke(null, EventArgs.Empty);
        }

        //With a 200% endpoint, a min size interpolates to it; without one, plain base*step.
        public static double Interpolate(double baseValue, double? maxValue) {
            if (maxValue is not double max)
                return baseValue * currentScale;
            double t = (currentScale - MinScale) / (MaxScale - MinScale);
            return baseValue + ((max - baseValue) * t);
        }
    }
}
