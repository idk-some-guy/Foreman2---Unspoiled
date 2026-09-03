using SkiaSharp;
using System;
using System.Drawing;

namespace Foreman.Mac.Canvas {
    //Ports ProductionGraphView/GridManager.cs. Pens/brush are per-instance cached SKPaints whose Width is
    //mutated per frame (2/viewScale etc.) so strokes stay a constant screen thickness under the view-scale
    //transform, matching upstream's one preserved per-frame-mutable pattern.
    //
    //CurrentGridUnit doesn't default from AppSettings.MinorGridlines here (deferred to the toolbar-wiring
    //task); the fresh-install default is 0 either way, so this matches upstream's own cold-start behavior.
    //
    //Fix-round item 2 (task-2-report.md): these paints used to be static, shared across every GridManager.
    //ImageExportWindow's own GridManager is never actually reachable from Paint (fullGraph:true skips
    //Grid.Paint entirely - GraphViewer.Paint's `if (!fullGraph) Grid.Paint(...)` guard), so a shared static
    //here was never racing across threads. The real benefit of per-instance fields is GalleryWindow: it
    //opens its own GraphViewer/GridManager pair on the same UI thread as the main canvas, and a shared
    //static's mutated StrokeWidth (ViewScale-dependent) would have bled between the two viewers' own grids
    //the moment their ViewScales diverged, even though nothing there is concurrent.
    public sealed class GridManager : IDisposable {
        private const int MinGridWidth = 6;

        public int CurrentGridUnit { get; set; }
        public int CurrentMajorGridUnit { get; set; }
        public bool ShowGrid { get; set; }
        public bool LockDragToAxis { get; set; }
        public bool ShowZeroAxis { get; set; }
        public Point DragOrigin { get; set; }

        private readonly SKPaint _gridPaint = new() { Color = new SKColor(230, 230, 230), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        private readonly SKPaint _gridMajorPaint = new() { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        private readonly SKPaint _gridFillPaint = new() { Color = new SKColor(240, 240, 240), Style = SKPaintStyle.Fill };
        private readonly SKPaint _zeroAxisPaint = new() { Color = new SKColor(140, 140, 140), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        private readonly SKPaint _lockedAxisPaint = new() { Color = new SKColor(180, 80, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 4 };

        //Ports GridManager.SetGridColors (ProductionGraphView/GridManager.cs:24-27), called from MainForm's
        //dark-mode ChangeTheme walk whenever it reaches the ProductionGraphViewer child. bg recolors the
        //fallback fill used when zoomed out too far for gridlines (_gridFillPaint); fg recolors the major
        //gridline pen (_gridMajorPaint). The minor gridline/zero-axis/locked-axis paints stay fixed, matching
        //upstream's own gridPen/zeroAxisPen/lockedAxisPen staying `readonly`.
        public void SetGridColors(SKColor bg, SKColor fg) {
            _gridFillPaint.Color = bg;
            _gridMajorPaint.Color = fg;
        }

        public void Paint(SKCanvas canvas, float viewScale, Rectangle visibleGraphBounds, bool draggedNodeActive = false) {
            if (ShowGrid) {
                _gridPaint.StrokeWidth = 1 / viewScale;
                _gridMajorPaint.StrokeWidth = 1 / viewScale;
                _zeroAxisPaint.StrokeWidth = 2 / viewScale;
                _lockedAxisPaint.StrokeWidth = 3 / viewScale;

                if (CurrentGridUnit > 0) {
                    if (visibleGraphBounds.Width > CurrentGridUnit && viewScale * CurrentGridUnit > MinGridWidth) {
                        for (int ix = visibleGraphBounds.X - (visibleGraphBounds.X % CurrentGridUnit); ix < visibleGraphBounds.X + visibleGraphBounds.Width; ix += CurrentGridUnit)
                            canvas.DrawLine(ix, visibleGraphBounds.Y, ix, visibleGraphBounds.Y + visibleGraphBounds.Height, _gridPaint);

                        for (int iy = visibleGraphBounds.Y - (visibleGraphBounds.Y % CurrentGridUnit); iy < visibleGraphBounds.Y + visibleGraphBounds.Height; iy += CurrentGridUnit)
                            canvas.DrawLine(visibleGraphBounds.X, iy, visibleGraphBounds.X + visibleGraphBounds.Width, iy, _gridPaint);
                    } else {
                        canvas.DrawRect(SKRect.Create(visibleGraphBounds.X, visibleGraphBounds.Y, visibleGraphBounds.Width, visibleGraphBounds.Height), _gridFillPaint);
                    }
                }

                if (CurrentMajorGridUnit > CurrentGridUnit) {
                    if (visibleGraphBounds.Width > CurrentMajorGridUnit && viewScale * CurrentMajorGridUnit > MinGridWidth) {
                        for (int ix = visibleGraphBounds.X - (visibleGraphBounds.X % CurrentMajorGridUnit); ix < visibleGraphBounds.X + visibleGraphBounds.Width; ix += CurrentMajorGridUnit)
                            canvas.DrawLine(ix, visibleGraphBounds.Y, ix, visibleGraphBounds.Y + visibleGraphBounds.Height, _gridMajorPaint);

                        for (int iy = visibleGraphBounds.Y - (visibleGraphBounds.Y % CurrentMajorGridUnit); iy < visibleGraphBounds.Y + visibleGraphBounds.Height; iy += CurrentMajorGridUnit)
                            canvas.DrawLine(visibleGraphBounds.X, iy, visibleGraphBounds.X + visibleGraphBounds.Width, iy, _gridMajorPaint);
                    }
                }

                if (ShowZeroAxis) {
                    canvas.DrawLine(0, visibleGraphBounds.Y, 0, visibleGraphBounds.Y + visibleGraphBounds.Height, _zeroAxisPaint);
                    canvas.DrawLine(visibleGraphBounds.X, 0, visibleGraphBounds.X + visibleGraphBounds.Width, 0, _zeroAxisPaint);
                }
            }

            if (LockDragToAxis && draggedNodeActive) {
                int xaxis = AlignToGrid(DragOrigin.X);
                int yaxis = AlignToGrid(DragOrigin.Y);

                canvas.DrawLine(xaxis, visibleGraphBounds.Y, xaxis, visibleGraphBounds.Y + visibleGraphBounds.Height, _lockedAxisPaint);
                canvas.DrawLine(visibleGraphBounds.X, yaxis, visibleGraphBounds.X + visibleGraphBounds.Width, yaxis, _lockedAxisPaint);
            }
        }

        public void Dispose() {
            _gridPaint.Dispose();
            _gridMajorPaint.Dispose();
            _gridFillPaint.Dispose();
            _zeroAxisPaint.Dispose();
            _lockedAxisPaint.Dispose();
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
    }
}
