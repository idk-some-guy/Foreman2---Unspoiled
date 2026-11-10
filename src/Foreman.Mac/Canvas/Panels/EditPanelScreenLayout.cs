using System;
using System.Drawing;

namespace Foreman.Mac.Canvas.Panels {
    //Verbatim port of upstream ProductionGraphView/EditPanelScreenLayout.cs (docs/panels-reference.md §7):
    //screen-space clamping of a floating panel's rectangle inside the viewer's visible client area, plus
    //anchor-offset placement for choosers. ShiftControlsToFit's mutation target moves from upstream's
    //Control.Location to Canvas.Left/Top, since panels are positioned via Canvas attached properties here.
    public static class EditPanelScreenLayout {
        public const int DefaultMargin = 25;

        public static Rectangle ClampRectToViewer(Rectangle bounds, int viewerWidth, int viewerHeight, int margin = DefaultMargin) {
            int x = bounds.X;
            int y = bounds.Y;
            int maxX = Math.Max(margin, viewerWidth - margin - bounds.Width);
            int maxY = Math.Max(margin, viewerHeight - margin - bounds.Height);
            if (x < margin)
                x = margin;
            else if (x > maxX)
                x = maxX;
            if (y < margin)
                y = margin;
            else if (y > maxY)
                y = maxY;
            return new Rectangle(x, y, bounds.Width, bounds.Height);
        }

        public static bool FitsViewer(Rectangle bounds, int viewerWidth, int viewerHeight, int margin = DefaultMargin) =>
            bounds.Left >= margin
            && bounds.Top >= margin
            && bounds.Right <= viewerWidth - margin
            && bounds.Bottom <= viewerHeight - margin;

        public static Point GetChooserTopLeft(Point anchor, Size panelSize, int viewerWidth, int viewerHeight, int margin = DefaultMargin) {
            const int anchorInsetX = 24;
            const int anchorInsetY = 16;
            var desired = new Rectangle(anchor.X - anchorInsetX, anchor.Y - anchorInsetY, panelSize.Width, panelSize.Height);
            return ClampRectToViewer(desired, viewerWidth, viewerHeight, margin).Location;
        }
    }
}
