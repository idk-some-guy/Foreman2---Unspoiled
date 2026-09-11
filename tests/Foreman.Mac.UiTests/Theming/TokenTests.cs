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
        public void DarkTheme_Selection_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.Selection", ThemeVariant.Dark, out var value);

            Assert.Equal(Color.Parse("#FF9F0A"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_CanvasBackground_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.CanvasBackground", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#FAFAFA"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_Accent_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.Accent", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#0068DA"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void LightTheme_Selection_MatchesContractHex() {
            Application.Current!.TryGetResource("Foreman.Color.Selection", ThemeVariant.Light, out var value);

            Assert.Equal(Color.Parse("#D97A06"), Assert.IsType<Color>(value));
        }
    }
}
