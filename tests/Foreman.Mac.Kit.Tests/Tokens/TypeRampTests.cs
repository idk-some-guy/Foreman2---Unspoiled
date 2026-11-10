using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using System;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    public class TypeRampTests {
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
        public void FiveStepRamp_HasTheExactSizeWeightPairs(string fileName) {
            var tokens = LoadThemeFile(fileName);

            Assert.Equal(20d, tokens["Foreman.Type.LargeTitle.FontSize"]);
            Assert.Equal(FontWeight.Bold, tokens["Foreman.Type.LargeTitle.FontWeight"]);
            Assert.Equal(13d, tokens["Foreman.Type.EmphasizedBody.FontSize"]);
            Assert.Equal(FontWeight.SemiBold, tokens["Foreman.Type.EmphasizedBody.FontWeight"]);
            Assert.Equal(13d, tokens["Foreman.Type.Body.FontSize"]);
            Assert.Equal(FontWeight.Normal, tokens["Foreman.Type.Body.FontWeight"]);
            Assert.Equal(12d, tokens["Foreman.Type.Callout.FontSize"]);
            Assert.Equal(FontWeight.Normal, tokens["Foreman.Type.Callout.FontWeight"]);
            Assert.Equal(11d, tokens["Foreman.Type.Caption.FontSize"]);
            Assert.Equal(FontWeight.Normal, tokens["Foreman.Type.Caption.FontWeight"]);
        }

        [AvaloniaTheory]
        [InlineData("Tokens.Light.axaml")]
        [InlineData("Tokens.Dark.axaml")]
        public void SectionHeader_FontWeightIsSixHundred_LetterSpacingStaysDefined(string fileName) {
            var tokens = LoadThemeFile(fileName);

            Assert.Equal(FontWeight.SemiBold, tokens["Foreman.Type.SectionHeader.FontWeight"]);
            Assert.Equal(0.33d, tokens["Foreman.Type.SectionHeader.LetterSpacing"]);
        }
    }
}
