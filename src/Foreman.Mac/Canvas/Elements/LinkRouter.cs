using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    public readonly record struct RouteSegment(SKPoint From, SKPoint To, float ArcRadius, SKPoint Corner = default);

    public readonly record struct CubicSegment(SKPoint C1, SKPoint C2, SKPoint To);

    internal static class LinkRouter {
        private const float Epsilon = 0.01f;

        internal static (float Supplier, float Consumer) StubDirections(NodeDirection supplierDirection, NodeDirection consumerDirection) =>
            (supplierDirection == NodeDirection.Up ? -1f : 1f, consumerDirection == NodeDirection.Up ? 1f : -1f);

        internal static List<SKPoint> OrthogonalPoints(Point supplier, NodeDirection supplierDirection, Point consumer, NodeDirection consumerDirection, IReadOnlyList<Point> waypoints, float stub, SKRect supplierBounds, SKRect consumerBounds) {
            (float supplierSign, float consumerSign) = StubDirections(supplierDirection, consumerDirection);
            var start = new SKPoint(supplier.X, supplier.Y);
            var end = new SKPoint(consumer.X, consumer.Y);
            var supplierStub = new SKPoint(start.X, start.Y + (supplierSign * stub));
            var consumerStub = new SKPoint(end.X, end.Y + (consumerSign * stub));

            var points = new List<SKPoint> { start, supplierStub };
            SKPoint cursor = supplierStub;
            foreach (Point waypoint in waypoints) {
                points.Add(new SKPoint(cursor.X, waypoint.Y));
                cursor = new SKPoint(waypoint.X, waypoint.Y);
                points.Add(cursor);
            }

            if (waypoints.Count == 0) {
                bool opposed = supplierSign != consumerSign;
                if (opposed && (consumerStub.Y - supplierStub.Y) * supplierSign < 0)
                    return Deduplicate(Lane(start, supplierStub, consumerStub, end, stub, supplierBounds, consumerBounds));
                if (!opposed) {
                    float farther = supplierSign > 0 ? Math.Max(supplierStub.Y, consumerStub.Y) : Math.Min(supplierStub.Y, consumerStub.Y);
                    var corner = new SKPoint(supplierStub.X, farther);
                    var other = new SKPoint(consumerStub.X, farther);
                    bool clear = !Cuts(supplierBounds, supplierStub, corner) && !Cuts(consumerBounds, supplierStub, corner)
                        && !Cuts(supplierBounds, corner, other) && !Cuts(consumerBounds, corner, other)
                        && !Cuts(supplierBounds, other, consumerStub) && !Cuts(consumerBounds, other, consumerStub);
                    return Deduplicate(clear
                        ? [start, supplierStub, corner, other, consumerStub, end]
                        : Lane(start, supplierStub, consumerStub, end, stub, supplierBounds, consumerBounds));
                }
            }

            float middleY = (cursor.Y + consumerStub.Y) / 2f;
            points.Add(new SKPoint(cursor.X, middleY));
            points.Add(new SKPoint(consumerStub.X, middleY));
            points.Add(consumerStub);
            points.Add(end);
            return Deduplicate(points);
        }

        private static List<SKPoint> Lane(SKPoint start, SKPoint supplierStub, SKPoint consumerStub, SKPoint end, float stub, SKRect supplierBounds, SKRect consumerBounds) {
            float clearance = 2 * stub;
            float lane;
            if (supplierBounds.Right <= consumerBounds.Left && consumerBounds.Left - supplierBounds.Right >= clearance)
                lane = (supplierBounds.Right + consumerBounds.Left) / 2f;
            else if (consumerBounds.Right <= supplierBounds.Left && supplierBounds.Left - consumerBounds.Right >= clearance)
                lane = (consumerBounds.Right + supplierBounds.Left) / 2f;
            else {
                float right = Math.Max(supplierBounds.Right, consumerBounds.Right) + clearance;
                float left = Math.Min(supplierBounds.Left, consumerBounds.Left) - clearance;
                float viaRight = Math.Abs(supplierStub.X - right) + Math.Abs(consumerStub.X - right);
                float viaLeft = Math.Abs(supplierStub.X - left) + Math.Abs(consumerStub.X - left);
                lane = viaRight <= viaLeft ? right : left;
            }
            return [start, supplierStub, new SKPoint(lane, supplierStub.Y), new SKPoint(lane, consumerStub.Y), consumerStub, end];
        }

        private static bool Cuts(SKRect rect, SKPoint a, SKPoint b) {
            if (Math.Abs(a.X - b.X) < Epsilon)
                return a.X > rect.Left + Epsilon && a.X < rect.Right - Epsilon && Math.Max(Math.Min(a.Y, b.Y), rect.Top) < Math.Min(Math.Max(a.Y, b.Y), rect.Bottom) - Epsilon;
            return a.Y > rect.Top + Epsilon && a.Y < rect.Bottom - Epsilon && Math.Max(Math.Min(a.X, b.X), rect.Left) < Math.Min(Math.Max(a.X, b.X), rect.Right) - Epsilon;
        }

        internal static List<RouteSegment> OrthogonalSegments(IReadOnlyList<SKPoint> points, float cornerRadius) {
            var segments = new List<RouteSegment>();
            SKPoint cursor = points[0];
            for (int i = 1; i < points.Count - 1; i++) {
                SKPoint previous = points[i - 1], corner = points[i], next = points[i + 1];
                SKPoint incoming = Unit(previous, corner), outgoing = Unit(corner, next);
                bool turns = Math.Abs((incoming.X * outgoing.Y) - (incoming.Y * outgoing.X)) > 0.5f;
                float radius = turns ? Math.Min(cornerRadius, Math.Min(Distance(previous, corner), Distance(corner, next)) / 2f) : 0f;
                if (radius <= Epsilon) {
                    AddRun(segments, cursor, corner);
                    cursor = corner;
                    continue;
                }

                var entry = new SKPoint(corner.X - (incoming.X * radius), corner.Y - (incoming.Y * radius));
                var exit = new SKPoint(corner.X + (outgoing.X * radius), corner.Y + (outgoing.Y * radius));
                AddRun(segments, cursor, entry);
                segments.Add(new RouteSegment(entry, exit, radius, corner));
                cursor = exit;
            }
            AddRun(segments, cursor, points[^1]);
            return segments;
        }

        internal static SKPath ToPath(SKPath path, IReadOnlyList<SKPoint> points, float cornerRadius) {
            List<RouteSegment> segments = OrthogonalSegments(points, cornerRadius);
            path.MoveTo(points[0]);
            foreach (RouteSegment segment in segments) {
                if (segment.ArcRadius <= 0) {
                    path.LineTo(segment.To);
                    continue;
                }

                path.LineTo(segment.From);
                path.ArcTo(segment.Corner, segment.To, segment.ArcRadius);
            }
            return path;
        }

        internal static List<CubicSegment> CatmullRom(Point supplier, NodeDirection supplierDirection, Point consumer, NodeDirection consumerDirection, IReadOnlyList<Point> waypoints) {
            (float supplierSign, float consumerSign) = StubDirections(supplierDirection, consumerDirection);
            var points = new List<SKPoint> { new(supplier.X, supplier.Y) };
            foreach (Point waypoint in waypoints)
                points.Add(new SKPoint(waypoint.X, waypoint.Y));
            points.Add(new SKPoint(consumer.X, consumer.Y));

            var tangents = new SKPoint[points.Count];
            for (int i = 1; i < points.Count - 1; i++)
                tangents[i] = new SKPoint((points[i + 1].X - points[i - 1].X) / 2f, (points[i + 1].Y - points[i - 1].Y) / 2f);
            tangents[0] = new SKPoint(0, supplierSign * Distance(points[0], points[1]));
            tangents[^1] = new SKPoint(0, -consumerSign * Distance(points[^2], points[^1]));

            var cubics = new List<CubicSegment>();
            for (int i = 0; i < points.Count - 1; i++) {
                cubics.Add(new CubicSegment(
                    new SKPoint(points[i].X + (tangents[i].X / 3f), points[i].Y + (tangents[i].Y / 3f)),
                    new SKPoint(points[i + 1].X - (tangents[i + 1].X / 3f), points[i + 1].Y - (tangents[i + 1].Y / 3f)),
                    points[i + 1]));
            }
            return cubics;
        }

        internal static SKPath ToPath(SKPath path, SKPoint start, IReadOnlyList<CubicSegment> cubics) {
            path.MoveTo(start);
            foreach (CubicSegment cubic in cubics)
                path.CubicTo(cubic.C1, cubic.C2, cubic.To);
            return path;
        }

        internal static List<SKPoint> Flatten(SKPath path, float step = 2f) {
            var points = new List<SKPoint>();
            using var measure = new SKPathMeasure(path, false);
            do {
                float length = measure.Length;
                for (float distance = 0; distance < length; distance += step) {
                    if (measure.GetPosition(distance, out SKPoint point))
                        points.Add(point);
                }
                if (measure.GetPosition(length, out SKPoint last))
                    points.Add(last);
            } while (measure.NextContour());
            return Deduplicate(points);
        }

        internal static float DistanceToPolyline(IReadOnlyList<SKPoint> polyline, SKPoint point) {
            if (polyline.Count == 0)
                return float.MaxValue;
            if (polyline.Count == 1)
                return Distance(polyline[0], point);

            float best = float.MaxValue;
            for (int i = 0; i < polyline.Count - 1; i++)
                best = Math.Min(best, Distance(Closest(polyline[i], polyline[i + 1], point), point));
            return best;
        }

        internal static (SKPoint Point, float AlongLength) NearestOnPolyline(IReadOnlyList<SKPoint> polyline, SKPoint point) {
            SKPoint bestPoint = polyline.Count > 0 ? polyline[0] : point;
            float bestDistance = float.MaxValue, bestAlong = 0, walked = 0;
            for (int i = 0; i < polyline.Count - 1; i++) {
                SKPoint closest = Closest(polyline[i], polyline[i + 1], point);
                float distance = Distance(closest, point);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    bestPoint = closest;
                    bestAlong = walked + Distance(polyline[i], closest);
                }
                walked += Distance(polyline[i], polyline[i + 1]);
            }
            return (bestPoint, bestAlong);
        }

        internal static float AlongLength(IReadOnlyList<SKPoint> polyline, SKPoint point) => NearestOnPolyline(polyline, point).AlongLength;

        internal static SKPoint Closest(SKPoint a, SKPoint b, SKPoint point) {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lengthSquared = (dx * dx) + (dy * dy);
            if (lengthSquared < 1e-9f)
                return a;

            float t = Math.Clamp((((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / lengthSquared, 0f, 1f);
            return new SKPoint(a.X + (t * dx), a.Y + (t * dy));
        }

        internal static float Distance(SKPoint a, SKPoint b) {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static SKPoint Unit(SKPoint from, SKPoint to) {
            float length = Distance(from, to);
            return length < 1e-6f ? new SKPoint(0, 0) : new SKPoint((to.X - from.X) / length, (to.Y - from.Y) / length);
        }

        private static void AddRun(List<RouteSegment> segments, SKPoint from, SKPoint to) {
            if (Distance(from, to) > Epsilon)
                segments.Add(new RouteSegment(from, to, 0));
        }

        private static List<SKPoint> Deduplicate(List<SKPoint> points) {
            var result = new List<SKPoint>(points.Count);
            foreach (SKPoint point in points) {
                if (result.Count == 0 || Distance(result[^1], point) > Epsilon)
                    result.Add(point);
            }
            return result;
        }
    }
}
