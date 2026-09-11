using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    public class TokenSymmetryTests {
        private static IResourceDictionary LoadThemeFile(string fileName) {
            var include = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri($"avares://Foreman.Mac.Kit/Themes/{fileName}"),
            };
            return include.Loaded as IResourceDictionary
                ?? throw new InvalidOperationException($"{fileName} did not load as a resource dictionary");
        }

        [AvaloniaFact]
        public void TokenFiles_LightAndDark_DeclareIdenticalKeySets() {
            var lightKeys = LoadThemeFile("Tokens.Light.axaml").Keys.Cast<string>().ToHashSet();
            var darkKeys = LoadThemeFile("Tokens.Dark.axaml").Keys.Cast<string>().ToHashSet();

            var onlyInLight = lightKeys.Except(darkKeys).ToArray();
            var onlyInDark = darkKeys.Except(lightKeys).ToArray();

            Assert.Empty(onlyInLight);
            Assert.Empty(onlyInDark);
            Assert.True(lightKeys.Count >= 100, $"expected a substantial token set, found {lightKeys.Count}");
        }

        //K1 task 2: the semantic rebind adds these on top of the existing 25 colour keys, and renames
        //Selection to NodeSelected (covered separately by AliasTests).
        [AvaloniaFact]
        public void TokenFiles_DeclareTheNewK1ColorKeys() {
            var lightKeys = LoadThemeFile("Tokens.Light.axaml").Keys.Cast<string>().ToHashSet();
            string[] newKeys = [
                "Foreman.Color.FocusRing",
                "Foreman.Color.SelectionInactive",
                "Foreman.Color.ControlBackgroundPressed",
                "Foreman.Color.TextOnAccent",
                "Foreman.Color.Warning",
                "Foreman.Color.Success",
                "Foreman.Color.ErrorSoft",
                "Foreman.Color.WarningSoft",
                "Foreman.Color.SuccessSoft",
                "Foreman.Color.PopoverMaterial",
                "Foreman.Color.TitlebarMaterialOpaque",
                "Foreman.Color.NodeSelected",
                "Foreman.Opacity.Disabled",
            ];

            foreach (string key in newKeys)
                Assert.Contains(key, lightKeys);

            Assert.DoesNotContain("Foreman.Color.Selection", lightKeys);
        }

        private static Color ColorOf(IResourceDictionary dictionary, string key) => (Color)dictionary[key]!;
        private static int Brightness(Color color) => color.R + color.G + color.B;

        //Fix round item 2: the pressed fill must read as more pushed-in than hover, in both themes.
        [AvaloniaFact]
        public void ControlBackgroundPressed_IsDarkerThanHover_BothThemes() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            Assert.True(Brightness(ColorOf(light, "Foreman.Color.ControlBackgroundPressed")) < Brightness(ColorOf(light, "Foreman.Color.ControlBackgroundHover")));
            Assert.True(Brightness(ColorOf(dark, "Foreman.Color.ControlBackgroundPressed")) < Brightness(ColorOf(dark, "Foreman.Color.ControlBackgroundHover")));
        }

        //Fix round item 4: AccentSoft is Accent's own RGB at a fixed alpha, not an independent value.
        [AvaloniaFact]
        public void AccentSoft_DerivesFromAccentColor_BothThemes() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            Color lightAccent = ColorOf(light, "Foreman.Color.Accent");
            Color lightAccentSoft = ColorOf(light, "Foreman.Color.AccentSoft");
            Color darkAccent = ColorOf(dark, "Foreman.Color.Accent");
            Color darkAccentSoft = ColorOf(dark, "Foreman.Color.AccentSoft");

            Assert.Equal((lightAccent.R, lightAccent.G, lightAccent.B), (lightAccentSoft.R, lightAccentSoft.G, lightAccentSoft.B));
            Assert.Equal((darkAccent.R, darkAccent.G, darkAccent.B), (darkAccentSoft.R, darkAccentSoft.G, darkAccentSoft.B));
        }

        [AvaloniaFact]
        public void KitEntryPoint_MergesWithoutError() {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };

            Application.Current!.Resources.MergedDictionaries.Add(kit);

            Assert.NotNull(kit.Loaded);
        }
    }
}
