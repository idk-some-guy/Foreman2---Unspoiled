using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    public class TokenSymmetryTests {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static readonly string[] ConsumerRoots = [
            Path.Combine("src", "Foreman.Mac.Kit"),
            Path.Combine("src", "Foreman.Mac"),
            Path.Combine("tests", "Foreman.Mac.Kit.Tests"),
            Path.Combine("tests", "Foreman.Mac.UiTests"),
        ];

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
            Assert.Equal(270, lightKeys.Count);
        }

        //Ruling §06 token 1: the find ring is Accent at 55 % dark / 50 % light, never NodeSelected amber or the 16 % AccentSoft fill.
        [AvaloniaFact]
        public void FindMatchRing_CarriesTheRuledHexInBothThemes() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            Assert.Equal(Color.Parse("#80007AFF"), Assert.IsType<Color>(light["Foreman.Color.FindMatchRing"]));
            Assert.Equal(Color.Parse("#8C0A84FF"), Assert.IsType<Color>(dark["Foreman.Color.FindMatchRing"]));
        }

        //The semantic rebind adds these on top of the existing 25 colour keys, and renames Selection to
        //NodeSelected.
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

        //The pressed fill must read as more pushed-in than hover, in both themes.
        [AvaloniaFact]
        public void ControlBackgroundPressed_IsDarkerThanHover_BothThemes() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            Assert.True(Brightness(ColorOf(light, "Foreman.Color.ControlBackgroundPressed")) < Brightness(ColorOf(light, "Foreman.Color.ControlBackgroundHover")));
            Assert.True(Brightness(ColorOf(dark, "Foreman.Color.ControlBackgroundPressed")) < Brightness(ColorOf(dark, "Foreman.Color.ControlBackgroundHover")));
        }

        //AccentSoft is Accent's own RGB at a fixed alpha, not an independent value.
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
        public void FocusRingOffset_IsTwo_AndSymmetricAcrossThemes() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            Assert.Equal(2.0, (double)light["Foreman.FocusRing.Offset"]!);
            Assert.Equal(2.0, (double)dark["Foreman.FocusRing.Offset"]!);
        }

        //A bare substring match lets a key stand in for its own longer neighbour (Foreman.Space.Space1 is
        //"referenced" by any Foreman.Space.Space15 use); this requires the match not continue into another
        //identifier character. ScaleContract.cs is excluded - a key only listed in its MultipliedKeys ramp
        //and read by nothing else must still count as dead.
        private static bool IsConsumerFile(string path) =>
            (path.EndsWith(".axaml", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && Path.GetFileName(path) is not ("Tokens.Light.axaml" or "Tokens.Dark.axaml" or "ScaleContract.cs");

        private static bool IsTokenReferenced(string key, string haystack) =>
            System.Text.RegularExpressions.Regex.IsMatch(haystack, $@"(?<![\w.]){System.Text.RegularExpressions.Regex.Escape(key)}(?![\w.])");

        [AvaloniaFact]
        public void IsConsumerFile_ExcludesScaleContract() {
            Assert.False(IsConsumerFile(Path.Combine("src", "Foreman.Mac.Kit", "Appearance", "ScaleContract.cs")));
            Assert.True(IsConsumerFile(Path.Combine("src", "Foreman.Mac.Kit", "Composites", "AlertFrame.cs")));
        }

        [AvaloniaFact]
        public void IsTokenReferenced_APrefixKeyIsNotSatisfiedByAReferencedNeighboursKey() {
            string haystack = "the gap uses {DynamicResource Foreman.Space.Space15} here";

            Assert.False(IsTokenReferenced("Foreman.Space.Space1", haystack));
            Assert.True(IsTokenReferenced("Foreman.Space.Space15", haystack));
        }

        [AvaloniaFact]
        public void EveryDeclaredToken_HasAtLeastOneConsumer() {
            var declaredKeys = LoadThemeFile("Tokens.Light.axaml").Keys.Cast<string>().ToHashSet();

            string[] files = [.. ConsumerRoots
                .SelectMany(root => Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*", SearchOption.AllDirectories))
                .Where(IsConsumerFile)];

            string haystack = string.Join('\n', files.Select(File.ReadAllText));

            string[] unreferenced = [.. declaredKeys.Where(key => !IsTokenReferenced(key, haystack))];

            Assert.Empty(unreferenced);
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
