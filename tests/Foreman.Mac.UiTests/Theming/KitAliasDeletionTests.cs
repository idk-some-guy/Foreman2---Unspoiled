using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    public class KitAliasDeletionTests {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        private static string AppSourceDirectory => Path.Combine(RepoRoot, "src", "Foreman.Mac");

        private static readonly Regex ResourceReference =
            new(@"\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

        private static readonly Regex CodeResourceReference =
            new(@"(?:\b(?:FindResource|TryFindResource|GetResourceObservable|ResolveColor|Brush|Art)\s*\(\s*""(?<key>(?:Foreman|Kit)\.[A-Za-z0-9_.]+)""|,\s*""(?<key>Foreman\.[A-Za-z0-9_.]+)""\s*\)|=\s*""(?<key>Foreman\.[A-Za-z0-9_.]+)"")", RegexOptions.Compiled);

        private static readonly Regex KeyDeclaration =
            new(@"x:Key\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);

        private static readonly HashSet<string> PlatformKeyAllowlist =
            new(StringComparer.Ordinal) { "SystemChromeMediumColor" }; // Avalonia's, not ours to declare

        private static IEnumerable<string> AxamlFiles() =>
            Directory.EnumerateFiles(AppSourceDirectory, "*.axaml", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        private static IEnumerable<string> CsFiles() =>
            Directory.EnumerateFiles(AppSourceDirectory, "*.cs", SearchOption.AllDirectories)
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
        public void EveryResourceReference_UnderApp_ResolvesInBothVariants() {
            string[] axamlFiles = [.. AxamlFiles()];
            Assert.True(axamlFiles.Length >= 25, $"expected at least 25 app axaml files, found {axamlFiles.Length}");

            string[] csFiles = [.. CsFiles()];
            Assert.True(csFiles.Length >= 90, $"expected at least 90 app cs files, found {csFiles.Length}");

            string[] missing = [
                .. FindMissingResourceKeys(axamlFiles.Select(File.ReadAllText)),
                .. FindMissingCodeResourceKeys(csFiles.Select(File.ReadAllText)),
            ];

            Assert.Empty(missing);
        }

        [Fact]
        public void AppResources_MergeTheKitAndNothingElse() {
            string app = File.ReadAllText(Path.Combine(AppSourceDirectory, "App.axaml"));
            string[] includes = [.. Regex.Matches(app, @"<(?:ResourceInclude|StyleInclude)\s+Source=""([^""]+)""").Select(m => m.Groups[1].Value)];

            Assert.Equal(["avares://Foreman.Mac.Kit/Kit.axaml"], includes);
            Assert.Contains("<FluentTheme />", app);
            Assert.False(Directory.Exists(Path.Combine(AppSourceDirectory, "Themes")));
        }

        [Fact]
        public void Scan_MatchesNonForemanPrefixedKeys() {
            var referenced = AxamlFiles()
                .SelectMany(file => ResourceReference.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));

            Assert.Contains("ComparatorRowTemplate", referenced);
        }

        [AvaloniaFact]
        public void Scan_FlagsUnresolvableKey() {
            const string syntheticAxaml = "<Border Background=\"{StaticResource DoesNotExist.ForTest}\" />";
            const string syntheticCode = "this.GetResourceObservable(\"Foreman.DoesNotExist.ForTestCode\");";

            Assert.Contains("DoesNotExist.ForTest", FindMissingResourceKeys([syntheticAxaml]));
            Assert.Contains("Foreman.DoesNotExist.ForTestCode", FindMissingCodeResourceKeys([syntheticCode]));
        }

        [AvaloniaFact]
        public void Scan_FlagsUnresolvableKeyInEveryCallShape() {
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

        [Fact]
        public void Scan_ExtractsEveryGalleryKey() {
            string[] keys = CodeKeys([File.ReadAllText(Path.Combine(AppSourceDirectory, "Views", "GalleryWindow.cs"))]);

            string[] expected = ["Accent", "Border", "BorderStrong", "CanvasBackground", "Error", "GalleryChrome", "SidebarBackground", "Success", "TextTertiary", "Warning", "WellBackground", "WindowBackground"];
            Assert.Empty(expected.Select(name => "Foreman.Color." + name).Except(keys));
        }
    }
}
