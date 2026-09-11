using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using System;

namespace Foreman.Mac.Kit.Appearance {
    //Accent/contrast/variant come from IPlatformSettings; Reduce Motion/Transparency are polled since Avalonia raises no event for them.
    public sealed class PlatformAppearance : IPlatformAppearance, IDisposable {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly IPlatformSettings settings;
        private readonly DispatcherTimer accessibilityPoll;

        public PlatformAppearance(IPlatformSettings settings) {
            this.settings = settings;
            settings.ColorValuesChanged += OnColorValuesChanged;

            ReduceMotion = MacAccessibilityFlags.ReduceMotion;
            ReduceTransparency = MacAccessibilityFlags.ReduceTransparency;

            accessibilityPoll = new DispatcherTimer { Interval = PollInterval };
            accessibilityPoll.Tick += OnAccessibilityPollTick;
            accessibilityPoll.Start();
        }

        public static PlatformAppearance? TryCreate(Application app) =>
            app.PlatformSettings is IPlatformSettings settings ? new PlatformAppearance(settings) : null;

        public Color? AccentColor => settings.GetColorValues().AccentColor1;
        public ColorContrastPreference ContrastPreference => settings.GetColorValues().ContrastPreference;
        public PlatformThemeVariant ThemeVariant => settings.GetColorValues().ThemeVariant;
        public bool ReduceTransparency { get; private set; }
        public bool ReduceMotion { get; private set; }

        public event EventHandler? Changed;

        private void OnColorValuesChanged(object? sender, PlatformColorValues e) => Changed?.Invoke(this, EventArgs.Empty);

        private void OnAccessibilityPollTick(object? sender, EventArgs e) {
            bool motion = MacAccessibilityFlags.ReduceMotion;
            bool transparency = MacAccessibilityFlags.ReduceTransparency;
            if (motion == ReduceMotion && transparency == ReduceTransparency)
                return;

            ReduceMotion = motion;
            ReduceTransparency = transparency;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() {
            accessibilityPoll.Stop();
            accessibilityPoll.Tick -= OnAccessibilityPollTick;
            settings.ColorValuesChanged -= OnColorValuesChanged;
        }
    }
}
