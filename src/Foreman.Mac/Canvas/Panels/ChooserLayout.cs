using System;

namespace Foreman.Mac.Canvas.Panels {
    //Ports upstream Controls/ChooserLayout.cs (docs/panels-reference.md §2 porting note): Avalonia scales
    //DPI natively, so only the 96-DPI design constants survive, as fixed logical-pixel sizes - the runtime
    //rescaling machinery (GetScaleFactor/Scale) has no port here. GroupIconSizeForCell/FooterButtonHeight
    //ForCell/FooterButtonFontSizeForCell are pure math and still portable (ForemanTest/ChooserLayoutTests.cs),
    //kept for whichever later panel first needs responsive footer/group sizing; this task's own chrome uses
    //the fixed sizes directly, since Avalonia's layout system replaces Ui.cs's manual fit-to-viewport pass.
    internal static class ChooserLayout {
        public const int CellSize = 40;
        public const int GroupIconSize = 64;
        public const int ScrollbarWidth = 17;
        public const int FilterTextWidth = 127;
        public const int QualityComboWidth = 146;
        public const int ItemIconSize = 40;
        public const int MinCellSize = 18;
        public const int MinGroupIconSize = 24;
        public const int FooterButtonHeight = 38;
        public const int MinFooterButtonHeight = 22;
        //Upstream declares these in points ("Microsoft Sans Serif", 8.25F / 6F - IRChooserPanel.Ui.cs's
        //GraphicsUnit.Point); Avalonia's FontSize is device-independent px at 96 DPI, so the point value
        //needs the standard 96/72 conversion rather than a bare copy (review finding 1: a bare copy is what
        //made the chooser's footer buttons render too small).
        public const double FooterButtonFontSize = 8.25 * 96.0 / 72.0;
        public const double MinFooterButtonFontSize = 6.0 * 96.0 / 72.0;
        public const int MinVisibleRows = 4;

        public static int ChooserWidth => ChooserIconGrid.ColumnCount * CellSize + ScrollbarWidth + 6;
        public static int GridOuterWidth => ChooserIconGrid.ColumnCount * CellSize + ScrollbarWidth;
        public static int GridOuterHeight => ChooserIconGrid.VisibleRowCount * CellSize;

        public static int GroupIconSizeForCell(int cellSize, int designGroupSize, int minGroupSize) {
            int fromCell = (int)Math.Round(cellSize * (designGroupSize / (double)CellSize));
            return Math.Min(designGroupSize, Math.Max(minGroupSize, fromCell));
        }

        public static int FooterButtonHeightForCell(int cellSize, int designFooterHeight, int minFooterHeight) {
            int fromCell = (int)Math.Round(cellSize * (designFooterHeight / (double)CellSize));
            return Math.Max(minFooterHeight, Math.Min(designFooterHeight, fromCell));
        }

        public static double FooterButtonFontSizeForCell(int cellSize, int designCellSize, double designFontSize, double minFontSize) {
            double fromCell = cellSize * (designFontSize / designCellSize);
            return Math.Max(minFontSize, Math.Min(designFontSize, fromCell));
        }
    }
}
