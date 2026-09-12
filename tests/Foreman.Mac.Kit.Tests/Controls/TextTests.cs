using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public class TextTests {
        private static ControlTheme KitTextTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static TextBlock Themed(string text, params string[] classes) {
            var block = new TextBlock { Theme = KitTextTheme(), Text = text };
            foreach (string cls in classes)
                block.Classes.Add(cls);
            return block;
        }

        [AvaloniaFact]
        public void KitText_IsAControlThemeTargetingTextBlock() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(TextBlock), KitTextTheme().TargetType);
        }

        [AvaloniaFact]
        public void NoRampClass_DefaultsForegroundToTextToken() {
            using var fixture = new KitFixture();
            var block = Themed("Iron Plate");
            fixture.Window.Content = block;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.Text")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground).Color);
        }

        public static IEnumerable<object[]> RampSteps() {
            yield return ["title", "Foreman.Type.LargeTitle.FontSize", "Foreman.Type.LargeTitle.FontWeight"];
            yield return ["headline", "Foreman.Type.EmphasizedBody.FontSize", "Foreman.Type.EmphasizedBody.FontWeight"];
            yield return ["body", "Foreman.Type.Body.FontSize", "Foreman.Type.Body.FontWeight"];
            yield return ["callout", "Foreman.Type.Callout.FontSize", "Foreman.Type.Callout.FontWeight"];
            yield return ["caption", "Foreman.Type.Caption.FontSize", "Foreman.Type.Caption.FontWeight"];
        }

        [AvaloniaTheory]
        [MemberData(nameof(RampSteps))]
        public void RampClass_SetsFontSizeAndWeightFromItsStepToken(string rampClass, string sizeKey, string weightKey) {
            using var fixture = new KitFixture();
            var block = Themed("Iron Plate Line", rampClass);
            fixture.Window.Content = block;

            Application.Current!.TryGetResource(sizeKey, Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource(weightKey, Application.Current.ActualThemeVariant, out object? weight);

            Assert.Equal(Assert.IsType<double>(size), block.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), block.FontWeight);
        }

        [AvaloniaFact]
        public void SecondaryClass_SetsForegroundToTextSecondaryToken() {
            using var fixture = new KitFixture();
            var block = Themed("v 1.1.0u", "caption", "secondary");
            fixture.Window.Content = block;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.TextSecondary")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground).Color);
        }

        [AvaloniaFact]
        public void TertiaryClass_SetsForegroundToTextTertiaryToken() {
            using var fixture = new KitFixture();
            var block = Themed("AppSettings.DefaultNodeDirection", "caption", "tertiary");
            fixture.Window.Content = block;

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.TextTertiary")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground).Color);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class TextScaleTests {
        [AvaloniaFact]
        public void TitleClass_FontSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            var block = new TextBlock { Theme = Assert.IsType<ControlTheme>(theme), Text = "Graph Summary" };
            block.Classes.Add("title");
            fixture.Window.Content = block;

            ScaleContract.Apply(1.5);

            Assert.Equal(30.0, block.FontSize);
        }
    }
}
