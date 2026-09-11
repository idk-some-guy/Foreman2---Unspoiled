using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    public class AliasTests {
        private static string RepoRoot => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");

        private static (string OldKey, string NewKey)[] LoadCsv() {
            string csvPath = Path.Combine(RepoRoot, "tools", "Kit", "alias-map.csv");
            return [.. File.ReadAllLines(csvPath)
                .Skip(1)
                .Where(line => line.Length > 0)
                .Select(line => line.Split(','))
                .Select(parts => (parts[0], parts[1]))];
        }

        private static IResourceDictionary LoadThemeFile(string fileName) {
            var include = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri($"avares://Foreman.Mac.Kit/Themes/{fileName}"),
            };
            return include.Loaded as IResourceDictionary
                ?? throw new InvalidOperationException($"{fileName} did not load as a resource dictionary");
        }

        //Aliases.axaml keys entries per ThemeVariant; subtract Tokens.Light's own keys to get the aliases
        private static HashSet<string> AliasKeys() {
            var aliasesRoot = LoadThemeFile("Aliases.axaml");
            var lightProvider = aliasesRoot.ThemeDictionaries[ThemeVariant.Light];
            var lightDictionary = lightProvider switch {
                ResourceInclude resourceInclude => resourceInclude.Loaded,
                IResourceDictionary loaded => loaded,
                _ => throw new InvalidOperationException($"unsupported theme provider type {lightProvider.GetType()}"),
            };

            var tokenKeys = LoadThemeFile("Tokens.Light.axaml").Keys.Cast<string>().ToHashSet();
            return [.. lightDictionary!.Keys.Cast<string>().Where(key => !tokenKeys.Contains(key))];
        }

        [AvaloniaFact]
        public void AliasMapCsv_MatchesAliasesAxamlKeySet() {
            var csvOldKeys = LoadCsv().Select(row => row.OldKey).ToHashSet();
            var axamlKeys = AliasKeys();

            Assert.Empty(csvOldKeys.Except(axamlKeys));
            Assert.Empty(axamlKeys.Except(csvOldKeys));
        }

        [AvaloniaFact]
        public void RemovedSelectionKey_ResolvesThroughAlias_ToHistoricalLightValue() {
            MergeKitIntoApplication();

            Application.Current!.TryGetResource("Foreman.Color.Selection", ThemeVariant.Light, out object? value);

            Assert.Equal(Color.Parse("#D97A06"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void RemovedSelectionKey_ResolvesThroughAlias_ToHistoricalDarkValue() {
            MergeKitIntoApplication();

            Application.Current!.TryGetResource("Foreman.Color.Selection", ThemeVariant.Dark, out object? value);

            Assert.Equal(Color.Parse("#FF9F0A"), Assert.IsType<Color>(value));
        }

        [AvaloniaFact]
        public void RemovedSelectionKey_ResolvesToTheSameValueAsNodeSelected_BothVariants() {
            MergeKitIntoApplication();

            Application.Current!.TryGetResource("Foreman.Color.Selection", ThemeVariant.Light, out object? oldLight);
            Application.Current!.TryGetResource("Foreman.Color.NodeSelected", ThemeVariant.Light, out object? newLight);
            Application.Current!.TryGetResource("Foreman.Color.Selection", ThemeVariant.Dark, out object? oldDark);
            Application.Current!.TryGetResource("Foreman.Color.NodeSelected", ThemeVariant.Dark, out object? newDark);

            Assert.Equal(newLight, oldLight);
            Assert.Equal(newDark, oldDark);
        }

        //Fix round item 3: a real DynamicResource consumer, not just a direct TryGetResource call.
        [AvaloniaFact]
        public void RealDynamicResourceConsumer_Border_FollowsThemeVariant_ThroughAlias() {
            MergeKitIntoApplication();
            var border = new Border();
            border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Foreman.Color.Selection"));
            var window = new Window { Content = border };
            window.Show();

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Color lightRendered = Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;
            Assert.Equal(Color.Parse("#D97A06"), lightRendered);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Color darkRendered = Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;
            Assert.Equal(Color.Parse("#FF9F0A"), darkRendered);
        }

        private static void MergeKitIntoApplication() {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };
            Application.Current!.Resources.MergedDictionaries.Add(kit);
        }
    }
}
