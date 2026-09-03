using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using SkiaSharp;
using System;
using System.Collections.Generic;
using AvaloniaCanvas = Avalonia.Controls.Canvas;

namespace Foreman.Mac.Canvas.Panels {
    //Ports Controls/ChooserIconGrid.cs (docs/panels-reference.md §2/§9 step 2): the 10x8 fixed grid plus a
    //real vertical scrollbar. ApplyLayout/LayoutCells are a verbatim port of the upstream cell-size clamp
    //math and per-cell bounds assignment, just against Canvas.SetLeft/Top instead of WinForms Control.Bounds;
    //SetBoundsCore's "pin the control to its last-laid-out size" guard has no port, since nothing here calls
    //an Avalonia parent-layout pass that could resize us out from under the grid the way WinForms flow layout
    //could - MinWidth/MaxWidth/MinHeight/MaxHeight pin the size instead, for whatever partial credit Avalonia's
    //own measure pass gives it.
    public sealed class ChooserIconGrid : AvaloniaCanvas {
        public const int ColumnCount = 10;
        public const int VisibleRowCount = 8;

        private readonly AvaloniaCanvas gridSurface = new();
        private readonly ScrollBar scrollBar = new() {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 0,
            SmallChange = 1,
            LargeChange = VisibleRowCount,
            //Maximum is set per-update to the effective scroll bound (row count - visible rows); pairing a
            //fixed ViewportSize of one page here keeps the thumb's rendered proportion (viewport / (range +
            //viewport)) matching the real row count, rather than Avalonia's default zero-viewport thumb.
            ViewportSize = VisibleRowCount,
            IsEnabled = false,
        };
        private readonly IconButton[][] buttons;

        public int TargetCellSize { get; private set; } = ChooserLayout.CellSize;
        public IReadOnlyList<IReadOnlyList<IconButton>> Buttons => buttons;
        public ScrollBar ScrollBar => scrollBar;

        public ChooserIconGrid() {
            buttons = new IconButton[ColumnCount][];
            for (int column = 0; column < ColumnCount; column++) {
                buttons[column] = new IconButton[VisibleRowCount];
                for (int row = 0; row < VisibleRowCount; row++) {
                    var button = new IconButton();
                    button.SetEmpty();
                    buttons[column][row] = button;
                    gridSurface.Children.Add(button);
                }
            }
            Children.Add(gridSurface);
            Children.Add(scrollBar);
        }

        public void WireMouseWheel(EventHandler<PointerWheelEventArgs> handler) => gridSurface.PointerWheelChanged += handler;

        //Plain SKCanvas walk for offscreen rendering (IRChooserPanel.RenderOffscreen) - reuses the exact
        //cell positions ApplyLayout/LayoutCells already computed, no Avalonia render pass needed.
        public void PaintOnto(SKCanvas canvas, float originX, float originY) {
            int cellSize = TargetCellSize;
            for (int row = 0; row < VisibleRowCount; row++) {
                for (int column = 0; column < ColumnCount; column++) {
                    float x = originX + column * cellSize;
                    float y = originY + row * cellSize;
                    buttons[column][row].PaintOnto(canvas, new SKRect(x, y, x + cellSize, y + cellSize));
                }
            }
        }

        //Sizes the grid to fit the allotted area; returns the outer width (cells + scrollbar).
        public int ApplyLayout(int availableGridHeight, int maxLayoutWidth, int designCellSize, int minCellSize, int scrollbarWidth, int minOuterWidth = 0) {
            int minGridHeight = minCellSize * VisibleRowCount;
            int cellByHeight = Math.Max(1, availableGridHeight / VisibleRowCount);
            int cellByWidth = Math.Max(1, (maxLayoutWidth - scrollbarWidth) / ColumnCount);
            int cell = Math.Min(designCellSize, Math.Min(cellByHeight, cellByWidth));
            cell = availableGridHeight >= minGridHeight ? Math.Max(minCellSize, cell) : Math.Max(1, cell);

            if (minOuterWidth > 0) {
                int cellForMinOuter = (int)Math.Ceiling((minOuterWidth - scrollbarWidth) / (double)ColumnCount);
                cellForMinOuter = Math.Max(minCellSize, Math.Min(designCellSize, cellForMinOuter));
                if (cellForMinOuter * ColumnCount + scrollbarWidth <= maxLayoutWidth) {
                    int cellByHeightCap = Math.Max(1, availableGridHeight / VisibleRowCount);
                    cell = Math.Max(cell, Math.Min(cellForMinOuter, cellByHeightCap));
                }
            }

            TargetCellSize = cell;
            int gridHeight = cell * VisibleRowCount;
            int gridWidth = cell * ColumnCount;
            int outerWidth = gridWidth + scrollbarWidth;

            Width = outerWidth;
            Height = gridHeight;
            MinWidth = outerWidth;
            MaxWidth = outerWidth;
            MinHeight = gridHeight;
            MaxHeight = gridHeight;

            ApplyCellGridBounds(gridWidth, gridHeight, scrollbarWidth);
            return outerWidth;
        }

        private void ApplyCellGridBounds(int gridWidth, int gridHeight, int scrollbarWidth) {
            SetLeft(gridSurface, 0);
            SetTop(gridSurface, 0);
            gridSurface.Width = gridWidth;
            gridSurface.Height = gridHeight;

            scrollBar.Width = scrollbarWidth;
            scrollBar.Height = gridHeight;
            SetLeft(scrollBar, gridWidth);
            SetTop(scrollBar, 0);

            LayoutCells();
        }

        private void LayoutCells() {
            int cellSize = TargetCellSize;
            if (cellSize < 1)
                return;

            for (int row = 0; row < VisibleRowCount; row++) {
                for (int column = 0; column < ColumnCount; column++) {
                    IconButton btn = buttons[column][row];
                    btn.Width = cellSize;
                    btn.Height = cellSize;
                    AvaloniaCanvas.SetLeft(btn, column * cellSize);
                    AvaloniaCanvas.SetTop(btn, row * cellSize);
                }
            }
        }
    }
}
