using SkiaSharp;

namespace Foreman.Mac.Canvas.Elements {
    //Rect is the stroke's centre line.
    public readonly record struct RingGeometry(SKRect Rect, float Radius, float StrokeWidth, SKColor Color, float[]? Dash);

    public enum NodeBadgeKind { Unavailable, Error, Undersupplied, Warning, Over }

    public readonly record struct NodeBadge(NodeBadgeKind Kind, string Text);

    internal enum NodeSolveState { Solved, Stale, NeverSolved }
}
