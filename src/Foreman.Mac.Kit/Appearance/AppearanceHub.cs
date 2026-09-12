using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Appearance {
    //Re-pushes platform-driven token overrides into Application resources and notifies Task 5's subscriber list.
    public sealed class AppearanceHub : IDisposable {
        //Set by the app at startup, mirroring Application.Current - lets ThemeAware.Attach reach the one
        //live hub without every call site threading it through.
        public static AppearanceHub? Current { get; set; }

        private readonly Application app;
        private readonly IPlatformAppearance appearance;
        private readonly List<WeakReference<IThemeAware>> subscribers = [];

        public AppearanceHub(Application app, IPlatformAppearance appearance) {
            this.app = app;
            this.appearance = appearance;
            appearance.Changed += OnAppearanceChanged;
            app.ActualThemeVariantChanged += OnAppearanceChanged;
            Refresh();
        }

        public event EventHandler? Changed;

        //Weakly held as a backstop; callers still unsubscribe explicitly on Detached/Closed.
        public void Subscribe(IThemeAware subscriber) => subscribers.Add(new WeakReference<IThemeAware>(subscriber));

        public void Unsubscribe(IThemeAware subscriber) =>
            subscribers.RemoveAll(w => !w.TryGetTarget(out IThemeAware? target) || ReferenceEquals(target, subscriber));

        //Test-only seam: live (non-collected) subscriber count, so a test can assert the list returns to
        //baseline after a window closes instead of poking at the weak-reference list directly.
        public int SubscriberCount => subscribers.Count(w => w.TryGetTarget(out _));

        public void Refresh() {
            bool highContrast = appearance.ContrastPreference == ColorContrastPreference.High;
            bool reduceTransparency = appearance.ReduceTransparency || highContrast;

            ApplyAccent();
            ApplyFocusRing(highContrast);
            ApplyBorderPromotion(highContrast);
            ApplyMaterialFallback(reduceTransparency);
            Motion.ReduceMotion = appearance.ReduceMotion;

            Changed?.Invoke(this, EventArgs.Empty);
            NotifySubscribers();
        }

        private void ApplyAccent() {
            var resources = app.Resources;
            if (appearance.AccentColor is Color accent) {
                byte softAlpha = app.ActualThemeVariant == ThemeVariant.Dark ? (byte)0x29 : (byte)0x1F;
                resources["Foreman.Color.Accent"] = accent;
                resources["Foreman.Color.AccentSoft"] = new Color(softAlpha, accent.R, accent.G, accent.B);
                resources["Foreman.Color.AccentText"] = accent;
            } else {
                resources.Remove("Foreman.Color.Accent");
                resources.Remove("Foreman.Color.AccentSoft");
                resources.Remove("Foreman.Color.AccentText");
            }
        }

        //Hue follows the platform accent when there is one; alpha stays at the token's own declared value
        //(read with any prior override cleared first) except under Increase Contrast, which forces full opacity.
        private void ApplyFocusRing(bool highContrast) {
            var resources = app.Resources;
            resources.Remove("Foreman.Color.FocusRing");

            bool hasAccent = appearance.AccentColor is Color;
            if (hasAccent || highContrast) {
                app.TryGetResource("Foreman.Color.FocusRing", app.ActualThemeVariant, out object? baseValue);
                Color baseColor = baseValue is Color color ? color : default;
                Color hued = appearance.AccentColor is Color accent ? new Color(baseColor.A, accent.R, accent.G, accent.B) : baseColor;
                resources["Foreman.Color.FocusRing"] = highContrast ? new Color(0xFF, hued.R, hued.G, hued.B) : hued;
            }

            if (highContrast)
                resources["Foreman.FocusRing.Width"] = 4.0;
            else
                resources.Remove("Foreman.FocusRing.Width");
        }

        private void ApplyBorderPromotion(bool highContrast) {
            if (highContrast && app.TryGetResource("Foreman.Color.BorderStrong", app.ActualThemeVariant, out object? borderStrong))
                app.Resources["Foreman.Color.Border"] = borderStrong!;
            else
                app.Resources.Remove("Foreman.Color.Border");
        }

        private void ApplyMaterialFallback(bool reduceTransparency) {
            if (reduceTransparency && app.TryGetResource("Foreman.Color.ControlBackground", app.ActualThemeVariant, out object? controlBackground))
                app.Resources["Foreman.Color.PopoverMaterial"] = controlBackground!;
            else
                app.Resources.Remove("Foreman.Color.PopoverMaterial");

            if (reduceTransparency && app.TryGetResource("Foreman.Color.TooltipMaterialOpaque", app.ActualThemeVariant, out object? tooltipOpaque))
                app.Resources["Foreman.Color.TooltipMaterial"] = tooltipOpaque!;
            else
                app.Resources.Remove("Foreman.Color.TooltipMaterial");
        }

        private void NotifySubscribers() {
            for (int i = subscribers.Count - 1; i >= 0; i--) {
                if (subscribers[i].TryGetTarget(out IThemeAware? subscriber))
                    subscriber.RefreshTheme();
                else
                    subscribers.RemoveAt(i);
            }
        }

        private void OnAppearanceChanged(object? sender, EventArgs e) => Refresh();

        public void Dispose() {
            appearance.Changed -= OnAppearanceChanged;
            app.ActualThemeVariantChanged -= OnAppearanceChanged;

            var resources = app.Resources;
            resources.Remove("Foreman.Color.Accent");
            resources.Remove("Foreman.Color.AccentSoft");
            resources.Remove("Foreman.Color.AccentText");
            resources.Remove("Foreman.Color.FocusRing");
            resources.Remove("Foreman.FocusRing.Width");
            resources.Remove("Foreman.Color.Border");
            resources.Remove("Foreman.Color.PopoverMaterial");
            resources.Remove("Foreman.Color.TooltipMaterial");
        }
    }
}
