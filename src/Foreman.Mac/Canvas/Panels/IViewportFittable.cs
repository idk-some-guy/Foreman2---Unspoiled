namespace Foreman.Mac.Canvas.Panels {
    //Lets FloatingPanelHost.Reposition offer content a chance to shrink itself before the natural-size-then-
    //clamp math in EditPanelViewportLayout.Apply runs, so a panel like IRChooserPanel can actually reflow
    //(ported from upstream ApplyViewerBounds) instead of just getting its outer bounds clipped.
    public interface IViewportFittable {
        void FitToViewport(double maxWidth, double maxHeight);
    }
}
