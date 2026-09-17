using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    public class TokenTests {
        private static IEnumerable<object> KeysFor(ThemeVariant variant) {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };
            var provider = (kit.Loaded as IResourceDictionary)!.ThemeDictionaries[variant];
            var dictionary = provider switch {
                ResourceInclude include => include.Loaded,
                IResourceDictionary loaded => loaded,
                _ => throw new InvalidOperationException($"unsupported theme provider type {provider.GetType()}"),
            };
            return dictionary.Keys;
        }

        [AvaloniaFact]
        public void ThemeDictionaries_LightAndDark_DeclareIdenticalKeySets() {
            var lightKeys = KeysFor(ThemeVariant.Light).Cast<string>().ToHashSet();
            var darkKeys = KeysFor(ThemeVariant.Dark).Cast<string>().ToHashSet();

            var onlyInLight = lightKeys.Except(darkKeys).ToArray();
            var onlyInDark = darkKeys.Except(lightKeys).ToArray();

            Assert.Empty(onlyInLight);
            Assert.Empty(onlyInDark);
            Assert.True(lightKeys.Count >= 30, $"expected a substantial token set, found {lightKeys.Count}");

            foreach (var key in lightKeys) {
                Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out _), $"{key} did not resolve for Light in the running app");
                Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Dark, out _), $"{key} did not resolve for Dark in the running app");
            }
        }

        [AvaloniaFact]
        public void DarkTheme_CanvasBackground_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.CanvasBackground", ThemeVariant.Dark, out var value);

            Assert.Equal(Color.Parse("#1E1E20"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void DarkTheme_Accent_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.Accent", ThemeVariant.Dark, out var value);

            Assert.Equal(Color.Parse("#0A84FF"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_CanvasBackground_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.CanvasBackground", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#FAFAFA"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_Accent_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.Accent", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#007AFF"), Assert.IsType<Color>(value));
        }

        //NodeSelected replaces the retired Selection key.
        [AvaloniaFact]
        public void DarkTheme_NodeSelected_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.NodeSelected", ThemeVariant.Dark, out var value);

            Assert.Equal(Color.Parse("#FF9F0A"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_NodeSelected_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.NodeSelected", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#D97A06"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void DarkTheme_Warning_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.Warning", ThemeVariant.Dark, out var value);

            Assert.Equal(Color.Parse("#FF9F0A"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_Warning_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.Warning", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#B25000"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void DarkTheme_FocusRing_MatchesAccent() {
            Application.Current!.TryGetResource("Foreman.Color.FocusRing", ThemeVariant.Dark, out var focusRing);
            Application.Current!.TryGetResource("Foreman.Color.Accent", ThemeVariant.Dark, out var accent);

            Assert.Equal(accent, focusRing);
        }

        [AvaloniaFact]
        public void LightTheme_FocusRing_MatchesAccent() {
            Application.Current!.TryGetResource("Foreman.Color.FocusRing", ThemeVariant.Light, out var focusRing);
            Application.Current!.TryGetResource("Foreman.Color.Accent", ThemeVariant.Light, out var accent);

            Assert.Equal(accent, focusRing);
        }

        [AvaloniaFact]
        public void DarkTheme_TextOnAccent_IsWhite() {
            Application.Current!.TryGetResource("Foreman.Color.TextOnAccent", ThemeVariant.Dark, out var value);

            Assert.Equal(Colors.White, Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_TextOnAccent_IsWhite() {
            Application.Current!.TryGetResource("Foreman.Color.TextOnAccent", ThemeVariant.Light, out var value);

            Assert.Equal(Colors.White, Assert.IsType<Color>(value));
        }

        //the kit mock's 'pressed darker than hover' rule wins over the contract table's #525256
        [AvaloniaFact]
        public void DarkTheme_ControlBackgroundPressed_IsDarkerThanHoverPer12PercentRule() {
            Application.Current!.TryGetResource("Foreman.Color.ControlBackgroundPressed", ThemeVariant.Dark, out var value);

            Assert.Equal(Color.Parse("#3F3F43"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_ControlBackgroundPressed_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.ControlBackgroundPressed", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#E6E6E8"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void DarkTheme_SelectionInactive_MatchesContractAlpha() {
            Application.Current!.TryGetResource("Foreman.Color.SelectionInactive", ThemeVariant.Dark, out var value);

            Assert.Equal(Color.Parse("#21FFFFFF"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_SelectionInactive_MatchesContractAlpha() {
            Application.Current!.TryGetResource("Foreman.Color.SelectionInactive", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#17000000"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void Success_ExistsInBothVariants() {
            Application.Current!.TryGetResource("Foreman.Color.Success", ThemeVariant.Dark, out var dark);
            Application.Current!.TryGetResource("Foreman.Color.Success", ThemeVariant.Light, out var light);

            Assert.IsType<Color>(dark);
            Assert.IsType<Color>(light);
        }

        [AvaloniaFact]
        public void DarkTheme_SoftRoleTokens_MatchAlphaOverRoleColor() {
            Application.Current!.TryGetResource("Foreman.Color.ErrorSoft", ThemeVariant.Dark, out var errorSoft);
            Application.Current!.TryGetResource("Foreman.Color.WarningSoft", ThemeVariant.Dark, out var warningSoft);
            Application.Current!.TryGetResource("Foreman.Color.SuccessSoft", ThemeVariant.Dark, out var successSoft);

            Assert.Equal(Color.Parse("#3DFF453A"), Assert.IsType<Color>(errorSoft));
            Assert.Equal(Color.Parse("#38FF9F0A"), Assert.IsType<Color>(warningSoft));
            Assert.Equal(Color.Parse("#7330D158"), Assert.IsType<Color>(successSoft));
        }

        [AvaloniaFact]
        public void LightTheme_SoftRoleTokens_MatchAlphaOverRoleColor() {
            Application.Current!.TryGetResource("Foreman.Color.ErrorSoft", ThemeVariant.Light, out var errorSoft);
            Application.Current!.TryGetResource("Foreman.Color.WarningSoft", ThemeVariant.Light, out var warningSoft);
            Application.Current!.TryGetResource("Foreman.Color.SuccessSoft", ThemeVariant.Light, out var successSoft);

            Assert.Equal(Color.Parse("#3DD70015"), Assert.IsType<Color>(errorSoft));
            Assert.Equal(Color.Parse("#38B25000"), Assert.IsType<Color>(warningSoft));
            Assert.Equal(Color.Parse("#7334C759"), Assert.IsType<Color>(successSoft));
        }

        [AvaloniaFact]
        public void OpacityDisabled_Is038_InBothVariants() {
            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", ThemeVariant.Dark, out var dark);
            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", ThemeVariant.Light, out var light);

            Assert.Equal(0.38, Assert.IsType<double>(dark));
            Assert.Equal(0.38, Assert.IsType<double>(light));
        }
    }
}
