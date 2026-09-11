using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
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
            Assert.True(lightKeys.Count >= 30, $"expected a substantial token set, found {lightKeys.Count}");
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
