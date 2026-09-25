using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using System;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    public class ContractColorTests {
        private static IResourceDictionary LoadThemeFile(string fileName) {
            var include = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri($"avares://Foreman.Mac.Kit/Themes/{fileName}"),
            };
            return include.Loaded as IResourceDictionary
                ?? throw new InvalidOperationException($"{fileName} did not load as a resource dictionary");
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.CanvasBackground", "#1E1E20")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.Accent", "#0A84FF")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.CanvasBackground", "#FAFAFA")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.Accent", "#007AFF")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.NodeSelected", "#FF9F0A")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.NodeSelected", "#C86A00")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.Warning", "#FF9F0A")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.Warning", "#B25000")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.TextOnAccent", "#FFFFFF")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.TextOnAccent", "#FFFFFF")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.ControlBackgroundPressed", "#E6E6E8")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.SelectionInactive", "#21FFFFFF")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.SelectionInactive", "#17000000")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.ErrorSoft", "#3DFF453A")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.WarningSoft", "#38FF9F0A")]
        [InlineData("Tokens.Dark.axaml", "Foreman.Color.SuccessSoft", "#7330D158")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.ErrorSoft", "#3DD70015")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.WarningSoft", "#38B25000")]
        [InlineData("Tokens.Light.axaml", "Foreman.Color.SuccessSoft", "#7334C759")]
        public void ColorToken_MatchesContractHex(string fileName, string key, string hex) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(Color.Parse(hex), Assert.IsType<Color>(tokens[key]));
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void FocusRing_MatchesAccent(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(tokens["Foreman.Color.Accent"], tokens["Foreman.Color.FocusRing"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void OpacityDisabled_Is038(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(0.38, Assert.IsType<double>(tokens["Foreman.Opacity.Disabled"]));
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void Success_IsDeclared(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.True(tokens.ContainsKey("Foreman.Color.Success"));
            Assert.IsType<Color>(tokens["Foreman.Color.Success"]);
        }

        //the kit mock's 'pressed darker than hover' rule wins over the contract table's #525256
        [AvaloniaFact]
        public void DarkTheme_ControlBackgroundPressed_IsDarkerThanHoverPer12PercentRule() {
            var tokens = LoadThemeFile("Tokens.Dark.axaml");

            Assert.Equal(Color.Parse("#3F3F43"), Assert.IsType<Color>(tokens["Foreman.Color.ControlBackgroundPressed"]));
        }
    }
}
