using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Services {
    //Owns the UiScale -> every-window LayoutTransform mapping (one call site, same shape as ThemeService's
    //FlagDarkMode -> RequestedThemeVariant). Avalonia has no app-wide "scale everything" switch, so each
    //top-level window wraps its own Content in a LayoutTransformControl; Track does that wrapping once per
    //window (called last in each Window's constructor), and SetScale re-applies to every window still open.
    public static class WindowScaler {
        public const double MinScale = 1.0;
        public const double MaxScale = 2.0;

        private static double currentScale = 1.0;
        private static readonly Dictionary<Window, LayoutTransformControl> tracked = [];
        private static readonly List<LayoutTransformControl> trackedPopupContent = [];

        public static double CurrentScale => currentScale;

        //Task 5: WindowScaler has no DynamicResource of its own to carry a scale change to baked,
        //size-dependent visuals - App wires this into AppearanceHub.Refresh() so subscribers hear about a
        //scale change the same way they hear about a theme/accent/contrast one.
        public static event EventHandler? Rescaled;

        public static void Track(Window window) {
            if (window.Content is not Control content)
                return;

            //Content must be detached before it can become the wrapper's Child - Decorator.Child's setter
            //throws if the control still has a parent (still window.Content at this point).
            window.Content = null;
            var wrapper = new LayoutTransformControl {
                Child = content,
                LayoutTransform = new ScaleTransform(currentScale, currentScale),
            };
            window.Content = wrapper;
            tracked[window] = wrapper;
            window.Closed += OnWindowClosed;
        }

        //I-3 (final fix wave): a Popup's content isn't a visual descendant of Window.Content, so Track's
        //own wrapping never reaches it - MainWindow's View Options card carries its own LayoutTransformControl
        //in XAML and hands it here so SetScale keeps driving it too. Torn down alongside the owning window,
        //same lifetime as Track's own wrapper.
        public static void TrackPopupContent(Window owner, LayoutTransformControl wrapper) {
            wrapper.LayoutTransform = new ScaleTransform(currentScale, currentScale);
            trackedPopupContent.Add(wrapper);
            owner.Closed += (_, _) => trackedPopupContent.Remove(wrapper);
        }

        private static void OnWindowClosed(object? sender, EventArgs e) {
            if (sender is not Window window)
                return;
            tracked.Remove(window);
            window.Closed -= OnWindowClosed;
        }

        public static void SetScale(double scale) {
            currentScale = Math.Clamp(scale, MinScale, MaxScale);
            foreach (LayoutTransformControl wrapper in tracked.Values)
                wrapper.LayoutTransform = new ScaleTransform(currentScale, currentScale);
            foreach (LayoutTransformControl wrapper in trackedPopupContent)
                wrapper.LayoutTransform = new ScaleTransform(currentScale, currentScale);
            Rescaled?.Invoke(null, EventArgs.Empty);
        }
    }
}
