using System;

namespace Foreman.Mac.Canvas.Panels {
    //Lets panel content opt into a fade-out before FloatingPanelHost.Close actually detaches it (e.g.
    //RadialMenu's own close animation) - Close() still clears IsOpen/_content immediately so callers see
    //the panel as closed right away, but the visual itself lingers in the host for ClosingDuration.
    public interface IPanelClosingTransition {
        TimeSpan ClosingDuration { get; }
        void BeginClosing();
    }
}
