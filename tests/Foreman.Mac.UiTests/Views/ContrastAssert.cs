using Avalonia.Media;
using System;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Reusable across every window with a hardcoded-light row Background (Settings' Enabled Objects tab,
    //Graph Summary's Buildings/Beacons/Items tabs): the live Fluent dark theme's default TextBlock
    //Foreground is white, so a row that binds Background without also binding Foreground renders invisible
    //text the moment its background is a light color - see docs/upstream-divergences.md.
    internal static class ContrastAssert {
        public static void Readable(IBrush? foreground, IBrush? background) {
            Assert.True(foreground is ISolidColorBrush, "Row foreground must be an explicit solid color, not left on the ambient theme default.");
            Assert.True(background is ISolidColorBrush, "Row background must be an explicit solid color to check contrast against.");
            Color fg = ((ISolidColorBrush)foreground!).Color;
            Color bg = ((ISolidColorBrush)background!).Color;
            double contrast = ContrastRatio(fg, bg);
            Assert.True(contrast >= 4.5, $"Foreground {fg} on background {bg} has contrast ratio {contrast:0.00}, below the WCAG AA minimum of 4.5.");
        }

        private static double ContrastRatio(Color a, Color b) {
            double lumA = RelativeLuminance(a) + 0.05;
            double lumB = RelativeLuminance(b) + 0.05;
            return lumA > lumB ? lumA / lumB : lumB / lumA;
        }

        private static double RelativeLuminance(Color c) =>
            0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        private static double Channel(byte value) {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}
