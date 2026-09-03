using Avalonia.Headless.XUnit;
using Foreman.Mac.Canvas.Panels;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Ports the portable cases from upstream ForemanTest/ChooserLayoutTests.cs (docs/panels-reference.md §9
    //step 2's brief: bring the layout-math cases that don't depend on WinForms). GroupIconSizeForCell/
    //FooterButtonHeightForCell/FooterButtonFontSizeForCell are pure functions with no Control dependency, so
    //every case ports as-is; the SystemInformation.VerticalScrollBarWidth/live-ItemChooserPanel/FlowLayoutPanel
    //cases stay behind (reference §2's DPI porting note, plus ItemChooserPanel itself is Task 3).
    public class ChooserLayoutTests {
        [Fact]
        public void GroupIconSizeForCell_MatchesDesignRatioAtFullCell() =>
            Assert.Equal(64, ChooserLayout.GroupIconSizeForCell(40, 64, 24));

        [Fact]
        public void GroupIconSizeForCell_ScalesDownWithCell() =>
            Assert.Equal(32, ChooserLayout.GroupIconSizeForCell(20, 64, 24));

        [Fact]
        public void GroupIconSizeForCell_ClampsToMinimum() =>
            Assert.Equal(24, ChooserLayout.GroupIconSizeForCell(10, 64, 24));

        [Fact]
        public void GroupIconSizeForCell_DoesNotExceedDesignGroup() =>
            Assert.Equal(64, ChooserLayout.GroupIconSizeForCell(100, 64, 24));

        [Fact]
        public void FooterButtonHeightForCell_MatchesDesignRatioAtFullCell() =>
            Assert.Equal(38, ChooserLayout.FooterButtonHeightForCell(40, 38, 22));

        [Fact]
        public void FooterButtonHeightForCell_ScalesDownWithCell() =>
            Assert.Equal(28, ChooserLayout.FooterButtonHeightForCell(30, 38, 22));

        [Fact]
        public void FooterButtonHeightForCell_ClampsToMinimum() =>
            Assert.Equal(22, ChooserLayout.FooterButtonHeightForCell(10, 38, 22));

        [Fact]
        public void FooterButtonHeightForCell_DoesNotExceedDesignHeight() =>
            Assert.Equal(38, ChooserLayout.FooterButtonHeightForCell(100, 38, 22));

        [Fact]
        public void FooterButtonFontSizeForCell_MatchesDesignRatioAtFullCell() =>
            Assert.Equal(8.25, ChooserLayout.FooterButtonFontSizeForCell(40, 40, 8.25, 6), 0.01);

        [Fact]
        public void FooterButtonFontSizeForCell_ScalesDownWithCell() =>
            Assert.Equal(6.1875, ChooserLayout.FooterButtonFontSizeForCell(30, 40, 8.25, 6), 0.01);

        [Fact]
        public void FooterButtonFontSizeForCell_ClampsToMinimum() =>
            Assert.Equal(6, ChooserLayout.FooterButtonFontSizeForCell(10, 40, 8.25, 6), 0.01);

        //Review finding 1: upstream declares these in points (IRChooserPanel.Ui.cs's GraphicsUnit.Point) -
        //Avalonia's FontSize is device-independent px at 96 DPI, so the constants need the 96/72 conversion
        //rather than the bare point values, which is what made the chooser's footer buttons render too small.
        [Fact]
        public void FooterButtonFontSize_IsConvertedFromPointsToAvaloniaPixels() {
            Assert.Equal(8.25 * 96.0 / 72.0, ChooserLayout.FooterButtonFontSize, 3);
            Assert.Equal(6.0 * 96.0 / 72.0, ChooserLayout.MinFooterButtonFontSize, 3);
        }

        [AvaloniaFact]
        public void ApplyLayout_SizesGridToCellCount() {
            var grid = new ChooserIconGrid();
            int outerWidth = grid.ApplyLayout(
                availableGridHeight: ChooserIconGrid.VisibleRowCount * 40,
                maxLayoutWidth: ChooserIconGrid.ColumnCount * 40 + ChooserLayout.ScrollbarWidth,
                designCellSize: 40,
                minCellSize: 18,
                scrollbarWidth: ChooserLayout.ScrollbarWidth);

            Assert.Equal(40, grid.TargetCellSize);
            Assert.Equal(40 * ChooserIconGrid.VisibleRowCount, grid.Height);
            Assert.Equal(40 * ChooserIconGrid.ColumnCount + ChooserLayout.ScrollbarWidth, grid.Width);
            Assert.Equal(outerWidth, grid.Width);
            Assert.Equal(40, grid.Buttons[0][0].Width);
            Assert.Equal(40, grid.Buttons[0][0].Height);
        }

        [AvaloniaFact]
        public void ApplyLayout_RoundsUpToMinOuterWidth() {
            var grid = new ChooserIconGrid();
            int outerWidth = grid.ApplyLayout(
                availableGridHeight: ChooserIconGrid.VisibleRowCount * 40,
                maxLayoutWidth: 270,
                designCellSize: 40,
                minCellSize: 18,
                scrollbarWidth: ChooserLayout.ScrollbarWidth,
                minOuterWidth: 251);

            Assert.True(outerWidth >= 251, $"Grid outer width {outerWidth} should meet chrome minimums that do not land on a cell boundary.");
            Assert.True(outerWidth <= 270);
        }

        [AvaloniaFact]
        public void ApplyLayout_ShrinksWhenHeightLimited() {
            var grid = new ChooserIconGrid();
            const int cell = 20;
            grid.ApplyLayout(
                availableGridHeight: ChooserIconGrid.VisibleRowCount * cell,
                maxLayoutWidth: 500,
                designCellSize: 40,
                minCellSize: 18,
                scrollbarWidth: ChooserLayout.ScrollbarWidth);

            Assert.Equal(cell, grid.TargetCellSize);
            Assert.Equal(cell * ChooserIconGrid.VisibleRowCount, grid.Height);
            Assert.Equal(cell * ChooserIconGrid.ColumnCount + ChooserLayout.ScrollbarWidth, grid.Width);
        }
    }
}
