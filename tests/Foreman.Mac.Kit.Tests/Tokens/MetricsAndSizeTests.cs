using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using System;
using System.IO;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    public class MetricsAndSizeTests {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string ReadComposite(string fileName) =>
            File.ReadAllText(Path.Combine(RepoRoot, "src", "Foreman.Mac.Kit", "Composites", fileName));

        [AvaloniaFact]
        public void CompositeTemplates_DeclareNoLiteralRadiusOrGlyphSize() {
            string chooserCell = ReadComposite("ChooserCell.axaml");
            Assert.DoesNotContain("CornerRadius=\"8\"", chooserCell);

            string paletteRow = ReadComposite("PaletteRow.axaml");
            Assert.DoesNotContain("CornerRadius=\"5\"", paletteRow);

            string recentRow = ReadComposite("RecentRow.axaml");
            Assert.DoesNotContain("CornerRadius=\"7\"", recentRow);

            string alertFrame = ReadComposite("AlertFrame.axaml");
            Assert.DoesNotContain("Width=\"32\" Height=\"32\"", alertFrame);
            Assert.DoesNotContain("LineHeight=\"16.8\"", alertFrame);

            string zoomStepper = ReadComposite("ZoomStepper.axaml");
            Assert.DoesNotContain("Padding=\"2\"", zoomStepper);
            Assert.DoesNotContain("Spacing=\"2\"", zoomStepper);

            string statePanel = ReadComposite("StatePanel.axaml");
            Assert.DoesNotContain("LineHeight=\"16.8\"", statePanel);
        }

        private static IResourceDictionary LoadThemeFile(string fileName) {
            var include = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri($"avares://Foreman.Mac.Kit/Themes/{fileName}"),
            };
            return include.Loaded as IResourceDictionary
                ?? throw new InvalidOperationException($"{fileName} did not load as a resource dictionary");
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void SheetBottomRadius_IsTen(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(new CornerRadius(0, 0, 10, 10), tokens["Foreman.Radius.SheetBottom"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void NodeRadius_IsTen(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(new CornerRadius(10), tokens["Foreman.Radius.Node"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void ControlHeights_AreRegularTwentyTwo_AndLargeTwentyFour(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(22d, tokens["Foreman.Size.ControlHeightRegular"]);
            Assert.Equal(24d, tokens["Foreman.Size.ControlHeightLarge"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void InspectorWidth_KeepsThreeHundred_WithNewMinAndMaxBounds(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(300d, tokens["Foreman.Size.InspectorWidth"]);
            Assert.Equal(260d, tokens["Foreman.Size.InspectorWidthMin"]);
            Assert.Equal(380d, tokens["Foreman.Size.InspectorWidthMax"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void ZoomStepperTokens_ReadoutSixteenTall_MicroButtonTwentyBySixteen(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(16d, tokens["Foreman.Size.ZoomReadoutHeight"]);
            Assert.Equal(20d, tokens["Foreman.Size.ZoomStepperMicroButtonWidth"]);
            Assert.Equal(16d, tokens["Foreman.Size.ZoomStepperMicroButtonHeight"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void ToolbarItemIconOnlyWidth_IsTwentyEight(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(28d, tokens["Foreman.Size.ToolbarItemIconOnlyWidth"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void ChooserCell_IsEightyEight(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.Equal(88d, tokens["Foreman.Size.ChooserCell"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void ShadowWindowAndShadowCard_AreDefined(string fileName) {
            var tokens = LoadThemeFile(fileName);
            Assert.IsType<BoxShadows>(tokens["Foreman.Shadow.Window"]);
            Assert.IsType<BoxShadows>(tokens["Foreman.Shadow.Card"]);
        }

        [AvaloniaFact]
        public void ShadowCard_HasNoOpacityInDark_ButKeepsGeometry() {
            var light = LoadThemeFile("Tokens.Light.axaml");
            var dark = LoadThemeFile("Tokens.Dark.axaml");

            BoxShadow lightShadow = ((BoxShadows)light["Foreman.Shadow.Card"]!)[0];
            BoxShadow darkShadow = ((BoxShadows)dark["Foreman.Shadow.Card"]!)[0];

            Assert.Equal(0, lightShadow.OffsetX);
            Assert.Equal(2, lightShadow.OffsetY);
            Assert.Equal(8, lightShadow.Blur);
            Assert.Equal((byte)0x12, lightShadow.Color.A);

            Assert.Equal(lightShadow.OffsetX, darkShadow.OffsetX);
            Assert.Equal(lightShadow.OffsetY, darkShadow.OffsetY);
            Assert.Equal(lightShadow.Blur, darkShadow.Blur);
            Assert.Equal((byte)0, darkShadow.Color.A);
        }
    }
}
