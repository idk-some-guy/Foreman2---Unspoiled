using Foreman.Mac.Canvas.Elements;
using System;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaCanvas = Avalonia.Controls.Canvas;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaSize = Avalonia.Size;

namespace Foreman.Mac.Canvas.Panels {
    //Avalonia equivalent of upstream FloatingTooltipControl's panel half (docs/panels-reference.md §7):
    //an overlay child hosted inside the owning canvas's own visual tree rather than a Popup, so it shares
    //the canvas's coordinate space (EditPanelViewportLayout/EditPanelScreenLayout's math) and gets real
    //Avalonia focus the same way upstream's WinForms child control did. One panel open at a time, matching
    //upstream's ClearFloatingControls/SubwindowOpen model - Show() always closes whatever was open first.
    //any new toolbar/menu handler that mutates canvas state or opens a modal must call Close() itself
    //(upstream's Leave event did this implicitly; this port closes at handlers - see perf-packaging-reference
    //§2). MainWindow's Closing-gate/save-prompt path (OnClosing/ConfirmCloseAsync) is exempt: it never
    //touches canvas state, so a panel left open through it is still exactly as valid as it was.
    public sealed class FloatingPanelHost {
        private readonly AvaloniaCanvas _host;
        private readonly Viewport _viewport;
        private readonly AvaloniaControl _owner;
        private AvaloniaControl? _content;
        private DrawingPoint _anchorGraphPoint;
        private Direction? _anchorDirection;

        public FloatingPanelHost(AvaloniaCanvas host, Viewport viewport, AvaloniaControl owner) {
            _host = host;
            _viewport = viewport;
            _owner = owner;
        }

        public bool IsOpen => _content is not null;
        public AvaloniaControl? Content => _content;

        public DrawingRectangle Bounds => BoundsOf(_content);

        private static DrawingRectangle BoundsOf(AvaloniaControl? control) => control is null
            ? DrawingRectangle.Empty
            : new DrawingRectangle(
                (int)AvaloniaCanvas.GetLeft(control), (int)AvaloniaCanvas.GetTop(control),
                (int)control.Width, (int)control.Height);

        //anchorDirection is the arrow direction upstream's getTooltipScreenBounds would draw (reference §7):
        //omitted for the choosers, which anchor their top-left directly at the click point; edit panels
        //pass Direction.Right, matching upstream's EditNode leftAnchor placement - the panel body sits to
        //the left of that anchor rather than starting at it.
        public void Show(AvaloniaControl content, DrawingPoint anchorGraphPoint, Direction? anchorDirection = null) {
            Close();
            _content = content;
            _anchorGraphPoint = anchorGraphPoint;
            _anchorDirection = anchorDirection;
            _host.Children.Add(content);
            Reposition();
            FocusPrimary(content);
        }

        private static void FocusPrimary(AvaloniaControl primary) {
            if (primary is IPanelInitialFocus focusable)
                focusable.FocusInitialControl();
            else
                primary.Focus();
        }

        //Restores focus to the owning canvas (reference §7): the click-outside-closes path already does
        //this itself right after calling Close(), but Escape and a chooser's own selection-close had no
        //equivalent, leaving canvas keyboard shortcuts dead until the next click.
        public void Close() {
            if (_content is null)
                return;
            _owner.Focus();
            _host.Children.Remove(_content);
            _content = null;
        }

        public void Reposition() {
            if (_content is null)
                return;

            int viewerWidth = (int)_viewport.Width;
            int viewerHeight = (int)_viewport.Height;
            DrawingRectangle rect = MeasureReflowableRect(_content, _anchorGraphPoint, _anchorDirection, viewerWidth, viewerHeight);
            DrawingRectangle clamped = EditPanelScreenLayout.ClampRectToViewer(rect, viewerWidth, viewerHeight);
            AvaloniaCanvas.SetLeft(_content, clamped.X);
            AvaloniaCanvas.SetTop(_content, clamped.Y);
        }

        //Gives content (e.g. IRChooserPanel) a chance to actually reflow to the available space before
        //EditPanelViewportLayout.Apply measures+clamps it - otherwise Apply can only shrink the outer
        //Width/Height property, which clips fixed-size content instead of making it smaller.
        private DrawingRectangle MeasureReflowableRect(AvaloniaControl content, DrawingPoint anchorGraphPoint, Direction? anchorDirection, int viewerWidth, int viewerHeight) {
            if (content is IViewportFittable fittable) {
                const int margin = EditPanelScreenLayout.DefaultMargin;
                fittable.FitToViewport(Math.Max(1, viewerWidth - margin * 2), Math.Max(1, viewerHeight - margin * 2));
            }

            AvaloniaSize size = EditPanelViewportLayout.Apply(content, viewerWidth, viewerHeight);
            return AnchoredRect(anchorGraphPoint, anchorDirection, size);
        }

        private DrawingRectangle AnchoredRect(DrawingPoint anchorGraphPoint, Direction? anchorDirection, AvaloniaSize size) {
            AvaloniaPoint anchorScreen = _viewport.GraphToScreen(anchorGraphPoint);
            var anchorScreenPoint = new DrawingPoint((int)anchorScreen.X, (int)anchorScreen.Y);
            var anchorSize = new DrawingSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
            return anchorDirection is Direction direction
                ? FloatingTooltipRenderer.GetTooltipScreenBounds(anchorScreenPoint, anchorSize, direction)
                : new DrawingRectangle(anchorScreenPoint.X, anchorScreenPoint.Y, anchorSize.Width, anchorSize.Height);
        }
    }
}
