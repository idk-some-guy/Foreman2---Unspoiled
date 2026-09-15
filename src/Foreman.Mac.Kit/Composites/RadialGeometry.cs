using Avalonia;
using System;

namespace Foreman.Mac.Kit.Composites {
    //Pure angle/hit-test maths for Kit.RadialRing, testable without a control, window or layout pass. Angle 0 is east, increasing clockwise; slice 0 is centred north (270°) and each following slice continues clockwise by 60°.
    internal static class RadialGeometry {
        public const int SliceCount = 6;
        public const double SliceSpan = 360.0 / SliceCount;

        public static double SliceCenter(int index) => Normalize(270 + (index * SliceSpan));

        public static double SliceStart(int index) => Normalize(SliceCenter(index) - (SliceSpan / 2));

        public static double AngleOf(Point vectorFromCenter) =>
            Normalize(Math.Atan2(vectorFromCenter.Y, vectorFromCenter.X) * 180 / Math.PI);

        public static int SliceForAngle(double angleDeg) {
            double angle = Normalize(angleDeg);
            for (int i = 0; i < SliceCount; i++) {
                double offset = Normalize(angle - SliceStart(i));
                if (offset < SliceSpan)
                    return i;
            }
            return 0;
        }

        //null outside the ring (past the outer radius) or inside the hub - the polar hit test a rectangle can't express.
        public static int? HitTest(Point vectorFromCenter, double hubRadius, double outerRadius) {
            double r = Length(vectorFromCenter);
            if (r < hubRadius || r > outerRadius)
                return null;
            return SliceForAngle(AngleOf(vectorFromCenter));
        }

        public static double Length(Point vector) => Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));

        public static Point PointOnCircle(double radius, double angleDeg) {
            double rad = angleDeg * Math.PI / 180;
            return new Point(radius * Math.Cos(rad), radius * Math.Sin(rad));
        }

        private static double Normalize(double angleDeg) {
            double a = angleDeg % 360;
            return a < 0 ? a + 360 : a;
        }
    }
}
