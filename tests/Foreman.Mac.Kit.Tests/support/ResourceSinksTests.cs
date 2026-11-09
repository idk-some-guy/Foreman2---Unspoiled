using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Foreman.Mac.Kit;
using Xunit;

namespace Foreman.Mac.Kit.Tests.support {
    public class ResourceSinksTests {
        [Fact]
        public void DoubleSink_SuppressesSameValue_ForwardsChangedValue() {
            int calls = 0;
            var sink = new ResourceSinks.DoubleSink(_ => calls++, 12.0);

            sink.OnNext(12.0);
            Assert.Equal(0, calls);

            sink.OnNext(18.0);
            Assert.Equal(1, calls);
        }

        [AvaloniaFact]
        public void BrushSink_SuppressesSameColor_ForwardsChangedColor() {
            int calls = 0;
            var sink = new ResourceSinks.BrushSink(_ => calls++, new SolidColorBrush(Colors.Red));

            sink.OnNext(Colors.Red);
            Assert.Equal(0, calls);

            sink.OnNext(Colors.Blue);
            Assert.Equal(1, calls);
        }

        [AvaloniaFact]
        public void WeightSink_SuppressesSameWeight_ForwardsChangedWeight() {
            int calls = 0;
            var sink = new ResourceSinks.WeightSink(_ => calls++, FontWeight.Normal);

            sink.OnNext(FontWeight.Normal);
            Assert.Equal(0, calls);

            sink.OnNext(FontWeight.Bold);
            Assert.Equal(1, calls);
        }

        [AvaloniaFact]
        public void RadiusSink_SuppressesSameRadius_ForwardsChangedRadius() {
            int calls = 0;
            var sink = new ResourceSinks.RadiusSink(_ => calls++, new CornerRadius(4));

            sink.OnNext(new CornerRadius(4));
            Assert.Equal(0, calls);

            sink.OnNext(new CornerRadius(6));
            Assert.Equal(1, calls);
        }

        [AvaloniaFact]
        public void ThicknessSink_SuppressesSameThickness_ForwardsChangedThickness() {
            int calls = 0;
            var sink = new ResourceSinks.ThicknessSink(_ => calls++, new Thickness(4, 0));

            sink.OnNext(new Thickness(4, 0));
            Assert.Equal(0, calls);

            sink.OnNext(new Thickness(6, 0));
            Assert.Equal(1, calls);
        }

        [AvaloniaFact]
        public void GenericSink_SuppressesSameGeometry_ForwardsChangedGeometry() {
            int calls = 0;
            Geometry? a = Geometry.Parse("M0,0 L1,1");
            Geometry? b = Geometry.Parse("M0,0 L2,2");
            var sink = new ResourceSinks.Sink<Geometry?>(_ => calls++, a);

            sink.OnNext(a);
            Assert.Equal(0, calls);

            sink.OnNext(b);
            Assert.Equal(1, calls);
        }
    }
}
