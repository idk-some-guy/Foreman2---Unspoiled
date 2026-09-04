using Avalonia;
using Foreman.Mac.Converters;
using System.Globalization;
using Xunit;

namespace Foreman.Mac.UiTests.Converters {
    //Gate-failure fix (2026-09-03 phase9 diagnosis): WindowDecorationMargin only ever carries a top-only
    //titlebar-height inset on macOS - this converter is the single place that turns "extension active"
    //(Top > 0) into the traffic lights' left-only clearance, so it's tested in isolation from any window.
    public class TrafficLightInsetConverterTests {
        [Fact]
        public void Convert_ExtendedTopInset_ReturnsLeftOnlyTrafficLightInset() {
            var result = TrafficLightInsetConverter.Instance.Convert(new Thickness(0, 52, 0, 0), typeof(Thickness), null, CultureInfo.InvariantCulture);

            var thickness = Assert.IsType<Thickness>(result);
            Assert.Equal(TrafficLightInsetConverter.TrafficLightInsetWidth, thickness.Left);
            Assert.Equal(0, thickness.Top);
            Assert.Equal(0, thickness.Right);
            Assert.Equal(0, thickness.Bottom);
        }

        [Fact]
        public void Convert_ZeroMargin_ReturnsZeroInset() {
            var result = TrafficLightInsetConverter.Instance.Convert(new Thickness(0, 0, 0, 0), typeof(Thickness), null, CultureInfo.InvariantCulture);

            Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(result));
        }

        [Fact]
        public void Convert_NonThicknessValue_ReturnsZeroInset() {
            var result = TrafficLightInsetConverter.Instance.Convert(null, typeof(Thickness), null, CultureInfo.InvariantCulture);

            Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(result));
        }
    }
}
