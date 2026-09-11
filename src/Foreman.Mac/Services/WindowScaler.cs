using Avalonia.Controls;
using Foreman.Mac.Kit.Appearance;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Services {
    //Track handles only each window's own MinWidth/MinHeight, which are not resource keys.
    public static class WindowScaler {
        public const double MinScale = 1.0;
        public const double MaxScale = 2.0;

        private static double currentScale = 1.0;
        private static readonly Dictionary<Window, TrackedMinSize> trackedMinSizes = [];

        public static double CurrentScale => currentScale;

        //App wires this into AppearanceHub.Refresh(), same seam as a theme/accent/contrast change.
        public static event EventHandler? Rescaled;

        //With a 200% endpoint, min size interpolates to it; without one, plain base*step.
        public static void Track(Window window, (double Width, double Height)? maxSize = null) {
            trackedMinSizes[window] = new TrackedMinSize(window.MinWidth, window.MinHeight, maxSize?.Width, maxSize?.Height);
            ApplyMinSize(window, trackedMinSizes[window]);
            window.Closed += OnWindowClosed;
        }

        private readonly record struct TrackedMinSize(double BaseWidth, double BaseHeight, double? MaxWidth, double? MaxHeight);

        private static void ApplyMinSize(Window window, TrackedMinSize entry) {
            window.MinWidth = Interpolate(entry.BaseWidth, entry.MaxWidth);
            window.MinHeight = Interpolate(entry.BaseHeight, entry.MaxHeight);
        }

        private static double Interpolate(double baseValue, double? maxValue) {
            if (maxValue is not double max)
                return baseValue * currentScale;
            double t = (currentScale - MinScale) / (MaxScale - MinScale);
            return baseValue + ((max - baseValue) * t);
        }

        private static void OnWindowClosed(object? sender, EventArgs e) {
            if (sender is not Window window)
                return;
            trackedMinSizes.Remove(window);
            window.Closed -= OnWindowClosed;
        }

        public static void SetScale(double scale) {
            currentScale = Math.Clamp(scale, MinScale, MaxScale);
            ScaleContract.Apply(currentScale);
            foreach ((Window window, TrackedMinSize entry) in trackedMinSizes)
                ApplyMinSize(window, entry);
            Rescaled?.Invoke(null, EventArgs.Empty);
        }
    }
}
