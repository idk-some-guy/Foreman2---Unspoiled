using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    //The app-side half of this scan lives in Foreman.Mac.UiTests: this project has no reference to
    //Foreman.Mac, so its avares:// resources cannot load here.
    public class KitAliasDeletionTests {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static void MergeKitIntoApplication() {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };
            Application.Current!.Resources.MergedDictionaries.Add(kit);
        }

        private static readonly string[] AddendumKeys = [
            // primitives addendum §10 Token changes
            "Foreman.Color.PopoverMaterial",
            "Foreman.Color.SliderThumb",
            "Foreman.Color.Text",
            "Foreman.Color.TooltipMaterial",
            "Foreman.Color.TooltipMaterialOpaque",
            "Foreman.Color.TooltipText",
            "Foreman.Metrics.SearchFieldMinWidth",
            "Foreman.Metrics.SearchFieldNaturalWidth",
            "Foreman.Radius.Pill",
            "Foreman.Radius.PopUpIndicator",
            "Foreman.Shadow.SliderThumb",
            "Foreman.Shadow.Tooltip",
            "Foreman.Size.GlyphMicro",
            "Foreman.Size.GlyphSmall",
            "Foreman.Size.ListRowHeight",
            "Foreman.Size.MenuItemHeight",
            "Foreman.Size.PopUpIndicator",
            "Foreman.Size.SidebarRowHeight",
            "Foreman.Type.SectionHeader.FontSize",
            "Foreman.Type.SectionHeader.FontWeight",
            "Foreman.Type.SectionHeader.LetterSpacing",
            "Foreman.Type.TabLabel.FontSize",
            "Foreman.Type.TabLabel.FontWeight",
            // composites addendum §12 Token changes
            "Foreman.Color.AccentSoft",
            "Foreman.Color.PopoverMaterialOpaque",
            "Foreman.Color.SidebarBackground",
            "Foreman.Color.WindowBackground",
            "Foreman.Metrics.SheetWidthAlert",
            "Foreman.Radius.ChooserCell",
            "Foreman.Radius.Popover",
            "Foreman.Size.FormLabelColumn",
            "Foreman.Size.FormLabelColumnInspector",
            "Foreman.Size.GlyphState",
            "Foreman.Size.PopoverTail",
            "Foreman.Size.RadialHub",
            "Foreman.Size.RadialOuter",
            "Foreman.Size.RecentRowHeight",
            "Foreman.Size.RecentRowIcon",
            "Foreman.Size.SpinnerSmall",
            "Foreman.Size.SpinnerStrokeSmall",
            "Foreman.Size.SplitDividerHit",
            "Foreman.Size.ToolbarRowHeight",
            "Foreman.Size.ZoomReadoutWidth",
            "Foreman.Type.Caption.Emphasized.FontWeight",
        ];

        [AvaloniaFact]
        public void EveryAddendumToken_ResolvesInBothVariants() {
            MergeKitIntoApplication();

            foreach (string key in AddendumKeys) {
                Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out _), $"{key} did not resolve for Light");
                Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Dark, out _), $"{key} did not resolve for Dark");
            }
        }

        private static readonly Regex ResourceReference =
            new(@"\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

        private static readonly Regex CodeResourceReference =
            new(@"(?:\b(?:FindResource|TryFindResource|GetResourceObservable|ResolveColor|Brush|Art)\s*\(\s*""(?<key>(?:Foreman|Kit)\.[A-Za-z0-9_.]+)""|,\s*""(?<key>Foreman\.[A-Za-z0-9_.]+)""\s*\)|=\s*""(?<key>Foreman\.[A-Za-z0-9_.]+)"")", RegexOptions.Compiled);

        private static readonly Regex KeyDeclaration =
            new(@"x:Key\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);

        private static readonly HashSet<string> PlatformKeyAllowlist =
            new(StringComparer.Ordinal); // no kit axaml references a key Avalonia itself supplies

        private static IEnumerable<string> AxamlFilesUnder(string root) =>
            Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.axaml", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        private static IEnumerable<string> CsFilesUnder(string root) =>
            Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        private static string[] FindMissingResourceKeys(IEnumerable<string> axamlTexts) {
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string text in axamlTexts) {
                var declaredInFile = new HashSet<string>(
                    KeyDeclaration.Matches(text).Select(m => m.Groups[1].Value), StringComparer.Ordinal);
                foreach (Match match in ResourceReference.Matches(text)) {
                    string key = match.Groups[1].Value;
                    if (declaredInFile.Contains(key) || PlatformKeyAllowlist.Contains(key))
                        continue;
                    if (!Application.Current!.TryGetResource(key, ThemeVariant.Light, out _) ||
                        !Application.Current!.TryGetResource(key, ThemeVariant.Dark, out _))
                        missing.Add(key);
                }
            }
            return [.. missing];
        }

        private static string[] CodeKeys(IEnumerable<string> csTexts) =>
            [.. csTexts.SelectMany(text => CodeResourceReference.Matches(text).Select(m => m.Groups["key"].Value)).Distinct(StringComparer.Ordinal)];

        private static string[] FindMissingCodeResourceKeys(IEnumerable<string> csTexts) {
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string key in CodeKeys(csTexts)) {
                if (!Application.Current!.TryGetResource(key, ThemeVariant.Light, out _) ||
                    !Application.Current!.TryGetResource(key, ThemeVariant.Dark, out _))
                    missing.Add(key);
            }
            return [.. missing];
        }

        [AvaloniaFact]
        public void EveryResourceReference_UnderKit_ResolvesInBothVariants() {
            MergeKitIntoApplication();

            string[] axamlFiles = [.. AxamlFilesUnder(Path.Combine("src", "Foreman.Mac.Kit"))];
            Assert.True(axamlFiles.Length >= 40, $"expected at least 40 kit axaml files, found {axamlFiles.Length}");

            string[] csFiles = [.. CsFilesUnder(Path.Combine("src", "Foreman.Mac.Kit"))];
            Assert.True(csFiles.Length >= 50, $"expected at least 50 kit cs files, found {csFiles.Length}");

            string[] missing = [
                .. FindMissingResourceKeys(axamlFiles.Select(File.ReadAllText)),
                .. FindMissingCodeResourceKeys(csFiles.Select(File.ReadAllText)),
            ];

            Assert.Empty(missing);
        }

        [Fact]
        public void TokenFiles_DeclareNoForwardingAlias() {
            foreach (string file in new[] { "Tokens.Light.axaml", "Tokens.Dark.axaml" }) {
                string text = File.ReadAllText(Path.Combine(RepoRoot, "src", "Foreman.Mac.Kit", "Themes", file));
                string[] forwards = [.. Regex.Matches(text, @"<StaticResource\s+x:Key=""([^""]+)""").Select(m => m.Groups[1].Value)];
                Assert.Empty(forwards);
            }
        }

        [Fact]
        public void Scan_MatchesKitPrefixedKeys() {
            var referenced = AxamlFilesUnder(Path.Combine("src", "Foreman.Mac.Kit"))
                .SelectMany(file => ResourceReference.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));

            Assert.Contains("Kit.SheetFrame", referenced);
        }

        [AvaloniaFact]
        public void Scan_FlagsUnresolvableKey() {
            MergeKitIntoApplication();

            const string syntheticAxaml = "<Border Background=\"{StaticResource Kit.DoesNotExist.ForTest}\" />";
            const string syntheticCode = "this.GetResourceObservable(\"Kit.DoesNotExist.ForTestCode\");";

            Assert.Contains("Kit.DoesNotExist.ForTest", FindMissingResourceKeys([syntheticAxaml]));
            Assert.Contains("Kit.DoesNotExist.ForTestCode", FindMissingCodeResourceKeys([syntheticCode]));
        }

        [AvaloniaFact]
        public void Scan_FlagsUnresolvableKeyInEveryCallShape() {
            MergeKitIntoApplication();

            string[] fixtures = [
                "LiveBrush.Bind(c, p, \"Foreman.Color.Nope\")",
                "Live(new Border { }, Border.BackgroundProperty, \"Foreman.Color.Nope\")",
                "string fill = \"Foreman.Color.Nope\"",
                "live.Art(\"Foreman.Color.Nope\")",
                "Application.Current!.FindResource(\"Foreman.Color.Nope\")",
            ];

            foreach (string fixture in fixtures)
                Assert.Equal(["Foreman.Color.Nope"], FindMissingCodeResourceKeys([fixture]));
        }
    }
}
