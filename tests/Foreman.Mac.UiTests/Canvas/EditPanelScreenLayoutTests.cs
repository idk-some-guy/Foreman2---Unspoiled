using Foreman.Mac.Canvas.Panels;
using System.Drawing;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Ports upstream/ForemanTest/EditPanelScreenLayoutTests.cs verbatim (docs/panels-reference.md §7): the
    //math this exercises has no WinForms coupling, so it carries over as the layout's parity anchor.
    public class EditPanelScreenLayoutTests {
        private const int ViewerW = 1200;
        private const int ViewerH = 800;
        private const int Margin = EditPanelScreenLayout.DefaultMargin;

        [Fact]
        public void ClampRectToViewer_KeepsBoundsInsideViewer() {
            var offBottom = new Rectangle(100, 900, 472, 689);
            Rectangle clamped = EditPanelScreenLayout.ClampRectToViewer(offBottom, ViewerW, ViewerH, Margin);

            Assert.True(EditPanelScreenLayout.FitsViewer(clamped, ViewerW, ViewerH, Margin));
            Assert.Equal(offBottom.Width, clamped.Width);
            Assert.Equal(offBottom.Height, clamped.Height);
        }

        [Fact]
        public void ClampRectToViewer_ShiftsUpWhenPanelExtendsBelowViewer() {
            var offBottom = new Rectangle(200, 700, 400, 200);
            Rectangle clamped = EditPanelScreenLayout.ClampRectToViewer(offBottom, ViewerW, ViewerH, Margin);

            Assert.True(clamped.Top < offBottom.Top);
            Assert.Equal(ViewerH - Margin - offBottom.Height, clamped.Top);
        }

        [Fact]
        public void ClampRectToViewer_ShiftsDownWhenPanelExtendsAboveViewer() {
            var offTop = new Rectangle(200, -50, 300, 150);
            Rectangle clamped = EditPanelScreenLayout.ClampRectToViewer(offTop, ViewerW, ViewerH, Margin);

            Assert.Equal(Margin, clamped.Top);
        }

        [Fact]
        public void FitsViewer_ReturnsTrueForBoundsAlreadyInside() {
            var inside = new Rectangle(Margin, Margin, 200, 100);
            Assert.True(EditPanelScreenLayout.FitsViewer(inside, ViewerW, ViewerH, Margin));
        }

        [Fact]
        public void GetChooserTopLeft_OffsetsFromAnchorThenClamps() {
            var anchor = new Point(100, 100);
            var size = new Size(300, 200);
            Point topLeft = EditPanelScreenLayout.GetChooserTopLeft(anchor, size, ViewerW, ViewerH, Margin);

            //No clamp needed here: anchor(100,100) - (24,16) = (76,84), already inside the margin.
            Assert.Equal(76, topLeft.X);
            Assert.Equal(84, topLeft.Y);
        }

        [Fact]
        public void GetChooserTopLeft_NearOrigin_ClampsToMargin() {
            var anchor = new Point(10, 10);
            var size = new Size(300, 200);
            Point topLeft = EditPanelScreenLayout.GetChooserTopLeft(anchor, size, ViewerW, ViewerH, Margin);

            //anchor(10,10) - (24,16) = (-14,-6), both below margin, so both clamp to Margin.
            Assert.Equal(Margin, topLeft.X);
            Assert.Equal(Margin, topLeft.Y);
        }
    }
}
