using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Appearance {
    //Re-pushes platform-driven token overrides into Application resources and notifies the subscriber list.
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

        public bool IncreaseContrast => appearance.ContrastPreference == ColorContrastPreference.High;

        public void Refresh() {
            bool highContrast = IncreaseContrast;
            bool reduceTransparency = appearance.ReduceTransparency || highContrast;

            ApplyAccent();
            ApplyFocusRing(highContrast);
            ApplyBorderPromotion(highContrast);
            ApplyMaterialFallback(reduceTransparency);
            ApplyGripIdleOpacity(highContrast);
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
            Promote("Foreman.Color.Border", "Foreman.Color.BorderStrong", highContrast);
            Promote("Foreman.Color.NodeTabBorder", "Foreman.Color.NodeTabBorderContrast", highContrast);
        }

        private void Promote(string key, string sourceKey, bool highContrast) {
            if (highContrast && app.TryGetResource(sourceKey, app.ActualThemeVariant, out object? source))
                app.Resources[key] = source!;
            else
                app.Resources.Remove(key);
        }

        //SplitDivider's grip is opacity-0 at rest and hover-revealed; Increase Contrast keeps it visible at rest.
        private void ApplyGripIdleOpacity(bool highContrast) {
            if (highContrast)
                app.Resources["Foreman.Opacity.SplitGripIdle"] = 1.0;
            else
                app.Resources.Remove("Foreman.Opacity.SplitGripIdle");
        }

        //Every material - popover, tooltip, palette, titlebar - re-points to its own opaque token under Reduce
        //Transparency or Increase Contrast; there is no live blur in this port, so the swap is purely behavioral.
        private void ApplyMaterialFallback(bool reduceTransparency) {
            ApplyMaterial("Foreman.Color.PopoverMaterial", "Foreman.Color.PopoverMaterialOpaque", reduceTransparency);
            ApplyMaterial("Foreman.Color.TooltipMaterial", "Foreman.Color.TooltipMaterialOpaque", reduceTransparency);
            ApplyMaterial("Foreman.Color.PaletteMaterial", "Foreman.Color.PaletteMaterialOpaque", reduceTransparency);
            ApplyMaterial("Foreman.Color.TitlebarMaterial", "Foreman.Color.TitlebarMaterialOpaque", reduceTransparency);
        }

        private void ApplyMaterial(string materialKey, string opaqueKey, bool reduceTransparency) {
            if (reduceTransparency && app.TryGetResource(opaqueKey, app.ActualThemeVariant, out object? opaque))
                app.Resources[materialKey] = opaque!;
            else
                app.Resources.Remove(materialKey);
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
            resources.Remove("Foreman.Color.NodeTabBorder");
            resources.Remove("Foreman.Color.PopoverMaterial");
            resources.Remove("Foreman.Color.TooltipMaterial");
            resources.Remove("Foreman.Color.PaletteMaterial");
            resources.Remove("Foreman.Color.TitlebarMaterial");
            resources.Remove("Foreman.Opacity.SplitGripIdle");
        }
    }
}
