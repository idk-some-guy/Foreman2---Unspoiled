using System;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaSize = Avalonia.Size;

namespace Foreman.Mac.Canvas.Panels {
    //Adapts upstream ProductionGraphView/EditPanelViewportLayout.cs (docs/panels-reference.md §7) to
    //Avalonia. The natural-size-then-clamp arithmetic (max = viewer size minus margin*2, final = min
    //(natural, max)) is verbatim; WinForms' AutoScroll scroll-host (EnsureScrollHost/LayoutScrollableContent)
    //has no port here, since Task 1 validates against a fixed-size placeholder panel with nothing to
    //overflow - a real ScrollViewer wrapper is deferred to whichever later panel first needs it.
    public static class EditPanelViewportLayout {
        public static AvaloniaSize MeasureNaturalSize(AvaloniaControl content) {
            content.Width = double.NaN;
            content.Height = double.NaN;
            content.Measure(new AvaloniaSize(double.PositiveInfinity, double.PositiveInfinity));
            return content.DesiredSize;
        }

        public static AvaloniaSize Apply(AvaloniaControl content, double viewerWidth, double viewerHeight, int margin = EditPanelScreenLayout.DefaultMargin) {
            double maxWidth = Math.Max(1, viewerWidth - margin * 2);
            double maxHeight = Math.Max(1, viewerHeight - margin * 2);

            AvaloniaSize natural = MeasureNaturalSize(content);
            double width = Math.Min(Math.Max(1, natural.Width), maxWidth);
            double height = Math.Min(Math.Max(1, natural.Height), maxHeight);

            var final = new AvaloniaSize(width, height);
            content.Width = final.Width;
            content.Height = final.Height;
            content.Measure(final);
            return final;
        }
    }
}
