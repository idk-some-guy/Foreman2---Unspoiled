using System;
using Avalonia.Threading;
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
        private bool _centerOnAnchor;
        private AvaloniaControl? _closingContent;
        private DispatcherTimer? _closingTimer;

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

        public void Show(AvaloniaControl content, DrawingPoint anchorGraphPoint, bool centerOnAnchor = false) {
            Close();
            CancelPendingClose();
            _content = content;
            _anchorGraphPoint = anchorGraphPoint;
            _centerOnAnchor = centerOnAnchor;
            _host.Children.Add(content);
            if (content is IPanelContentResize resizable)
                resizable.ContentResized += OnContentResized;
            Reposition();
            FocusPrimary(content);
        }

        //Content that shrinks/grows (QuickAddPalette's row count) invalidates its own measure but doesn't
        //arrange until the next layout pass - forcing that pass here means Reposition's Measure call below
        //sees the real desired size instead of whatever was true before the row count changed.
        private void OnContentResized(object? sender, EventArgs e) {
            _content?.UpdateLayout();
            Reposition();
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
        //IsOpen/_content clear immediately either way, so callers see the panel as closed right away -
        //content that opts into IPanelClosingTransition (RadialMenu) just lingers in the host, fading,
        //for its own ClosingDuration before actually being detached.
        public void Close() {
            if (_content is null)
                return;
            if (_content is IPanelContentResize resizable)
                resizable.ContentResized -= OnContentResized;
            AvaloniaControl closing = _content;
            _owner.Focus();
            _content = null;

            if (closing is IPanelClosingTransition transition) {
                CancelPendingClose();
                _closingContent = closing;
                transition.BeginClosing();
                _closingTimer = new DispatcherTimer { Interval = transition.ClosingDuration };
                _closingTimer.Tick += OnClosingTimerElapsed;
                _closingTimer.Start();
            } else {
                _host.Children.Remove(closing);
            }
        }

        private void OnClosingTimerElapsed(object? sender, EventArgs e) => CancelPendingClose();

        //The owning canvas's own teardown (GraphCanvasControl.OnDetachedFromVisualTree) calls this: a
        //DispatcherTimer left running past its window's own lifetime fires on a UI thread that a headless
        //test runner may since have handed to a different, unrelated test - stopping it here instead avoids
        //that cross-thread ownership exception during that other test's cleanup.
        public void CancelPendingCloseTransition() => CancelPendingClose();

        //Cancellable: a new Show() (or a second Close()) purges whatever's still mid-fade immediately
        //instead of leaving two panels in the host or racing the timer to remove one already gone.
        private void CancelPendingClose() {
            if (_closingTimer is not null) {
                _closingTimer.Stop();
                _closingTimer.Tick -= OnClosingTimerElapsed;
                _closingTimer = null;
            }
            if (_closingContent is not null) {
                _host.Children.Remove(_closingContent);
                _closingContent = null;
            }
        }

        public void Reposition() {
            if (_content is null)
                return;

            int viewerWidth = (int)_viewport.Width;
            int viewerHeight = (int)_viewport.Height;
            DrawingRectangle rect = _centerOnAnchor
                ? CenteredRect(_content, _anchorGraphPoint)
                : MeasureReflowableRect(_content, _anchorGraphPoint, viewerWidth, viewerHeight);
            DrawingRectangle clamped = EditPanelScreenLayout.ClampRectToViewer(rect, viewerWidth, viewerHeight);
            AvaloniaCanvas.SetLeft(_content, clamped.X);
            AvaloniaCanvas.SetTop(_content, clamped.Y);
        }

        //Centers fixed-size content on the anchor instead of the reflow-then-anchor-at-top-left math every other panel uses.
        private DrawingRectangle CenteredRect(AvaloniaControl content, DrawingPoint anchorGraphPoint) {
            content.Measure(new AvaloniaSize(double.PositiveInfinity, double.PositiveInfinity));
            AvaloniaSize desired = content.DesiredSize;
            AvaloniaPoint anchorScreen = _viewport.GraphToScreen(anchorGraphPoint);
            return new DrawingRectangle(
                (int)Math.Round(anchorScreen.X - (desired.Width / 2)),
                (int)Math.Round(anchorScreen.Y - (desired.Height / 2)),
                (int)Math.Ceiling(desired.Width),
                (int)Math.Ceiling(desired.Height));
        }

        //Gives content (e.g. QuickAddPalette) a chance to actually reflow to the available space before
        //EditPanelViewportLayout.Apply measures+clamps it - otherwise Apply can only shrink the outer
        //Width/Height property, which clips fixed-size content instead of making it smaller.
        private DrawingRectangle MeasureReflowableRect(AvaloniaControl content, DrawingPoint anchorGraphPoint, int viewerWidth, int viewerHeight) {
            AvaloniaSize size = EditPanelViewportLayout.Apply(content, viewerWidth, viewerHeight);
            return AnchoredRect(anchorGraphPoint, size);
        }

        private DrawingRectangle AnchoredRect(DrawingPoint anchorGraphPoint, AvaloniaSize size) {
            AvaloniaPoint anchorScreen = _viewport.GraphToScreen(anchorGraphPoint);
            var anchorScreenPoint = new DrawingPoint((int)anchorScreen.X, (int)anchorScreen.Y);
            var anchorSize = new DrawingSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
            return new DrawingRectangle(anchorScreenPoint.X, anchorScreenPoint.Y, anchorSize.Width, anchorSize.Height);
        }
    }
}
