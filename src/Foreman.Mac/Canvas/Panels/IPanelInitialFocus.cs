namespace Foreman.Mac.Canvas.Panels {
    //Lets FloatingPanelHost.Show hand focus to a specific control inside a panel (e.g. IRChooserPanel's
    //filter box) instead of the panel's own root - Show() calls this only after the content is attached and
    //repositioned, unlike a panel's own constructor/Initialize(), where Focus() on an unattached control is
    //a silent no-op.
    public interface IPanelInitialFocus {
        void FocusInitialControl();
    }
}
