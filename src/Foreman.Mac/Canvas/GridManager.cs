using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System;
using System.Drawing;

namespace Foreman.Mac.Canvas {
    public sealed class GridManager : IDisposable {
        private const float MinorDotDiameter = 1;
        private const float MajorDotDiameter = 2;
        private const float GuideDash = 4;

        public int CurrentGridUnit { get; set; }
        public int CurrentMajorGridUnit { get; set; }
        public bool ShowGrid { get; set; }
        public bool ShowOriginAxes { get; set; }
        public bool LockDragToAxis { get; set; }
        public Point DragOrigin { get; set; }

        private readonly SKPaint _minorDotPaint = new() { Style = SKPaintStyle.Fill };
        private readonly SKPaint _majorDotPaint = new() { Style = SKPaintStyle.Fill };
        private readonly SKPaint _originAxisPaint = new() { Style = SKPaintStyle.Stroke };
        private readonly SKPaint _axisGuidePaint = new() { Style = SKPaintStyle.Stroke };
        private readonly DotTile _minorTile = new();
        private readonly DotTile _majorTile = new();
        private float _guideScale;

        internal SKPaint MinorDotPaint => Synced(_minorDotPaint);
        internal SKPaint MajorDotPaint => Synced(_majorDotPaint);
        internal SKPaint AxisGuidePaint => _axisGuidePaint;
        internal int MinorTileSide => _minorTile.Side;
        internal int MajorTileSide => _majorTile.Side;

        private SKPaint Synced(SKPaint paint) {
            Recolor(TokenBridge.Current);
            return paint;
        }

        private void Recolor(CanvasTokens tokens) {
            _minorDotPaint.Color = tokens.IncreaseContrast ? tokens.BorderStrong : tokens.Border;
            _majorDotPaint.Color = tokens.IncreaseContrast ? tokens.NodeTabBorder : tokens.BorderStrong;
            _originAxisPaint.Color = tokens.BorderStrong;
            _axisGuidePaint.Color = tokens.Accent;
        }

        public void Paint(SKCanvas canvas, float viewScale, Rectangle visibleGraphBounds, bool draggedNodeActive = false) {
            CanvasTokens tokens = TokenBridge.Current;
            Recolor(tokens);

            if (ShowGrid) {
                float density = PixelsPerUnit(canvas, viewScale);
                bool minor = DotsShow(CurrentGridUnit, viewScale, tokens);
                bool major = CurrentMajorGridUnit > CurrentGridUnit && DotsShow(CurrentMajorGridUnit, viewScale, tokens);
                var minorLevel = new DotLevel(CurrentGridUnit, MinorDotDiameter, _minorDotPaint.Color);
                var majorLevel = new DotLevel(CurrentMajorGridUnit, MajorDotDiameter, _majorDotPaint.Color);
                if (minor && major && CurrentMajorGridUnit % CurrentGridUnit == 0)
                    DrawDots(canvas, _minorTile, viewScale, density, visibleGraphBounds, minorLevel, majorLevel);
                else {
                    if (minor)
                        DrawDots(canvas, _minorTile, viewScale, density, visibleGraphBounds, minorLevel);
                    if (major)
                        DrawDots(canvas, _majorTile, viewScale, density, visibleGraphBounds, majorLevel);
                }

                if (ShowOriginAxes) {
                    _originAxisPaint.StrokeWidth = 1 / viewScale;
                    canvas.DrawLine(0, visibleGraphBounds.Y, 0, visibleGraphBounds.Y + visibleGraphBounds.Height, _originAxisPaint);
                    canvas.DrawLine(visibleGraphBounds.X, 0, visibleGraphBounds.X + visibleGraphBounds.Width, 0, _originAxisPaint);
                }
            }

            if (LockDragToAxis && draggedNodeActive) {
                SetGuideScale(viewScale);
                int xaxis = AlignToGrid(DragOrigin.X);
                int yaxis = AlignToGrid(DragOrigin.Y);

                canvas.DrawLine(xaxis, visibleGraphBounds.Y, xaxis, visibleGraphBounds.Y + visibleGraphBounds.Height, _axisGuidePaint);
                canvas.DrawLine(visibleGraphBounds.X, yaxis, visibleGraphBounds.X + visibleGraphBounds.Width, yaxis, _axisGuidePaint);
            }
        }

        private static bool DotsShow(int unit, float viewScale, CanvasTokens tokens) =>
            unit > 0 && viewScale * unit >= tokens.GridMinScreenSpacing;

        private static float PixelsPerUnit(SKCanvas canvas, float viewScale) {
            float scale = Math.Abs(canvas.TotalMatrix.ScaleX);
            return scale > 0 ? scale : viewScale;
        }

        private static void DrawDots(SKCanvas canvas, DotTile tile, float viewScale, float density, Rectangle bounds, params ReadOnlySpan<DotLevel> levels) {
            tile.Prepare(viewScale, density, levels);
            canvas.DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, tile.Paint);
        }

        private void SetGuideScale(float viewScale) {
            _axisGuidePaint.StrokeWidth = 1 / viewScale;
            if (_guideScale == viewScale && _axisGuidePaint.PathEffect is not null)
                return;
            _guideScale = viewScale;
            _axisGuidePaint.PathEffect?.Dispose();
            _axisGuidePaint.PathEffect = SKPathEffect.CreateDash([GuideDash / viewScale, GuideDash / viewScale], 0);
        }

        public void Dispose() {
            _minorTile.Dispose();
            _majorTile.Dispose();
            _minorDotPaint.Dispose();
            _majorDotPaint.Dispose();
            _originAxisPaint.Dispose();
            _axisGuidePaint.PathEffect?.Dispose();
            _axisGuidePaint.Dispose();
            GC.SuppressFinalize(this);
        }

        public Point AlignToGrid(Point original) => new(AlignToGrid(original.X), AlignToGrid(original.Y));

        public int AlignToGrid(int original) {
            if (CurrentGridUnit < 1 || !ShowGrid)
                return original;

            original += Math.Sign(original) * CurrentGridUnit / 2;
            original -= original % CurrentGridUnit;
            return original;
        }

        private readonly record struct DotLevel(int Unit, float Diameter, SKColor Color);

        private sealed class DotTile : IDisposable {
            private SKImage? _image;
            private float _viewScale;
            private float _density;
            private DotLevel _first;
            private DotLevel _second;
            private int _count;

            public SKPaint Paint { get; } = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
            public int Side { get; private set; }

            public void Prepare(float viewScale, float density, ReadOnlySpan<DotLevel> levels) {
                DotLevel first = levels[0];
                DotLevel second = levels.Length > 1 ? levels[1] : default;
                if (_image is not null && _viewScale == viewScale && _density == density && _count == levels.Length && _first == first && _second == second)
                    return;

                _viewScale = viewScale;
                _density = density;
                _first = first;
                _second = second;
                _count = levels.Length;
                Release();

                int period = levels[^1].Unit;
                int side = Math.Max(1, (int)Math.Ceiling(period * density));
                Side = side;
                float pxPerUnit = side / (float)period;
                using (var surface = SKSurface.Create(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul))) {
                    using var dot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                    foreach (DotLevel level in levels) {
                        dot.Color = level.Color;
                        float radius = level.Diameter / 2 * pxPerUnit / viewScale;
                        float step = level.Unit * pxPerUnit;
                        int cells = period / level.Unit;
                        // Dots on the tile edge stitch their quarters together when the tile repeats
                        for (int row = 0; row <= cells; row++)
                            for (int column = 0; column <= cells; column++)
                                surface.Canvas.DrawCircle(column * step, row * step, radius, dot);
                    }
                    _image = surface.Snapshot();
                }

                Paint.Shader = _image.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), SKMatrix.CreateScale(1 / pxPerUnit, 1 / pxPerUnit));
            }

            private void Release() {
                Paint.Shader?.Dispose();
                Paint.Shader = null;
                _image?.Dispose();
                _image = null;
            }

            public void Dispose() {
                Release();
                Paint.Dispose();
            }
        }
    }
}
