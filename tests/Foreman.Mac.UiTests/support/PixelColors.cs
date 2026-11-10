using SkiaSharp;
using System;
using Xunit;

namespace Foreman.Mac.UiTests.support {
    internal static class PixelColors {
        internal static SKColor Over(SKColor top, SKColor ground) {
            float a = top.Alpha / 255f;
            byte Mix(byte t, byte g) => (byte)Math.Round((t * a) + (g * (1 - a)));
            return new SKColor(Mix(top.Red, ground.Red), Mix(top.Green, ground.Green), Mix(top.Blue, ground.Blue));
        }

        internal static void AssertClose(SKColor expected, SKColor actual) {
            Assert.InRange(actual.Red, Math.Max(0, expected.Red - 2), Math.Min(255, expected.Red + 2));
            Assert.InRange(actual.Green, Math.Max(0, expected.Green - 2), Math.Min(255, expected.Green + 2));
            Assert.InRange(actual.Blue, Math.Max(0, expected.Blue - 2), Math.Min(255, expected.Blue + 2));
        }
    }
}
