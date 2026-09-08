using System;

namespace Foreman.Mac.Canvas.Panels {
    //Lets panel content whose natural size can change after Show() (e.g. QuickAddPalette's row count)
    //tell the host to re-measure and reposition - Show() only lays out from the content's DesiredSize
    //once, and that DesiredSize goes stale the moment the content's own layout would want to grow or shrink.
    public interface IPanelContentResize {
        event EventHandler ContentResized;
    }
}
