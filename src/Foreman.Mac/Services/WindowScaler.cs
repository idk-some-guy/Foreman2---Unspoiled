using Foreman.Mac.Kit.Appearance;
using System;

namespace Foreman.Mac.Services {
    public static class WindowScaler {
        public const double MinScale = 1.0;
        public const double MaxScale = 2.0;

        private static double currentScale = 1.0;

        public static double CurrentScale => currentScale;

        public static event EventHandler? Rescaled;

        public static void SetScale(double scale) {
            currentScale = Math.Clamp(scale, MinScale, MaxScale);
            ScaleContract.Apply(currentScale);
            Rescaled?.Invoke(null, EventArgs.Empty);
        }

        public static double Interpolate(double baseValue, double? maxValue) {
            if (maxValue is not double max)
                return baseValue * currentScale;
            double t = (currentScale - MinScale) / (MaxScale - MinScale);
            return baseValue + ((max - baseValue) * t);
        }
    }
}
