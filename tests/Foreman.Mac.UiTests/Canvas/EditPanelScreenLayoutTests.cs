using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Foreman.Mac.Canvas.Panels;
using System.Drawing;
using Xunit;
using AvaloniaCanvas = Avalonia.Controls.Canvas;

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
        public void GetShiftToFit_UnionOfTwoPanels_UsesSingleDeltaForBoth() {
            var left = new Rectangle(50, 750, 472, 689);
            var right = new Rectangle(left.Right + 5, 750, 300, 400);
            Rectangle union = Rectangle.Union(left, right);
            Point shift = EditPanelScreenLayout.GetShiftToFit(union, ViewerW, ViewerH, Margin);

            var shiftedUnion = new Rectangle(union.X + shift.X, union.Y + shift.Y, union.Width, union.Height);
            Assert.True(EditPanelScreenLayout.FitsViewer(shiftedUnion, ViewerW, ViewerH, Margin));
            Assert.True(shift.Y < 0, "Tall union below the viewer should shift upward.");
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

        //Not part of upstream's own test file (ShiftControlsToFit had no direct upstream coverage), but
        //this port's mutation target changed from Control.Location to Canvas.Left/Top, so it earns its
        //own case alongside the ported ones above.
        [AvaloniaFact]
        public void ShiftControlsToFit_MovesEveryPanelByTheSameDelta() {
            var left = new Control();
            var right = new Control();
            AvaloniaCanvas.SetLeft(left, 50);
            AvaloniaCanvas.SetTop(left, 750);
            AvaloniaCanvas.SetLeft(right, 527);
            AvaloniaCanvas.SetTop(right, 750);

            var union = new Rectangle(50, 750, 300 + (527 - 50), 400);
            EditPanelScreenLayout.ShiftControlsToFit(union, ViewerW, ViewerH, Margin, left, right);

            Point shift = EditPanelScreenLayout.GetShiftToFit(union, ViewerW, ViewerH, Margin);
            Assert.Equal(50 + shift.X, AvaloniaCanvas.GetLeft(left));
            Assert.Equal(750 + shift.Y, AvaloniaCanvas.GetTop(left));
            Assert.Equal(527 + shift.X, AvaloniaCanvas.GetLeft(right));
            Assert.Equal(750 + shift.Y, AvaloniaCanvas.GetTop(right));
        }
    }
}
