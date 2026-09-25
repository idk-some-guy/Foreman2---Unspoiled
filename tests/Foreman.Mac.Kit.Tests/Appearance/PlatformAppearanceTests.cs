using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Foreman.Mac.Kit.Appearance;
using System;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Appearance {
    public class PlatformAppearanceTests {
        private sealed class FakeAppearance : IPlatformAppearance {
            private EventHandler? changed;

            public Color? AccentColor { get; set; }
            public ColorContrastPreference ContrastPreference { get; set; } = ColorContrastPreference.NoPreference;
            public bool ReduceTransparency { get; set; }
            public bool ReduceMotion { get; set; }
            public PlatformThemeVariant ThemeVariant { get; set; } = PlatformThemeVariant.Light;

            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }

            public void Raise() => changed?.Invoke(this, EventArgs.Empty);
        }

        private static void MergeKitIntoApplication() {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };
            Application.Current!.Resources.MergedDictionaries.Add(kit);
        }

        [AvaloniaFact]
        public void AccentChange_UpdatesAccentResource_AndDynamicResourceConsumerFollows() {
            MergeKitIntoApplication();
            var appearance = new FakeAppearance();
            _ = new AppearanceHub((Application)Application.Current!, appearance);
            var border = new Border();
            border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Foreman.Color.Accent"));
            var window = new Window { Content = border };
            window.Show();

            var newAccent = Color.Parse("#FF00FF");
            appearance.AccentColor = newAccent;
            appearance.Raise();

            Application.Current!.TryGetResource("Foreman.Color.Accent", Application.Current.ActualThemeVariant, out object? resolved);
            Assert.Equal(newAccent, Assert.IsType<Color>(resolved));
            Assert.Equal(newAccent, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
        }

        [AvaloniaFact]
        public void ContrastHigh_WidensFocusRing_ForcesFullOpacity_AndPromotesBorderToBorderStrong() {
            MergeKitIntoApplication();
            var appearance = new FakeAppearance();
            var hub = new AppearanceHub((Application)Application.Current!, appearance);

            appearance.AccentColor = Color.Parse("#123456");
            appearance.ContrastPreference = ColorContrastPreference.High;
            appearance.Raise();

            Application.Current!.TryGetResource("Foreman.FocusRing.Width", Application.Current.ActualThemeVariant, out object? width);
            Assert.Equal(4.0, Assert.IsType<double>(width));

            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            Assert.Equal(2.0, Assert.IsType<double>(offset));

            Application.Current!.TryGetResource("Foreman.Color.FocusRing", Application.Current.ActualThemeVariant, out object? ring);
            Assert.Equal((byte)0xFF, Assert.IsType<Color>(ring).A);

            Application.Current!.TryGetResource("Foreman.Color.BorderStrong", Application.Current.ActualThemeVariant, out object? borderStrong);
            Application.Current!.TryGetResource("Foreman.Color.Border", Application.Current.ActualThemeVariant, out object? border);
            Assert.Equal(borderStrong, border);

            hub.Dispose();
        }

        //v2 §01 ties full ring opacity to Increase Contrast specifically - an accent-driven default keeps
        //the FocusRing token's own declared alpha rather than always forcing it to full.
        [AvaloniaFact]
        public void AccentPresent_DefaultContrast_FocusRingKeepsTokensDeclaredOpacity() {
            MergeKitIntoApplication();
            Application.Current!.TryGetResource("Foreman.Color.FocusRing", Application.Current.ActualThemeVariant, out object? declared);
            byte declaredAlpha = Assert.IsType<Color>(declared).A;

            var appearance = new FakeAppearance();
            var hub = new AppearanceHub((Application)Application.Current!, appearance);

            var newAccent = Color.Parse("#FF00FF");
            appearance.AccentColor = newAccent;
            appearance.Raise();

            Application.Current!.TryGetResource("Foreman.Color.FocusRing", Application.Current.ActualThemeVariant, out object? resolved);
            Color ring = Assert.IsType<Color>(resolved);
            Assert.Equal(declaredAlpha, ring.A);
            Assert.Equal((newAccent.R, newAccent.G, newAccent.B), (ring.R, ring.G, ring.B));

            hub.Dispose();
        }

        //An explicit Light/Dark flip (ThemeService.Apply) never fires IPlatformAppearance.Changed, so
        //variant-resolved overrides must re-derive off the hub's own ActualThemeVariantChanged
        //subscription instead of relying on caller discipline.
        [AvaloniaFact]
        public void ThemeFlip_WithContrastHigh_ReResolvesBorderToTheNewVariantsBorderStrong() {
            MergeKitIntoApplication();
            var appearance = new FakeAppearance { ContrastPreference = ColorContrastPreference.High };
            var hub = new AppearanceHub((Application)Application.Current!, appearance);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Application.Current!.TryGetResource("Foreman.Color.BorderStrong", ThemeVariant.Light, out object? lightBorderStrong);
            Application.Current!.TryGetResource("Foreman.Color.Border", Application.Current.ActualThemeVariant, out object? borderInLight);
            Assert.Equal(lightBorderStrong, borderInLight);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Application.Current!.TryGetResource("Foreman.Color.BorderStrong", ThemeVariant.Dark, out object? darkBorderStrong);
            Application.Current!.TryGetResource("Foreman.Color.Border", Application.Current.ActualThemeVariant, out object? borderInDark);
            Assert.Equal(darkBorderStrong, borderInDark);
            Assert.NotEqual(lightBorderStrong, darkBorderStrong);

            hub.Dispose();
        }

        [AvaloniaFact]
        public void ContrastHigh_PromotesNodeTabBorderContrastOntoNodeTabBorder_AndRestoresItWhenContrastDrops() {
            MergeKitIntoApplication();
            var appearance = new FakeAppearance();
            var hub = new AppearanceHub((Application)Application.Current!, appearance);
            try {
                Application.Current!.TryGetResource("Foreman.Color.NodeTabBorder", Application.Current.ActualThemeVariant, out object? declared);
                Application.Current!.TryGetResource("Foreman.Color.NodeTabBorderContrast", Application.Current.ActualThemeVariant, out object? contrast);
                Assert.False(hub.IncreaseContrast);

                appearance.ContrastPreference = ColorContrastPreference.High;
                appearance.Raise();

                Application.Current!.TryGetResource("Foreman.Color.NodeTabBorder", Application.Current.ActualThemeVariant, out object? promoted);
                Assert.Equal(Assert.IsType<Color>(contrast), Assert.IsType<Color>(promoted));
                Assert.True(hub.IncreaseContrast);

                appearance.ContrastPreference = ColorContrastPreference.NoPreference;
                appearance.Raise();

                Application.Current!.TryGetResource("Foreman.Color.NodeTabBorder", Application.Current.ActualThemeVariant, out object? restored);
                Assert.Equal(Assert.IsType<Color>(declared), Assert.IsType<Color>(restored));
                Assert.False(hub.IncreaseContrast);
            } finally {
                hub.Dispose();
            }
        }

        [AvaloniaFact]
        public void ReduceTransparency_PopoverMaterialResolvesToOpaqueValue() {
            MergeKitIntoApplication();
            var appearance = new FakeAppearance();
            var hub = new AppearanceHub((Application)Application.Current!, appearance);

            appearance.ReduceTransparency = true;
            appearance.Raise();

            Application.Current!.TryGetResource("Foreman.Color.ControlBackground", Application.Current.ActualThemeVariant, out object? controlBackground);
            Application.Current!.TryGetResource("Foreman.Color.PopoverMaterial", Application.Current.ActualThemeVariant, out object? popoverMaterial);
            Assert.Equal(controlBackground, popoverMaterial);

            hub.Dispose();
        }

        [AvaloniaFact]
        public void ReduceTransparency_TooltipMaterialResolvesToOpaqueValue() {
            MergeKitIntoApplication();
            var appearance = new FakeAppearance();
            var hub = new AppearanceHub((Application)Application.Current!, appearance);

            appearance.ReduceTransparency = true;
            appearance.Raise();

            Assert.True(Application.Current!.Resources.ContainsKey("Foreman.Color.TooltipMaterial"));

            appearance.ReduceTransparency = false;
            appearance.Raise();

            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.TooltipMaterial"));

            hub.Dispose();
        }

        [AvaloniaFact]
        public void Dispose_RemovesEveryResourceOverrideItOwns() {
            MergeKitIntoApplication();
            var appearance = new FakeAppearance {
                AccentColor = Color.Parse("#123456"),
                ContrastPreference = ColorContrastPreference.High,
                ReduceTransparency = true,
            };
            var hub = new AppearanceHub((Application)Application.Current!, appearance);

            hub.Dispose();

            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.Accent"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.AccentSoft"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.AccentText"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.FocusRing"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.FocusRing.Width"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.Border"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.NodeTabBorder"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.PopoverMaterial"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.TooltipMaterial"));
        }

        [AvaloniaFact]
        public void ReduceMotion_SetsMotionFlag() {
            var appearance = new FakeAppearance();
            var hub = new AppearanceHub((Application)Application.Current!, appearance);

            try {
                appearance.ReduceMotion = true;
                appearance.Raise();

                Assert.True(Motion.ReduceMotion);
            } finally {
                Motion.ReduceMotion = false;
                hub.Dispose();
            }
        }
    }
}
