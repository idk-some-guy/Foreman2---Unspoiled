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
    //Avalonia focus the same way upstream's WinForms child control did. One panel (or one paired panel set)
    //open at a time, matching upstream's ClearFloatingControls/SubwindowOpen model - Show()/ShowPaired()
    //always close whatever was open first.
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
        private AvaloniaControl? _companion;
        private DrawingPoint _companionAnchorGraphPoint;
        private Direction _companionAnchorDirection;

        public FloatingPanelHost(AvaloniaCanvas host, Viewport viewport, AvaloniaControl owner) {
            _host = host;
            _viewport = viewport;
            _owner = owner;
        }

        public bool IsOpen => _content is not null;
        public AvaloniaControl? Content => _content;
        public AvaloniaControl? CompanionContent => _companion;

        public DrawingRectangle Bounds => BoundsOf(_content);
        public DrawingRectangle CompanionBounds => BoundsOf(_companion);

        private static DrawingRectangle BoundsOf(AvaloniaControl? control) => control is null
            ? DrawingRectangle.Empty
            : new DrawingRectangle(
                (int)AvaloniaCanvas.GetLeft(control), (int)AvaloniaCanvas.GetTop(control),
                (int)control.Width, (int)control.Height);

        //anchorDirection is the arrow direction upstream's getTooltipScreenBounds would draw (reference §7):
        //omitted for the choosers, which anchor their top-left directly at the click point; edit panels
        //(Task 6) pass Direction.Right, matching upstream's EditNode/EditRecipeNode leftAnchor placement -
        //the panel body sits to the left of that anchor rather than starting at it.
        public void Show(AvaloniaControl content, DrawingPoint anchorGraphPoint, Direction? anchorDirection = null) {
            Close();
            _content = content;
            _anchorGraphPoint = anchorGraphPoint;
            _anchorDirection = anchorDirection;
            _host.Children.Add(content);
            Reposition();
            FocusPrimary(content);
        }

        //Ports EditRecipeNode's paired-tooltip placement (upstream ProductionGraphViewer.cs 538-576): the
        //companion (upstream's RecipePanel) floats at its own fixed intrinsic size - it's never remeasured
        //against the viewport, matching upstream's recipePanel.Size used as-is in getTooltipScreenBounds -
        //while the primary keeps reflowing through EditPanelViewportLayout the way a solo Show() does. Both
        //open, reposition, and close together; only the primary takes focus.
        public void ShowPaired(AvaloniaControl primary, DrawingPoint primaryAnchor, Direction primaryDirection,
                                AvaloniaControl companion, DrawingPoint companionAnchor, Direction companionDirection) {
            Close();
            _content = primary;
            _anchorGraphPoint = primaryAnchor;
            _anchorDirection = primaryDirection;
            _companion = companion;
            _companionAnchorGraphPoint = companionAnchor;
            _companionAnchorDirection = companionDirection;
            _host.Children.Add(primary);
            _host.Children.Add(companion);
            Reposition();
            FocusPrimary(primary);
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
            if (_companion is not null) {
                _host.Children.Remove(_companion);
                _companion = null;
            }
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
            DrawingRectangle primaryRect = MeasureReflowableRect(_content, _anchorGraphPoint, _anchorDirection, viewerWidth, viewerHeight);

            if (_companion is null) {
                DrawingRectangle clamped = EditPanelScreenLayout.ClampRectToViewer(primaryRect, viewerWidth, viewerHeight);
                AvaloniaCanvas.SetLeft(_content, clamped.X);
                AvaloniaCanvas.SetTop(_content, clamped.Y);
                return;
            }

            //Each panel's desired rect is placed at its own anchor first (unclamped), then both shift by the
            //same delta so their relative arrangement survives hitting a viewport edge, mirroring upstream's
            //Rectangle.Union(editRect, recipeRect) + PlaceFloatingPanels rather than clamping each
            //independently, which could let one panel overlap the other instead of both sliding together.
            DrawingRectangle companionRect = MeasureFixedRect(_companion, _companionAnchorGraphPoint, _companionAnchorDirection);
            AvaloniaCanvas.SetLeft(_content, primaryRect.X);
            AvaloniaCanvas.SetTop(_content, primaryRect.Y);
            AvaloniaCanvas.SetLeft(_companion, companionRect.X);
            AvaloniaCanvas.SetTop(_companion, companionRect.Y);

            EditPanelScreenLayout.ShiftControlsToFit(
                DrawingRectangle.Union(primaryRect, companionRect), viewerWidth, viewerHeight,
                EditPanelScreenLayout.DefaultMargin, _content, _companion);
        }

        //Gives content (e.g. IRChooserPanel/EditRecipePanel) a chance to actually reflow to the available
        //space before EditPanelViewportLayout.Apply measures+clamps it - otherwise Apply can only shrink the
        //outer Width/Height property, which clips fixed-size content instead of making it smaller.
        private DrawingRectangle MeasureReflowableRect(AvaloniaControl content, DrawingPoint anchorGraphPoint, Direction? anchorDirection, int viewerWidth, int viewerHeight) {
            if (content is IViewportFittable fittable) {
                const int margin = EditPanelScreenLayout.DefaultMargin;
                fittable.FitToViewport(Math.Max(1, viewerWidth - margin * 2), Math.Max(1, viewerHeight - margin * 2));
            }

            AvaloniaSize size = EditPanelViewportLayout.Apply(content, viewerWidth, viewerHeight);
            return AnchoredRect(anchorGraphPoint, anchorDirection, size);
        }

        private DrawingRectangle MeasureFixedRect(AvaloniaControl content, DrawingPoint anchorGraphPoint, Direction anchorDirection) =>
            AnchoredRect(anchorGraphPoint, anchorDirection, new AvaloniaSize(content.Width, content.Height));

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
