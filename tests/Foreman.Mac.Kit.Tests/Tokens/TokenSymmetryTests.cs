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
            Assert.Equal(307, lightKeys.Count);
        }

        //The find ring is opaque Accent, never NodeSelected amber or the AccentSoft fill.
        [AvaloniaFact]
        public void FindMatchRing_CarriesTheRuledHexInBothThemes() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            Assert.Equal(Color.Parse("#FF007AFF"), Assert.IsType<Color>(light["Foreman.Color.FindMatchRing"]));
            Assert.Equal(Color.Parse("#FF0A84FF"), Assert.IsType<Color>(dark["Foreman.Color.FindMatchRing"]));
        }

        [AvaloniaFact]
        public void NodeSelected_Light_IsC86A00() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            Assert.Equal(Color.Parse("#C86A00"), Assert.IsType<Color>(light["Foreman.Color.NodeSelected"]));
            Assert.Equal(Color.Parse("#FF9F0A"), Assert.IsType<Color>(dark["Foreman.Color.NodeSelected"]));
        }

        public static IEnumerable<object[]> CanvasColorKeys() {
            yield return ["Foreman.Color.NodeRecipe", "#1D8878", "#45C4B0"];
            yield return ["Foreman.Color.NodeSupplier", "#8E4A90", "#CF7FD0"];
            yield return ["Foreman.Color.NodeConsumer", "#9A7318", "#E0B34D"];
            yield return ["Foreman.Color.NodePassthrough", "#77777C", "#9A9A9E"];
            yield return ["Foreman.Color.NodeTabBorder", "#73000000", "#80FFFFFF"];
            yield return ["Foreman.Color.NodeTabBorderContrast", "#99000000", "#99FFFFFF"];
            yield return ["Foreman.Color.TextOnWarning", "#FFFFFF", "#1A1A1A"];
        }

        [AvaloniaTheory]
        [MemberData(nameof(CanvasColorKeys))]
        public void CanvasColorKey_CarriesItsLightAndDarkHex(string key, string lightHex, string darkHex) {
            Assert.Equal(Color.Parse(lightHex), Assert.IsType<Color>(LoadThemeFile("Tokens.Light.axaml")[key]));
            Assert.Equal(Color.Parse(darkHex), Assert.IsType<Color>(LoadThemeFile("Tokens.Dark.axaml")[key]));
        }

        public static IEnumerable<object[]> CanvasDoubleKeys() {
            yield return ["Foreman.Opacity.NodeUnavailable", 0.62];
            yield return ["Foreman.Size.NodeIcon", 26.0];
            yield return ["Foreman.Size.NodeBuildingIcon", 20.0];
            yield return ["Foreman.Size.NodeTabIcon", 16.0];
            yield return ["Foreman.Size.NodeInsetTop", 18.0];
            yield return ["Foreman.Size.NodeInsetSide", 12.0];
            yield return ["Foreman.Size.NodeInsetBottom", 22.0];
            yield return ["Foreman.Size.NodeTypeStrip", 3.0];
            yield return ["Foreman.Size.NodeTypeStripInset", 12.0];
            yield return ["Foreman.Size.NodeTabHeight", 22.0];
            yield return ["Foreman.Size.NodeTabMinWidth", 44.0];
            yield return ["Foreman.Size.NodeTabOverhang", 13.0];
            yield return ["Foreman.Size.NodeBadgeHeight", 16.0];
            yield return ["Foreman.Size.NodePip", 6.0];
            yield return ["Foreman.Size.NodeGapTight", 3.0];
            yield return ["Foreman.Size.NodeBadgeStroke", 1.4];
            yield return ["Foreman.Size.LinkWidth", 3.0];
            yield return ["Foreman.Size.LinkWidthMax", 35.0];
            yield return ["Foreman.Size.LinkHitWidth", 12.0];
            yield return ["Foreman.Size.LinkStub", 12.0];
            yield return ["Foreman.Space.CapsuleLeading", 8.0];
            yield return ["Foreman.Space.CapsuleTrailing", 10.0];
            yield return ["Foreman.Size.WaypointDiameter", 10.0];
            yield return ["Foreman.Size.WaypointHitTarget", 20.0];
            yield return ["Foreman.Metrics.CanvasMinTextSize", 7.0];
            yield return ["Foreman.Metrics.GridMinScreenSpacing", 8.0];
        }

        [AvaloniaTheory]
        [MemberData(nameof(CanvasDoubleKeys))]
        public void CanvasDoubleKey_CarriesItsValueInBothThemes(string key, double value) {
            Assert.Equal(value, Assert.IsType<double>(LoadThemeFile("Tokens.Light.axaml")[key]));
            Assert.Equal(value, Assert.IsType<double>(LoadThemeFile("Tokens.Dark.axaml")[key]));
        }

        [AvaloniaFact]
        public void CanvasRadiiWeightAndDash_CarryTheirValuesInBothThemes() {
            foreach (string file in new[] { "Tokens.Light.axaml", "Tokens.Dark.axaml" }) {
                var tokens = LoadThemeFile(file);
                Assert.Equal(new CornerRadius(6), tokens["Foreman.Radius.NodeTab"]);
                Assert.Equal(new CornerRadius(4), tokens["Foreman.Radius.LinkCorner"]);
                Assert.Equal(new CornerRadius(3), tokens["Foreman.Radius.NodeTypeStrip"]);
                Assert.Equal(new CornerRadius(4), tokens["Foreman.Radius.NodeBeaconBadge"]);
                Assert.Equal(FontWeight.SemiBold, tokens["Foreman.Type.Canvas.Emphasized.FontWeight"]);
                Assert.Equal("5,3", Assert.IsType<string>(tokens["Foreman.Metrics.FindRingDash"]));
            }
        }

        [AvaloniaFact]
        public void RetiredCanvasBorderKeys_AreInNeitherDictionary() {
            string[] retired = ["Foreman.Color.CanvasTabBorder", "Foreman.Color.CanvasChipBorder", "Foreman.Color.CanvasErrorBorder"];
            var lightKeys = LoadThemeFile("Tokens.Light.axaml").Keys.Cast<string>().ToHashSet();
            var darkKeys = LoadThemeFile("Tokens.Dark.axaml").Keys.Cast<string>().ToHashSet();

            foreach (string key in retired) {
                Assert.DoesNotContain(key, lightKeys);
                Assert.DoesNotContain(key, darkKeys);
            }
        }

        //The semantic rebind adds these on top of the existing 25 colour keys, and renames Selection to
        //NodeSelected.
        [AvaloniaFact]
        public void TokenFiles_DeclareTheSemanticColorKeys() {
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
