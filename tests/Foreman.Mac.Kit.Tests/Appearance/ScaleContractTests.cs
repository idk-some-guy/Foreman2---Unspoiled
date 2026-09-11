using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Foreman.Mac.Kit.Appearance;
using System;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Appearance {
    //K1 task 7: ScaleContract.Apply(step) recomputes the kit's sizing tokens (type ramp, control
    //heights, icon/picker/chooser sizes, the form label column, the toolbar item height, the canvas
    //bridge's tokens) into Application.Resources, the same re-push shape AppearanceHub already uses for
    //colour. Borders, radii, the focus ring and the scrim never move.
    public class ScaleContractTests : IDisposable {
        public ScaleContractTests() {
            MergeKitIntoApplication();
            ScaleContract.Apply(1.0);
        }

        public void Dispose() {
            ScaleContract.Apply(1.0);
            GC.SuppressFinalize(this);
        }

        private static void MergeKitIntoApplication() {
            var kit = new ResourceInclude(new Uri("avares://Foreman.Mac.Kit/")) {
                Source = new Uri("avares://Foreman.Mac.Kit/Kit.axaml"),
            };
            Application.Current!.Resources.MergedDictionaries.Add(kit);
        }

        private static double ResourceDouble(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            return Assert.IsType<double>(value);
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_MultipliesTheTypeRamp() {
            ScaleContract.Apply(1.5);

            Assert.Equal(30.0, ResourceDouble("Foreman.Type.LargeTitle.FontSize"));
            Assert.Equal(19.5, ResourceDouble("Foreman.Type.Body.FontSize"));
            Assert.Equal(19.5, ResourceDouble("Foreman.Type.EmphasizedBody.FontSize"));
            Assert.Equal(18.0, ResourceDouble("Foreman.Type.Callout.FontSize"));
            Assert.Equal(16.5, ResourceDouble("Foreman.Type.Caption.FontSize"));
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_MultipliesControlHeights() {
            ScaleContract.Apply(1.5);

            Assert.Equal(33.0, ResourceDouble("Foreman.Size.ControlHeightRegular"));
            Assert.Equal(36.0, ResourceDouble("Foreman.Size.ControlHeightLarge"));
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_MultipliesIconPickerAndChooserSizes() {
            ScaleContract.Apply(1.5);

            Assert.Equal(39.0, ResourceDouble("Foreman.Size.PickCell"));
            Assert.Equal(132.0, ResourceDouble("Foreman.Size.ChooserCell"));
            Assert.Equal(33.0, ResourceDouble("Foreman.Size.PaletteRowIcon"));
            Assert.Equal(60.0, ResourceDouble("Foreman.Size.BrowserCell"));
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_MultipliesTheFormLabelColumn() {
            ScaleContract.Apply(1.5);

            Assert.Equal(198.0, ResourceDouble("Foreman.Size.FormLabelColumn"));
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_MultipliesToolbarItemHeightAndTheCanvasBridgeTokens() {
            ScaleContract.Apply(1.5);

            Assert.Equal(39.0, ResourceDouble("Foreman.Size.ToolbarItem"));
            Assert.Equal(12.0, ResourceDouble("Foreman.Size.NodePadding"));
            Assert.Equal(15.0, ResourceDouble("Foreman.Type.Canvas.FontSize"));
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_MultipliesTheGlyphSizeTokens() {
            ScaleContract.Apply(1.5);

            Assert.Equal(24.0, ResourceDouble("Foreman.Size.Glyph16"));
            Assert.Equal(25.5, ResourceDouble("Foreman.Size.Glyph17"));
            Assert.Equal(27.0, ResourceDouble("Foreman.Size.Glyph18"));
            Assert.Equal(30.0, ResourceDouble("Foreman.Size.Glyph20"));
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_MultipliesAliasedTypeKeysToo() {
            ScaleContract.Apply(1.5);

            Assert.Equal(19.5, ResourceDouble("Foreman.Type.WindowTitle.FontSize"));
            Assert.Equal(16.5, ResourceDouble("Foreman.Type.InspectorSubtitle.FontSize"));
        }

        [AvaloniaFact]
        public void Apply_AtOneFifty_LeavesBordersRadiiFocusRingAndScrimFixed() {
            ScaleContract.Apply(1.5);

            Assert.Equal(3.0, ResourceDouble("Foreman.FocusRing.Width"));

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(new CornerRadius(6), radius);

            Application.Current!.TryGetResource("Foreman.Color.SheetScrim", Application.Current.ActualThemeVariant, out object? scrim);
            Assert.Equal(Color.Parse("#80000000"), scrim);
        }

        [AvaloniaFact]
        public void Apply_IsIdempotent_RepeatedCallsDoNotCompound() {
            ScaleContract.Apply(1.5);
            ScaleContract.Apply(1.5);

            Assert.Equal(19.5, ResourceDouble("Foreman.Type.Body.FontSize"));
        }

        [AvaloniaFact]
        public void Apply_BackToOne_RestoresBaseValues() {
            ScaleContract.Apply(1.5);
            ScaleContract.Apply(1.0);

            Assert.Equal(13.0, ResourceDouble("Foreman.Type.Body.FontSize"));
            Assert.Equal(22.0, ResourceDouble("Foreman.Size.ControlHeightRegular"));
        }
    }
}
