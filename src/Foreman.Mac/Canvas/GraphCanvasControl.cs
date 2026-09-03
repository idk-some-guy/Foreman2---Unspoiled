using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Views;
using Foreman.Models;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCanvas = Avalonia.Controls.Canvas;
using AvaloniaPoint = Avalonia.Point;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace Foreman.Mac.Canvas {
    //Ports upstream's NewNodeType (reference §3/§10's AddNewNode/AddItem call sites).
    public enum NewNodeType { Disconnected, Supplier, Consumer }

    //Ports the chooser-dependent tail of DraggedLinkElement.EndDrag (reference §3's "exactly one end bound,
    //dropped on empty space" outcome, upstream AddNewNode): the real RecipeChooserPanel/ItemChooserPanel is
    //P5 scope (reference §10). GraphCanvasControl.RequestNewNodeFromLinkDrag just records this and disposes
    //the drag; Task 7 wires NewNodeRequested to the real placeholder chooser.
    public readonly record struct NewNodeLinkDragRequest(NewNodeType NodeType, ItemQualityPair Item, AvaloniaPoint ScreenPoint, DrawingPoint EndpointLocation, BaseNodeElement OriginElement, NodeDirection Direction);

    //Ports ProductionGraphViewer's OnPaint transform setup, MouseWheel zoom, middle/right-drag pan,
    //GetNodeAtPoint hit-testing, and MouseMove's per-frame hover-tooltip recompute (reference §6); the paint
    //pipeline itself (GetPaintingOrder, LOD/dynamic-link-width style selection, link rendering) is
    //GraphViewer's job (reference §3, §8.12) - this control just hosts the Avalonia surface, owns pointer
    //input, and delegates painting to it. Link-drag lifecycle (reference §3: StartLinkDrag/DisposeLinkDrag,
    //the ghost's MouseDownElement redirection, passthrough-bus creation) lives here too, alongside the rest
    //of the drag/mouse-routing state (MouseDownElement, CurrentDragOperation) rather than on GraphViewer.
    public sealed class GraphCanvasControl : Decorator {
        //Ports ProductionGraphViewer's private DragOperation enum verbatim (reference §1). DrawShape enters
        //the state machine here but its Move/Up behavior is Task 6's job - inDrawShapeMode stays false until
        //then, so this control never actually reaches it yet.
        internal enum DragOperation { None, Item, Selection, DrawShape }

        //Reference §1: "dragDiff² > minDragDiff (30, squared screen pixels)" - minDragDiff already IS the
        //squared threshold (a ~5.5px move), not 30 squared again.
        private const int MinDragDiffSquared = 30;

        public Viewport Viewport { get; }
        public GridManager Grid { get; }
        public GraphViewer Viewer { get; }
        public FloatingPanelHost FloatingPanelHost { get; }
        public List<BaseNodeElement> NodeElements => Viewer.NodeElements;
        public List<LinkElement> LinkElements => Viewer.LinkElements;
        public List<AnnotationElement> Annotations => Viewer.Annotations;

        internal DragOperation CurrentDragOperation { get; private set; } = DragOperation.None;
        internal GraphElement? MouseDownElement { get; private set; }
        internal bool ViewBeingDragged { get; private set; }

        //Ports draggedLinkElement (reference §3) and SmartNodeDirection (reference §8) - the latter isn't
        //wired from settings yet (that's task 11's auto-placement polish), so it defaults false, matching
        //upstream's own cold-start default before MainForm_Load reads the setting.
        internal DraggedLinkElement? DraggedLink { get; private set; }
        internal bool SmartNodeDirection { get; set; }

        //Stands in for upstream's global Control.ModifierKeys read inside UpdateSlaveLinks (reference §3):
        //Avalonia has no such global query, so this tracks the most recently observed KeyModifiers from
        //every pointer/key event and is read from PrePaint instead. Upstream's Ctrl maps to Cmd/Meta here
        //per this task's plan (docs/interaction-reference.md §7's Cmd-mapping note doesn't cover this
        //specific in-drag modifier, since it isn't a selection idiom).
        internal bool IsPassthroughBusModifierHeld { get; private set; }

        internal NewNodeLinkDragRequest? LastNewNodeLinkDragRequest { get; private set; }
        internal Action<NewNodeLinkDragRequest>? NewNodeRequested { get; set; }

        //Ports inDrawShapeMode (reference §1/§6): armed by BeginDrawShape (the background right-click menu's
        //"Add Shape" entry, since there's no toolbar button or keyboard shortcut per reference §6), cleared by
        //FinishDrawShape or an Escape key-down.
        private bool inDrawShapeMode;
        private AvaloniaPoint mouseDownStartScreenPoint;
        private DrawingPoint viewDragOriginGraphPoint;
        private DrawingPoint selectionZoneOriginGraphPoint;
        private AvaloniaPoint? hoverScreenPoint;

        public GraphCanvasControl() {
            Viewport = new Viewport(Bounds.Width, Bounds.Height);
            Grid = new GridManager();
            Viewer = new GraphViewer(Viewport, Grid);
            //The Skia surface paints via our own Render(DrawingContext) override below (Decorator.Render
            //stays open, unlike the sealed Panel.Render an overlay-hosting Canvas base would need); the
            //floating panel host owns this separate, real Decorator.Child instead, so it gets genuine
            //Avalonia layout/focus/hit-testing layered above that paint (reference §7/§9 step 1, risk 5).
            //Gotcha: overlay.Background must stay null - a set Background would hit-test as opaque and
            //swallow every canvas click, since Decorator.Child paints above our own Render override.
            var overlay = new AvaloniaCanvas();
            Child = overlay;
            FloatingPanelHost = new FloatingPanelHost(overlay, Viewport, this);
            Viewer.Context.SetClipboardText = SetClipboardText;
            Viewer.Context.GetClipboardText = GetClipboardText;
            Viewer.Context.StartLinkDrag = StartLinkDrag;
            Viewer.Context.EditNode = EditNode;
            Viewer.ShowAnnotationPropertiesDialog = ShowAnnotationProperties;
            NewNodeRequested = HandleNewNodeRequestedAsync;
            ClipToBounds = true;
            Focusable = true;
        }

        //Ports StartLinkDrag/DisposeLinkDrag (reference §3): disposes any prior in-flight drag, constructs
        //the new ghost, and redirects MouseDownElement to it - every further mouse event routes through the
        //ghost first per the priority OnPointerPressed/Moved/Released give it below.
        internal void StartLinkDrag(BaseNodeElement startNode, LinkType linkType, ItemQualityPair item) {
            DraggedLink?.Dispose();
            DraggedLink = new DraggedLinkElement(this, startNode, linkType, item);
            MouseDownElement = DraggedLink;
        }

        //Unlike upstream's DisposeLinkDrag, which leaves MouseDownElement pointing at the just-disposed ghost
        //until the next left-click resets it (reachable via a right-click cancel that lands while a pan is
        //also active - the generic-fallback MouseUp branch that would otherwise clear it is skipped whenever
        //viewBeingDragged), this always clears MouseDownElement too, so a later drag never redirects
        //Dragged/SetLocation calls onto a disposed element.
        internal void DisposeLinkDrag() {
            DraggedLink?.Dispose();
            DraggedLink = null;
            MouseDownElement = null;
        }

        //Ports EditNode/EditRecipeNode's dispatch (reference §8/§9 step 6, upstream ProductionGraphViewer.cs
        //512-586): a RecipeNodeElement opens the real EditRecipePanel paired with a standalone RecipePanel -
        //upstream's editPanel/recipePanel floating side by side (leftAnchor/Direction.Right for the editor,
        //rightAnchor/Direction.Left for the recipe card) - every other node type opens EditFlowPanel alone,
        //matching upstream's unconditional else. Anchored at the node's edges the same way upstream's
        //non-locked branch is; LockedRecipeEditPanelPosition's fixed (15,15) origin needs the Settings dialog
        //to toggle (5b scope), so that branch isn't reachable here.
        internal void EditNode(BaseNodeElement node) {
            if (Viewer.Context.DCache is not DataCache cache)
                return;

            var leftAnchor = new DrawingPoint(node.X - (node.Width / 2), node.Y);

            if (node is RecipeNodeElement && node.ViewModel is IRecipeNodeViewModel recipeViewModel) {
                if (recipeViewModel.BaseRecipe.Recipe is not IRecipe baseRecipe)
                    return;

                var editPanel = new EditRecipePanel(recipeViewModel, Viewer) {
                    RequestRedraw = RequestRedraw,
                    RequestReposition = FloatingPanelHost.Reposition,
                };
                var recipePanel = new RecipePanel([baseRecipe], Viewer.Context.AbbreviateSciPacks);
                var rightAnchor = new DrawingPoint(node.X + (node.Width / 2), node.Y);
                FloatingPanelHost.ShowPaired(editPanel, leftAnchor, Direction.Right, recipePanel, rightAnchor, Direction.Left);
                return;
            }

            var flowPanel = new EditFlowPanel(node.ViewModel, Viewer) {
                RequestRedraw = RequestRedraw,
                RequestReposition = FloatingPanelHost.Reposition,
            };
            FloatingPanelHost.Show(flowPanel, leftAnchor, Direction.Right);
        }

        //IMPORTANT 4 (final fix wave, reference §7): loading a save over an open edit/chooser panel left it
        //bound to a node the load just replaced or removed - closes it first, mirroring upstream
        //ClearFloatingControls' own unconditional-close-on-major-state-change model.
        internal GraphLoadResult LoadDocument(DataCache cache, string json, bool setEnablesFromJson = true) {
            FloatingPanelHost.Close();
            return Viewer.LoadDocument(cache, json, setEnablesFromJson);
        }

        internal GraphLoadResult LoadDocument(DataCache cache, GraphViewerSaveDocument document, bool setEnablesFromJson = true) {
            FloatingPanelHost.Close();
            return Viewer.LoadDocument(cache, document, setEnablesFromJson);
        }

        //Ports AddPassthroughNodesFromSelection (reference §3 "Bus drop"): one new passthrough node per
        //selected passthrough, mirroring the master drag's placement offset, each auto-linked back to its own
        //source passthrough, then the selection becomes the new nodes.
        internal void AddPassthroughNodesFromSelection(LinkType linkType, DrawingSize offset) {
            var newPassthroughNodes = new List<BaseNodeElement>();
            foreach (PassthroughNodeElement passthroughNode in Viewer.SelectedNodes.OfType<PassthroughNodeElement>()) {
                NodeDirection newNodeDirection = !SmartNodeDirection
                    ? Viewer.Graph.DefaultNodeDirection
                    : DraggedLink is { } drag
                        ? drag.Type != BaseLinkElement.LineType.UShape
                            ? passthroughNode.ViewModel.NodeDirection
                            : passthroughNode.ViewModel.NodeDirection == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up
                        : Viewer.Graph.DefaultNodeDirection;

                ItemQualityPair passthroughItem = ((IPassthroughNodeViewModel)passthroughNode.ViewModel).PassthroughItem;

                int yoffset = linkType == LinkType.Input ? passthroughNode.Height / 2 : -passthroughNode.Height / 2;
                yoffset *= newNodeDirection == NodeDirection.Up ? 1 : -1;
                yoffset += offset.Height;

                NodeId newNodeId = Viewer.Session.Editor.CreatePassthroughNode(passthroughItem, new DrawingPoint(passthroughNode.Location.X + offset.Width, passthroughNode.Location.Y + yoffset));
                Viewer.Session.Editor.SetDirection(newNodeId, newNodeDirection);

                if (linkType == LinkType.Input)
                    Viewer.Session.Editor.CreateLink(newNodeId, passthroughNode.ViewModel.Id, passthroughItem);
                else
                    Viewer.Session.Editor.CreateLink(passthroughNode.ViewModel.Id, newNodeId, passthroughItem);

                if (Viewer.NodeElementDictionary.TryGetValue(newNodeId, out BaseNodeElement? newElement))
                    newPassthroughNodes.Add(newElement);
            }
            Viewer.SetSelection(newPassthroughNodes);

            DisposeLinkDrag();
            Viewer.Graph.UpdateNodeStates(false);
            RequestRedraw();
        }

        internal void RequestNewNodeFromLinkDrag(NewNodeLinkDragRequest request) {
            LastNewNodeLinkDragRequest = request;
            NewNodeRequested?.Invoke(request);
            DisposeLinkDrag();
            RequestRedraw();
        }

        //Ports ProcessNodeRequest's Passthrough case plus FinalizeNodePosition's link-creation tail (reference
        //§3/§10), for DraggedLinkElement's Cmd-held release path: no chooser involved, so this builds the node
        //and its link straight from the request the chooser flow would otherwise have carried to
        //HandleNewNodeRequestedAsync.
        internal void CreatePassthroughFromLinkDrag(NewNodeLinkDragRequest request) {
            NodeId newNodeId = Viewer.Session.Editor.CreatePassthroughNode(request.Item, request.EndpointLocation);
            Viewer.Session.Editor.SetDirection(newNodeId, request.Direction);
            if (request.NodeType == NewNodeType.Consumer)
                Viewer.Session.Editor.CreateLink(request.OriginElement.ViewModel.Id, newNodeId, request.Item);
            else
                Viewer.Session.Editor.CreateLink(newNodeId, request.OriginElement.ViewModel.Id, request.Item);

            Viewer.Graph.UpdateNodeValues();
            Viewer.Graph.UpdateNodeStates(false);
            if (Viewer.NodeElementDictionary.TryGetValue(newNodeId, out BaseNodeElement? element))
                Viewer.SetSelection([element]);
            DisposeLinkDrag();
            RequestRedraw();
        }

        //Adapts Avalonia's async IClipboard to the plain synchronous delegates BaseNodeElement's menu
        //building expects (reference §4b copy/paste, upstream's WinForms Clipboard is synchronous) - see
        //docs/upstream-divergences.md.
        private void SetClipboardText(string text) {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                Async.Fire(clipboard.SetTextAsync(text), nameof(SetClipboardText));
        }

        private string? GetClipboardText() =>
            TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? clipboard.TryGetTextAsync().GetAwaiter().GetResult() : null;

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these): the entries and the real
        //Avalonia ContextMenu most recently built for a right click, so tests can assert content/routing
        //from the model and, when needed, drive a real MenuItem.Click to prove the control's own wiring
        //(not just the element methods in isolation).
        internal IReadOnlyList<MenuEntry>? LastContextMenuEntries { get; private set; }
        internal ContextMenu? LastContextMenu { get; private set; }

        //Test-only seam: how many times a menu action asked for a repaint (reference §4's Invalidate()
        //calls inside every menu handler) - a menu click runs from Avalonia's own popup loop, after
        //OnPointerReleased's own trailing InvalidateVisual() has already fired, so it needs its own.
        internal int RedrawRequestCount { get; private set; }

        //Internal rather than private: DraggedLinkElement (a different class) also needs to request a
        //repaint at the same state-changing points upstream calls Invalidate() from (reference §3's EndDrag/
        //AddPassthroughNodesFromSelection/AddNewNode tails).
        internal void RequestRedraw() {
            RedrawRequestCount++;
            InvalidateVisual();
        }

        private void ShowContextMenu(List<MenuEntry> entries) {
            LastContextMenuEntries = entries;
            if (entries.Count == 0)
                return;

            LastContextMenu = ElementMenus.Build(entries, RequestRedraw);
            LastContextMenu.Open(this);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e) {
            base.OnSizeChanged(e);
            Viewport.SetSize(e.NewSize.Width, e.NewSize.Height, Viewer.Graph.Bounds);
            FloatingPanelHost.Reposition();
            InvalidateVisual();
        }

        //Final-review I2: Viewer/Grid were IDisposable with no caller - dead code, and a real ownership trap
        //had anything ever called it from outside this control, since ImageExportWindow (MainWindow.axaml.cs's
        //`new ImageExportWindow(GraphCanvas.Viewer)`) holds the SAME live Viewer for its export paint and never
        //disposes it itself. This control is the one place that constructs that Viewer (constructor above), so
        //disposing it here at its own natural teardown is safe as the sole owner - the only way this control
        //detaches is MainWindow itself closing, and ImageExportWindow is a modal (ShowDialog) that blocks
        //MainWindow's own close for as long as it's open, so the export path is never mid-paint when this runs.
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Viewer.Dispose();
        }

        //Ports ProductionGraphViewer_MouseWheel's own focus guard (reference §7, upstream lines 1065-1067
        //`if (ContainsFocus && !this.Focused) return;`): a hosted panel always holds real Avalonia focus
        //while it's open (FloatingPanelHost.Show focuses its content), so IsOpen alone stands in for that
        //check - the wheel must do nothing at all, not zoom the canvas out from under an open panel.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);
            if (FloatingPanelHost.IsOpen)
                return;

            AvaloniaPoint position = e.GetPosition(this);
            Viewport.ZoomAt(position, e.Delta.Y > 0, Viewer.Graph.Bounds);
            FloatingPanelHost.Reposition();
            InvalidateVisual();
            e.Handled = true;
        }

        //Ports ProductionGraphViewer_MouseDown (reference §1). ToolTipRenderer.ClearFloatingControls() and
        //the chooser-panel close-on-outside-click have no equivalent here - this port's hover tooltip is a
        //pure per-frame function of hoverScreenPoint, never a floating control, and there's no chooser panel
        //yet. Annotation_OnMouseDownDoubleClick/OnMouseDown is also out: annotation click routing is Task 6.
        //A Left click on a node claims MouseDownElement and forwards to BaseNodeElement.MouseDown (reference
        //§2); Focus() picks up keyboard input (arrow keys, Shift axis-lock) the same click starts.
        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            AvaloniaPoint screenPoint = e.GetPosition(this);

            //Ports ToolTipRenderer.ClearFloatingControls() being the first statement in
            //ProductionGraphViewer_MouseDown, unconditional on every click (reference §7): a click that
            //lands on the panel itself never reaches here at all (it's a real Avalonia child in front of
            //us), so only an outside click can close it - and, matching upstream, that same click still
            //falls through to the canvas handling below instead of being swallowed.
            if (FloatingPanelHost.IsOpen) {
                var pressedPoint = new DrawingPoint((int)screenPoint.X, (int)screenPoint.Y);
                if (FloatingPanelHost.Bounds.Contains(pressedPoint) || FloatingPanelHost.CompanionBounds.Contains(pressedPoint)) {
                    e.Handled = true;
                    return;
                }
                FloatingPanelHost.Close();
            }

            Focus();
            mouseDownStartScreenPoint = screenPoint;
            DrawingPoint graphPoint = Viewport.ScreenToGraph(screenPoint);
            PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
            UpdateModifiers(e.KeyModifiers);

            //Ghost-first priority (reference §1's clickedElement resolution): a link drag in progress claims
            //every click, ignoring whatever node/annotation is actually under the cursor.
            GraphElement? clickedElement = (GraphElement?)DraggedLink ?? HitTest(screenPoint);

            //Ports Annotation_OnMouseDownDoubleClick's short-circuit (reference §1/§6): a double left-click on
            //an annotation opens its properties dialog immediately instead of arming a drag/resize.
            if (properties.IsLeftButtonPressed && e.ClickCount == 2 && clickedElement is AnnotationElement doubleClicked) {
                doubleClicked.CancelMouseCapture();
                MouseDownElement = null;
                ShowAnnotationProperties(doubleClicked);
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            if (clickedElement is DraggedLinkElement dragGhost) {
                dragGhost.MouseDown(graphPoint, ButtonFrom(properties));
            } else if (properties.IsLeftButtonPressed && clickedElement is AnnotationElement clickedAnnotation) {
                //Ports AnnotationElement.MouseDown's own claim gate (reference §6): only an already-selected
                //annotation claims MouseDownElement, so a first click on an unselected one falls through to
                //rubber-band selection instead of immediately starting a move.
                clickedAnnotation.MouseDown(graphPoint);
                MouseDownElement = clickedAnnotation.IsSelected ? clickedAnnotation : null;
            } else {
                MouseDownElement = properties.IsLeftButtonPressed && clickedElement is BaseNodeElement ? clickedElement : null;
                if (MouseDownElement is BaseNodeElement downNode)
                    downNode.MouseDown(graphPoint);
            }

            if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed) {
                viewDragOriginGraphPoint = graphPoint;
            } else if (properties.IsLeftButtonPressed && clickedElement is not AnnotationElement) {
                selectionZoneOriginGraphPoint = graphPoint;
                if (ModifierFor(e.KeyModifiers) == SelectionModifier.Replace) {
                    bool keepGroupSelection = clickedElement is BaseNodeElement clickedNode && Viewer.SelectedNodes.Contains(clickedNode);
                    if (!keepGroupSelection) {
                        Viewer.ClearSelection();
                        InvalidateVisual();
                    }
                }
            }

            e.Pointer.Capture(this);
            e.Handled = true;
        }

        //Ports ProductionGraphViewer_MouseMove (reference §1/§2).
        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            AvaloniaPoint screenPoint = e.GetPosition(this);

            //Mirrors OnPointerPressed's chrome-swallow guard (reference §7): a move over the panel itself
            //must not start a canvas lasso, drag a node underneath, or recompute the (irrelevant, covered)
            //hover tooltip - only the panel's own controls should see it.
            if (FloatingPanelHost.IsOpen) {
                var movedPoint = new DrawingPoint((int)screenPoint.X, (int)screenPoint.Y);
                if (FloatingPanelHost.Bounds.Contains(movedPoint) || FloatingPanelHost.CompanionBounds.Contains(movedPoint))
                    return;
            }

            hoverScreenPoint = screenPoint;
            DrawingPoint graphPoint = Viewport.ScreenToGraph(screenPoint);
            PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
            UpdateModifiers(e.KeyModifiers);

            //Ports the unconditional draggedLinkElement?.MouseMoved(graph_location) call upstream makes
            //ahead of its own DragOperation switch (reference §3): the ghost tracks the cursor on every move
            //regardless of whether a node drag has crossed the threshold yet, gated the same way upstream's
            //is (not mid-lasso/shape-draw).
            //
            //A right-click mid-drag (reference §3's MouseDown Right-button cancel branch) never reaches
            //OnPointerPressed for this: Avalonia's shared MouseDevice folds a button transition into a plain
            //PointerMoved instead of a real PointerPressed/PointerReleased whenever another button is already
            //held, so the newly-pressed right button surfaces here, distinguishable only via
            //PointerUpdateKind. Cancels the same way MouseDown(Right) would instead of feeding it to
            //MouseMoved, which would just track the endpoint and leave the drag (and its eventual chooser on
            //release) running.
            if (CurrentDragOperation is not (DragOperation.Selection or DragOperation.DrawShape)) {
                if (DraggedLink is not null && properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed) {
                    DisposeLinkDrag();
                    RequestRedraw();
                } else {
                    DraggedLink?.MouseMoved(graphPoint);
                }
            }

            switch (CurrentDragOperation) {
                case DragOperation.None:
                    AvaloniaPoint dragDiff = screenPoint - mouseDownStartScreenPoint;
                    if ((dragDiff.X * dragDiff.X) + (dragDiff.Y * dragDiff.Y) > MinDragDiffSquared) {
                        if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed)
                            ViewBeingDragged = true;

                        if (MouseDownElement is not null && !inDrawShapeMode)
                            CurrentDragOperation = DragOperation.Item;
                        else if (properties.IsLeftButtonPressed)
                            CurrentDragOperation = inDrawShapeMode ? DragOperation.DrawShape : DragOperation.Selection;
                    }
                    break;

                case DragOperation.Item:
                    //Leader/follower split (reference §2): the directly-dragged node runs its own grid-
                    //snap/axis-lock logic; every other selected node and annotation gets the resulting raw,
                    //unaligned delta instead (SetLocation/X+=/Y+=), preserving relative offset off-grid.
                    if (MouseDownElement is BaseNodeElement groupDragNode && Viewer.SelectedNodes.Contains(groupDragNode)) {
                        DrawingPoint startPoint = groupDragNode.Location;
                        groupDragNode.Dragged(graphPoint, Grid);
                        DrawingPoint endPoint = groupDragNode.Location;
                        if (startPoint != endPoint) {
                            int dx = endPoint.X - startPoint.X;
                            int dy = endPoint.Y - startPoint.Y;
                            foreach (BaseNodeElement node in Viewer.SelectedNodes.Where(node => node != groupDragNode))
                                node.SetLocation(new DrawingPoint(node.X + dx, node.Y + dy));
                            foreach (AnnotationElement annotation in Viewer.SelectedAnnotations)
                                annotation.Location = new DrawingPoint(annotation.X + dx, annotation.Y + dy);
                        }
                    } else if (MouseDownElement is AnnotationElement draggedAnnotation) {
                        //Ports Annotation_OnItemDrag (reference §2/§6): a selected annotation leading the drag
                        //moves every other selected annotation by the resulting raw delta and re-snaps every
                        //selected node through SetLocation - the mirror image of the node-leads case above.
                        if (Viewer.SelectedAnnotations.Contains(draggedAnnotation)) {
                            if (draggedAnnotation.IsResizing) {
                                draggedAnnotation.Dragged(graphPoint);
                            } else {
                                DrawingPoint startPoint = draggedAnnotation.Location;
                                draggedAnnotation.Dragged(graphPoint);
                                DrawingPoint endPoint = draggedAnnotation.Location;
                                if (startPoint != endPoint)
                                    Viewer.DragSelectedAnnotationsAndNodes(draggedAnnotation, endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
                            }
                        } else {
                            draggedAnnotation.Dragged(graphPoint);
                        }
                    } else if (MouseDownElement is BaseNodeElement soloNode) {
                        soloNode.Dragged(graphPoint, Grid);
                    }

                    if (properties.IsMiddleButtonPressed)
                        ViewBeingDragged = true;
                    break;

                case DragOperation.Selection: {
                    DrawingRectangle zone = ComputeZone(selectionZoneOriginGraphPoint, graphPoint);
                    Viewer.CurrentSelectionNodes.Clear();
                    Viewer.CurrentSelectionNodes.UnionWith(Viewer.NodeElements.Where(node => node.IntersectsWithZone(zone, -20, -20)));
                    Viewer.UpdateSelection(ModifierFor(e.KeyModifiers));
                    Viewer.UpdateAnnotationLassoPreview(zone, ModifierFor(e.KeyModifiers));
                    Viewer.SelectionZone = zone;

                    if (properties.IsMiddleButtonPressed)
                        ViewBeingDragged = true;
                    break;
                }

                //Ports the DrawShape branch of ProductionGraphViewer_MouseMove (reference §1/§6): just the
                //rubber-band rectangle, recomputed from the origin every frame - no lasso preview/selection
                //math, unlike Selection above.
                case DragOperation.DrawShape:
                    Viewer.SelectionZone = ComputeZone(selectionZoneOriginGraphPoint, graphPoint);
                    if (properties.IsMiddleButtonPressed)
                        ViewBeingDragged = true;
                    break;
            }

            //Carried from Task 1's review (reference §2): only hard-limit the pan to Graph.Bounds while
            //nothing is being dragged, matching upstream's UpdateGraphBounds(MouseDownElement == null).
            if (ViewBeingDragged) {
                Viewport.PanTo(screenPoint, viewDragOriginGraphPoint, MouseDownElement is null ? Viewer.Graph.Bounds : null);
                FloatingPanelHost.Reposition();
            }

            InvalidateVisual();
        }

        //Ports ProductionGraphViewer_MouseUp (reference §1/§4/§6). A plain left click with no modifiers
        //forwards to the clicked node's own MouseUp routing (reference §4, upstream lines 258-269), same as a
        //plain right click does - a node click reaching here without a tab/error-notice claim now opens its
        //real edit panel (reference §8, Task 6's EditNode below). Annotation routing
        //(double-click already short-circuited in OnPointerPressed) mirrors Annotation_OnMouseUpLeft: a
        //tracked annotation (MouseDownElement claimed it because it was already selected) toggles/replaces via
        //HandleTrackedAnnotationMouseUp, an untracked one clicked without a drag selects singly - both only
        //while CurrentDragOperation is still None, since an Item-drag ending here (node or annotation) needs
        //no extra handling beyond the drag math OnPointerMoved already applied.
        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            AvaloniaPoint releaseScreenPoint = e.GetPosition(this);

            //Mirrors OnPointerPressed's chrome-swallow guard (reference §7): a press landing on the panel
            //already returns before claiming MouseDownElement/CurrentDragOperation, but without this the
            //matching release still reached the hit-test below and opened a node's context menu (or acted
            //on whatever's underneath) right through the panel's own chrome.
            if (FloatingPanelHost.IsOpen) {
                var releasedPoint = new DrawingPoint((int)releaseScreenPoint.X, (int)releaseScreenPoint.Y);
                if (FloatingPanelHost.Bounds.Contains(releasedPoint) || FloatingPanelHost.CompanionBounds.Contains(releasedPoint)) {
                    e.Handled = true;
                    return;
                }
            }

            DrawingPoint graphPoint = Viewport.ScreenToGraph(releaseScreenPoint);
            UpdateModifiers(e.KeyModifiers);

            switch (e.InitialPressMouseButton) {
                case MouseButton.Right:
                    //Mirrors BaseNodeElement.MouseUp's own wasDragged early-return (upstream lines 259-268):
                    //upstream's ProductionGraphViewer_MouseUp passes wasDragged = (currentDragOperation ==
                    //DragOperation.Item) straight into element.MouseUp, so a right-click release mid node-
                    //drag (reachable via a chorded left-drag-then-right-release) opens no menu, same as the
                    //Selection case just below it already didn't. Ports the background menu (reference §4a,
                    //"Add Item"/"Add Recipe" omitted - their P5 choosers don't exist yet, task 12's job) and
                    //the annotation right-click menu (reference §4f) alongside the existing node menu.
                    if (ViewBeingDragged) {
                        ViewBeingDragged = false;
                    } else if (CurrentDragOperation is not (DragOperation.Selection or DragOperation.Item)) {
                        GraphElement? rightClickTarget = HitTest(e.GetPosition(this));
                        if (rightClickTarget is BaseNodeElement rightClickedNode)
                            ShowContextMenu(rightClickedNode.MouseUpRight(graphPoint));
                        else if (rightClickTarget is AnnotationElement rightClickedAnnotation)
                            ShowContextMenu(rightClickedAnnotation.BuildRightClickMenu());
                        else if (CurrentDragOperation == DragOperation.None)
                            ShowContextMenu(BuildBackgroundMenu(graphPoint));
                    }
                    break;

                case MouseButton.Middle:
                    ViewBeingDragged = false;
                    break;

                case MouseButton.Left:
                    if (CurrentDragOperation == DragOperation.DrawShape) {
                        FinishDrawShape();
                    } else if (CurrentDragOperation == DragOperation.Selection) {
                        SelectionModifier lassoModifier = ModifierFor(e.KeyModifiers);
                        Viewer.CommitLassoSelection(lassoModifier);
                        Viewer.CommitAnnotationLassoSelection(Viewer.SelectionZone ?? new DrawingRectangle(), lassoModifier);
                        Viewer.SelectionZone = null;
                    } else if (DraggedLink is DraggedLinkElement releaseGhost) {
                        //Ports the "anything else" branch's !viewBeingDragged gate (reference §1's MouseUp,
                        //upstream lines 966-969): a release that coincides with an active pan doesn't end the
                        //drag - MouseDown's click-to-drop path (above) is what finally ends it later.
                        if (!ViewBeingDragged)
                            releaseGhost.MouseUp(graphPoint, MouseButton.Left);
                    } else if (CurrentDragOperation == DragOperation.None && MouseDownElement is BaseNodeElement clickedNode) {
                        SelectionModifier modifier = ModifierFor(e.KeyModifiers);
                        if (modifier == SelectionModifier.Remove) {
                            Viewer.SelectedNodes.Remove(clickedNode);
                            clickedNode.Highlighted = false;
                        } else if (modifier == SelectionModifier.Add) {
                            if (clickedNode.Highlighted)
                                Viewer.SelectedNodes.Remove(clickedNode);
                            else
                                Viewer.SelectedNodes.Add(clickedNode);
                            clickedNode.Highlighted = !clickedNode.Highlighted;
                        } else if (!ViewBeingDragged) {
                            clickedNode.MouseUpLeft(graphPoint);
                        }
                    } else if (CurrentDragOperation == DragOperation.None && MouseDownElement is AnnotationElement trackedAnnotation) {
                        Viewer.HandleTrackedAnnotationMouseUp(trackedAnnotation, ModifierFor(e.KeyModifiers), ViewBeingDragged);
                    } else if (CurrentDragOperation == DragOperation.None && MouseDownElement is null && !ViewBeingDragged
                        && HitTest(e.GetPosition(this)) is AnnotationElement unselectedAnnotation) {
                        Viewer.SelectSingleAnnotation(unselectedAnnotation, ModifierFor(e.KeyModifiers));
                    }

                    CurrentDragOperation = DragOperation.None;
                    MouseDownElement = null;
                    break;
            }

            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        //Ports Annotation_FinishDrawShape (reference §1/§6): commits the drawn rectangle (30-graph-unit
        //minimum per axis) or falls back to a default-sized shape centered at the click if the drag was too
        //small.
        private void FinishDrawShape() {
            const int minDrawSize = 30;
            DrawingRectangle zone = Viewer.SelectionZone ?? new DrawingRectangle();

            AnnotationElement shape;
            if (zone.Width >= minDrawSize || zone.Height >= minDrawSize) {
                int w = Math.Max(zone.Width, minDrawSize);
                int h = Math.Max(zone.Height, minDrawSize);
                var center = new DrawingPoint(zone.Left + (w / 2), zone.Top + (h / 2));
                shape = new ShapeAnnotationElement(center, w, h);
            } else {
                shape = new ShapeAnnotationElement(selectionZoneOriginGraphPoint);
            }
            Viewer.AddAnnotationElement(shape);

            inDrawShapeMode = false;
            Viewer.SelectionZone = null;
        }

        //Ports AddShapeAnnotation's arming half (reference §6): the background menu's "Add Shape" entry point
        //since there's no toolbar button or shortcut for it.
        internal void BeginDrawShape(DrawingPoint graphPoint) {
            inDrawShapeMode = true;
            selectionZoneOriginGraphPoint = graphPoint;
            Viewer.SelectionZone = new DrawingRectangle();
            RequestRedraw();
        }

        //Ports AddTextAnnotation (reference §6): creates the element selected, opens its properties dialog
        //immediately - OK clears the rest of the selection but keeps this one selected, Cancel deletes the
        //just-created element outright.
        internal async Task AddTextAnnotationAsync(DrawingPoint graphPoint) {
            var element = new TextAnnotationElement(graphPoint) { IsSelected = true };
            Viewer.AddAnnotationElement(element);
            RequestRedraw();

            bool confirmed = await ShowTextPropertiesDialogAsync(element).ConfigureAwait(true);
            if (confirmed) {
                Viewer.ClearSelection();
                Viewer.SelectedAnnotations.Add(element);
                element.IsSelected = true;
            } else {
                Viewer.RemoveAnnotationElement(element);
            }
            RequestRedraw();
        }

        //Ports Annotation_AppendContextMenuItems (reference §4a), now including Add Item/Add Recipe through
        //the real IRChooserPanel family (reference §2/§8/§10).
        private List<MenuEntry> BuildBackgroundMenu(DrawingPoint graphPoint) => [
            MenuEntry.Item("Add Item", () => AddItemAsync(graphPoint)),
            MenuEntry.Item("Add Recipe", () => AddRecipeAsync(graphPoint)),
            MenuEntry.Divider,
            MenuEntry.Item("Add Text", () => Async.Fire(AddTextAnnotationAsync(graphPoint), nameof(AddTextAnnotationAsync))),
            MenuEntry.Item("Add Shape", () => BeginDrawShape(graphPoint)),
        ];

        //Ports AddItem (reference §4a/§10): opens the real ItemChooserPanel over every available item.
        //Picking one starts the two-stage AddNewNode(Disconnected) flow below (RecipeChooserPanel with that
        //item as KeyItem) exactly as upstream's own Add Item entry point does - it never creates a node
        //itself, only ever leads to AddNewNode.
        internal void AddItemAsync(DrawingPoint graphPoint) {
            if (Viewer.Context.DCache is not DataCache cache)
                return;

            var panel = new ItemChooserPanel(cache, ChooserSettings);
            panel.Initialize();
            panel.ItemRequested += (_, e) => AddNewNode(graphPoint, e.Item, graphPoint, NewNodeType.Disconnected, null, Viewer.Graph.DefaultNodeDirection, new FRange(0, 0, true));
            FloatingPanelHost.Show(panel, graphPoint);
        }

        //Ports the background menu's "Add Recipe" entry (reference §4a/§10, upstream ProductionGraphViewer_
        //MouseUp lines 909-916): unlike Add Item, this skips the item chooser and opens RecipeChooserPanel
        //directly with an empty KeyItem, so every recipe in the cache is offered with no footer alt-node
        //buttons (a KeyItem-less RecipeChooserPanel hides them, per its ctor's else branch).
        internal void AddRecipeAsync(DrawingPoint graphPoint) =>
            AddNewNode(graphPoint, default, graphPoint, NewNodeType.Disconnected, null, Viewer.Graph.DefaultNodeDirection, new FRange(0, 0, true));

        private Services.AppSettings? fallbackChooserSettings;
        private Services.AppSettings ChooserSettings => AnnotationSettings ?? (fallbackChooserSettings ??= new Services.AppSettings());

        //Bundles AddNewNode's closure-captured locals (reference §3/§10) so ProcessNodeRequest/FinalizeNode/
        //the spoil-plant sub-pickers below can share them without upstream's nested-local-function shape.
        //OffsetLocationToItemTabLevel ports upstream's own AddNewNode parameter of the same name (reference
        //§3, ProductionGraphViewer.cs:206/413-414): upstream passes true from both DraggedLinkElement call
        //sites only (HandleNewNodeRequestedAsync here) and leaves it false everywhere else (Add Item/Add
        //Recipe), so a link-drag-created node's tab lands at the drop point instead of its center.
        private readonly record struct NewNodeContext(ItemQualityPair BaseItem, DrawingPoint NewLocation, NewNodeType OuterNodeType, BaseNodeElement? OriginElement, NodeDirection NewNodeDirection, bool OffsetLocationToItemTabLevel);

        //Ports AddNewNode's RecipeChooserPanel half (reference §3/§10) for every call site that reaches it:
        //Add Item's two-stage follow-up, Add Recipe, and the link-drag EndDrag outcome (HandleNewNodeRequestedAsync,
        //below). originElement/newNodeDirection are unused by FinalizeNode for the two disconnected entry
        //points above, matching upstream's own "no origin" guard. The Spoil/Plant-Down multi-origin branches
        //need to swap in a follow-up ItemChooserPanel without running this panel's own close cleanup early
        //(reference §2's RequiresItemSelection close reason) - suppressNextClose exists solely for that: it's
        //set true immediately before the replacement panel opens (synchronously, before FloatingPanelHost.Show
        //returns), and read+cleared by the very next PanelClosed this panel raises.
        private void AddNewNode(DrawingPoint drawOrigin, ItemQualityPair baseItem, DrawingPoint newLocation, NewNodeType nNodeType, BaseNodeElement? originElement, NodeDirection newNodeDirection, FRange tempRange, bool offsetLocationToItemTabLevel = false) {
            if (Viewer.Context.DCache is not DataCache cache)
                return;

            var panel = new RecipeChooserPanel(cache, ChooserSettings, baseItem, tempRange, nNodeType);
            panel.Initialize();
            var ctx = new NewNodeContext(baseItem, newLocation, nNodeType, originElement, newNodeDirection, offsetLocationToItemTabLevel);
            bool suppressNextClose = false;

            panel.RecipeRequested += (_, e) => {
                if (e.NodeType == NodeType.Spoil && e.Direction == NodeDirection.Down && baseItem.Item is IItem spoilItem && spoilItem.SpoilOrigins.Count > 1) {
                    suppressNextClose = true;
                    OpenSpoilOriginPicker(drawOrigin, spoilItem, ctx);
                } else if (e.NodeType == NodeType.Plant && e.Direction == NodeDirection.Down && baseItem.Item is IItem plantItem && plantItem.PlantOrigins.Count > 1) {
                    suppressNextClose = true;
                    OpenPlantOriginPicker(drawOrigin, plantItem, ctx);
                } else {
                    ProcessNodeRequest(ctx, e.NodeType, e.Recipe, e.Direction);
                }
            };
            panel.PanelClosed += (_, e) => {
                if (suppressNextClose) {
                    suppressNextClose = false;
                    return;
                }
                if (e.Reason != IRChooserPanel.ChooserPanelCloseReason.RequiresItemSelection)
                    FinishChooserFlow();
            };

            FloatingPanelHost.Show(panel, drawOrigin);
        }

        //Spoil/Plant footer buttons hand back NodeType.Recipe==false requests carrying no item of their own
        //(reference §2) - GraphCanvasControl.baseItem, captured in NewNodeContext, is upstream's real source
        //for what gets spoiled/planted, matching AddNewNode's own closure-captured baseItem.
        private void OpenSpoilOriginPicker(DrawingPoint drawOrigin, IItem spoilItem, NewNodeContext ctx) {
            if (Viewer.Context.DCache is not DataCache cache)
                return;
            var subPicker = new ItemChooserPanel(cache, ChooserSettings, spoilItem.SpoilOrigins);
            subPicker.Initialize();
            subPicker.ItemRequested += (_, e) => {
                if (e.Item.Item is IItem originItem && e.Item.Quality is IQuality originQuality)
                    ProcessCreatedNode(ctx, Viewer.Session.Editor.CreateSpoilNode(new ItemQualityPair(originItem, originQuality), spoilItem, ctx.NewLocation));
            };
            subPicker.PanelClosed += (_, _) => FinishChooserFlow();
            FloatingPanelHost.Show(subPicker, drawOrigin);
        }

        //Ports the Plant sub-picker's own quality rule (reference §2/§10, upstream lines 345-351): the newly
        //planted node always resolves at the cache's DefaultQuality, ignoring whatever quality the origin item
        //was picked at - not a port bug, upstream does the same.
        private void OpenPlantOriginPicker(DrawingPoint drawOrigin, IItem plantItem, NewNodeContext ctx) {
            if (Viewer.Context.DCache is not DataCache cache)
                return;
            var subPicker = new ItemChooserPanel(cache, ChooserSettings, plantItem.PlantOrigins);
            subPicker.Initialize();
            subPicker.ItemRequested += (_, e) => {
                if (e.Item.Item?.PlantResult is IPlantProcess plantProcess && cache.DefaultQuality is IQuality defaultQuality)
                    ProcessCreatedNode(ctx, Viewer.Session.Editor.CreatePlantNode(plantProcess, defaultQuality, ctx.NewLocation));
            };
            subPicker.PanelClosed += (_, _) => FinishChooserFlow();
            FloatingPanelHost.Show(subPicker, drawOrigin);
        }

        //Ports ProcessNodeRequest's node-creation switch (reference §3/§10, upstream lines 265-393): every
        //NodeType a RecipeChooserPanel can request except the Spoil/Plant-Down multi-origin cases, which
        //AddNewNode routes to the sub-pickers above before this ever runs. Assembler/fuel auto-selection for
        //a Recipe pick is ApplyAssemblerFuelAutoSelection's job, below.
        private void ProcessNodeRequest(NewNodeContext ctx, NodeType nodeType, RecipeQualityPair recipe, NodeDirection direction) {
            if (Viewer.Context.DCache is not DataCache cache)
                return;

            IItem? itemNN = ctx.BaseItem.Item;
            IQuality? qualityNN = ctx.BaseItem.Quality;
            NodeId newNodeId = NodeId.Invalid;

            switch (nodeType) {
                case NodeType.Consumer:
                    newNodeId = Viewer.Session.Editor.CreateConsumerNode(ctx.BaseItem, ctx.NewLocation);
                    break;
                case NodeType.Supplier:
                    newNodeId = Viewer.Session.Editor.CreateSupplierNode(ctx.BaseItem, ctx.NewLocation);
                    break;
                case NodeType.Passthrough:
                    newNodeId = Viewer.Session.Editor.CreatePassthroughNode(ctx.BaseItem, ctx.NewLocation);
                    break;
                case NodeType.Spoil when direction == NodeDirection.Up && itemNN?.SpoilResult is IItem spoilOutput:
                    newNodeId = Viewer.Session.Editor.CreateSpoilNode(ctx.BaseItem, spoilOutput, ctx.NewLocation);
                    break;
                case NodeType.Spoil when direction == NodeDirection.Down && itemNN is not null && qualityNN is not null && itemNN.SpoilOrigins.Count == 1:
                    newNodeId = Viewer.Session.Editor.CreateSpoilNode(new ItemQualityPair(itemNN.SpoilOrigins.First(), qualityNN), itemNN, ctx.NewLocation);
                    break;
                case NodeType.Plant when direction == NodeDirection.Up && itemNN?.PlantResult is IPlantProcess plantUp && qualityNN is not null:
                    newNodeId = Viewer.Session.Editor.CreatePlantNode(plantUp, qualityNN, ctx.NewLocation);
                    break;
                case NodeType.Plant when direction == NodeDirection.Down && itemNN is not null && itemNN.PlantOrigins.Count == 1:
                    IItem plantOrigin = itemNN.PlantOrigins.First();
                    if (plantOrigin.PlantResult is IPlantProcess plantDown && cache.DefaultQuality is IQuality defaultPlantQuality)
                        newNodeId = Viewer.Session.Editor.CreatePlantNode(plantDown, defaultPlantQuality, ctx.NewLocation);
                    break;
                case NodeType.Recipe when recipe:
                    newNodeId = Viewer.Session.Editor.CreateRecipeNode(recipe, ctx.NewLocation);
                    ApplyAssemblerFuelAutoSelection(ctx, newNodeId, recipe.Recipe!, itemNN);
                    break;
            }

            ProcessCreatedNode(ctx, newNodeId);
        }

        //Ports ProcessNodeRequest's assembler/fuel auto-selection for a plain recipe pick (reference §8,
        //upstream lines 367-391): only runs when the picked recipe doesn't already use baseItem as an
        //ingredient/product on its own - the whole point is wiring baseItem in as fuel when the recipe
        //otherwise has no use for it. Falls back to no-op (instead of upstream's unconditional .First(), which
        //can throw) when no assembler option actually fuels on baseItem; a defensive divergence, not a
        //behavior upstream ever intentionally exercises.
        private void ApplyAssemblerFuelAutoSelection(NewNodeContext ctx, NodeId newNodeId, IRecipe recipeDef, IItem? itemNN) {
            if (itemNN is null || !newNodeId.IsValid)
                return;
            bool itemAlreadyInRecipe = recipeDef.IngredientSet.ContainsKey(itemNN) || recipeDef.ProductSet.ContainsKey(itemNN);
            bool needsAutoSelect =
                (ctx.OuterNodeType == NewNodeType.Consumer && !recipeDef.IngredientSet.ContainsKey(itemNN)) ||
                (ctx.OuterNodeType == NewNodeType.Supplier && !recipeDef.ProductSet.ContainsKey(itemNN)) ||
                (ctx.OuterNodeType == NewNodeType.Disconnected && ctx.BaseItem && !itemAlreadyInRecipe);
            if (!needsAutoSelect)
                return;
            if (Viewer.Session.Editor.RequestNodeController(newNodeId) is not RecipeNodeController controller)
                return;
            if (Viewer.Graph.DefaultAssemblerQuality is not IQuality defAssyQuality)
                return;

            AssemblerSelector.Style style = Viewer.Graph.AssemblerSelector.DefaultSelectionStyle switch {
                AssemblerSelector.Style.Best or AssemblerSelector.Style.BestBurner or AssemblerSelector.Style.BestNonBurner => AssemblerSelector.Style.BestBurner,
                _ => AssemblerSelector.Style.WorstBurner,
            };
            List<IAssembler> assemblerOptions = AssemblerSelector.GetOrderedAssemblerList(recipeDef, style);

            if (ctx.OuterNodeType == NewNodeType.Consumer || (ctx.OuterNodeType == NewNodeType.Disconnected && assemblerOptions.Any(a => a.Fuels.Contains(itemNN)))) {
                if (assemblerOptions.FirstOrDefault(a => a.Fuels.Contains(itemNN)) is IAssembler fuelAssembler) {
                    controller.SetAssembler(new AssemblerQualityPair(fuelAssembler, defAssyQuality));
                    controller.SetFuel(itemNN);
                }
            } else if (itemNN.FuelOrigin is IItem fuelOrigin && (ctx.OuterNodeType == NewNodeType.Supplier || (ctx.OuterNodeType == NewNodeType.Disconnected && assemblerOptions.Any(a => a.Fuels.Contains(fuelOrigin))))) {
                if (assemblerOptions.FirstOrDefault(a => a.Fuels.Contains(fuelOrigin)) is IAssembler fuelOriginAssembler) {
                    controller.SetAssembler(new AssemblerQualityPair(fuelOriginAssembler, defAssyQuality));
                    controller.SetFuel(fuelOrigin);
                }
            }
        }

        //Ports FinalizeNodePosition's direction/link half (reference §3): direction only gets set when an
        //origin element exists (the two disconnected entry points already got Graph.DefaultNodeDirection from
        //node creation itself), and the link always runs through baseItem in the direction the drag started
        //from, regardless of which NodeType the chooser ultimately produced - matching upstream exactly.
        //Also ports FinalizeNodePosition's tab-level Y-offset (upstream lines 413-414): only when
        //OffsetLocationToItemTabLevel is set (the link-drag flow) does the drop point land on the new node's
        //tab row instead of its center - Consumer shifts the node down by half its height (so its top-edge
        //input tab reaches the drop point), Supplier shifts it up by half (so its bottom-edge output tab
        //does), then the whole offset flips sign for a Down-directed node, matching upstream's own
        //direction-relative tab placement (UpdateTabOrder above puts input/output tabs at ±Height/2 depending
        //on NodeDirection).
        private void ProcessCreatedNode(NewNodeContext ctx, NodeId newNodeId) {
            if (!newNodeId.IsValid)
                return;

            if (ctx.OriginElement is not null)
                Viewer.Session.Editor.SetDirection(newNodeId, ctx.NewNodeDirection);

            if (ctx.OffsetLocationToItemTabLevel && Viewer.NodeElementDictionary.TryGetValue(newNodeId, out BaseNodeElement? tabAlignElement)) {
                int yoffset = ctx.OuterNodeType == NewNodeType.Consumer ? -tabAlignElement.Height / 2
                    : ctx.OuterNodeType == NewNodeType.Supplier ? tabAlignElement.Height / 2
                    : 0;
                yoffset *= ctx.NewNodeDirection == NodeDirection.Up ? 1 : -1;
                if (yoffset != 0)
                    Viewer.Session.Editor.SetLocation(newNodeId, new DrawingPoint(ctx.NewLocation.X, ctx.NewLocation.Y + yoffset));
            }

            if (ctx.OuterNodeType == NewNodeType.Consumer && ctx.OriginElement is not null)
                Viewer.Session.Editor.CreateLink(ctx.OriginElement.ViewModel.Id, newNodeId, ctx.BaseItem);
            else if (ctx.OuterNodeType == NewNodeType.Supplier && ctx.OriginElement is not null)
                Viewer.Session.Editor.CreateLink(newNodeId, ctx.OriginElement.ViewModel.Id, ctx.BaseItem);

            Viewer.Graph.UpdateNodeValues();
            Viewer.Graph.UpdateNodeStates(false);
            if (Viewer.NodeElementDictionary.TryGetValue(newNodeId, out BaseNodeElement? element))
                Viewer.SetSelection([element]);
            RequestRedraw();
        }

        //Ports the PanelClosed cleanup shared by AddNewNode and both spoil/plant sub-pickers (reference §3's
        //DisposeLinkDrag/Graph.UpdateNodeStates/Invalidate tail): a no-op DraggedLink dispose for the two
        //disconnected entry points (nothing to dispose), the real link-drag ghost teardown for
        //HandleNewNodeRequestedAsync's flow.
        private void FinishChooserFlow() {
            FloatingPanelHost.Close();
            DraggedLink?.Dispose();
            DraggedLink = null;
            Viewer.Graph.UpdateNodeStates(false);
            RequestRedraw();
        }

        //Ports AddNewNode's RecipeChooserPanel construction for the link-drag EndDrag outcome (reference §3/
        //§10, upstream lines 234-243): the dragged item's real temperature range via LinkChecker (the same
        //Core helper LinkChecker.IsPossibleConnection already uses for live link validity), replacing task 7's
        //producers/consumers approximation with upstream's exact RecipeMatchesKeyItem predicate.
        private void HandleNewNodeRequestedAsync(NewNodeLinkDragRequest request) {
            if (Viewer.Context.DCache is not DataCache cache || request.Item.Item is not IItem item)
                return;

            var tempRange = new FRange(0, 0, true);
            if (item is IFluid fluid && fluid.IsTemperatureDependent) {
                LinkType direction = request.NodeType == NewNodeType.Consumer ? LinkType.Output : LinkType.Input;
                tempRange = LinkChecker.GetTemperatureRange(fluid, request.OriginElement.ViewModel, direction, true, Viewer.Session);
            }

            DrawingPoint drawOrigin = Viewport.ScreenToGraph(request.ScreenPoint);
            AddNewNode(drawOrigin, request.Item, request.EndpointLocation, request.NodeType, request.OriginElement, request.Direction, tempRange, offsetLocationToItemTabLevel: true);
        }

        //Test-only seams (reference §6's "double-click opens dialog (headless-testable hook)"): stub these to
        //observe/short-circuit the real Avalonia-window calls below without a modal dialog blocking the test.
        internal Func<TextAnnotationElement, Task<bool>>? TextPropertiesDialogStub { get; set; }
        internal Func<ShapeAnnotationElement, Task<bool>>? ShapePropertiesDialogStub { get; set; }
        internal Action<AnnotationElement>? AnnotationPropertiesDialogStub { get; set; }

        private Task<bool> ShowTextPropertiesDialogAsync(TextAnnotationElement element) =>
            TextPropertiesDialogStub?.Invoke(element) ?? ShowRealTextPropertiesDialogAsync(element);

        private Task<bool> ShowShapePropertiesDialogAsync(ShapeAnnotationElement element) =>
            ShapePropertiesDialogStub?.Invoke(element) ?? ShowRealShapePropertiesDialogAsync(element);

        //AnnotationSettings threads MainWindow's AppSettings through to the dialogs' OK-saves-as-default write
        //(reference §6); GraphCanvasControl otherwise has no settings reference of its own (see MainWindow.
        //ApplyLoadedSettings for where this gets assigned).
        internal Services.AppSettings? AnnotationSettings { get; set; }

        private async Task<bool> ShowRealTextPropertiesDialogAsync(TextAnnotationElement element) {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new TextPropertiesWindow(element) { RequestRedraw = RequestRedraw, Settings = AnnotationSettings };
            bool? result = await dialog.ShowDialog<bool?>(owner).ConfigureAwait(true);
            return result == true;
        }

        private async Task<bool> ShowRealShapePropertiesDialogAsync(ShapeAnnotationElement element) {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new ShapePropertiesWindow(element) { RequestRedraw = RequestRedraw, Settings = AnnotationSettings };
            bool? result = await dialog.ShowDialog<bool?>(owner).ConfigureAwait(true);
            return result == true;
        }

        //Ports Annotation_OnMouseDownDoubleClick's dialog-open half (reference §6), also the target of the
        //right-click "Properties" menu item via GraphViewer.ShowAnnotationPropertiesDialog. Unlike AddText's
        //creation flow there's no create/delete-on-cancel here - the window already reverted its own live
        //preview before closing (see Views/*PropertiesWindow), this just requests a final repaint.
        private void ShowAnnotationProperties(AnnotationElement annotation) {
            if (AnnotationPropertiesDialogStub is { } stub) {
                stub(annotation);
                return;
            }
            _ = annotation switch {
                TextAnnotationElement text => ShowExistingTextPropertiesAsync(text),
                ShapeAnnotationElement shape => ShowExistingShapePropertiesAsync(shape),
                _ => Task.CompletedTask
            };
        }

        private async Task ShowExistingTextPropertiesAsync(TextAnnotationElement element) {
            await ShowTextPropertiesDialogAsync(element).ConfigureAwait(true);
            RequestRedraw();
        }

        private async Task ShowExistingShapePropertiesAsync(ShapeAnnotationElement element) {
            await ShowShapePropertiesDialogAsync(element).ConfigureAwait(true);
            RequestRedraw();
        }

        //Ports the Shift-toggle block shared by ProductionGraphViewer_KeyDown/KeyUp (reference §2 and §7's
        //keyboard map): re-anchors Grid.DragOrigin to the aligned MouseDownElement location at the moment
        //of toggling (not at drag start), then immediately re-drags the leader once so the lock takes
        //effect without waiting for the next pointer move. Also Escape-cancels an in-progress link drag, in
        //addition to upstream's right-click-only cancel: docs/interaction-reference.md §7 notes upstream has
        //no Escape handling for link drags at all (only for inDrawShapeMode's cancel, now wired below) - the
        //link-drag cancel is a deliberate Mac-convention addition, not a strict port.
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);

            //Ports SubwindowOpen gating WASD (reference §7's ClearFloatingControls/SubwindowOpen model),
            //generalized to every canvas shortcut while a panel holds focus - Escape is the one key a
            //panel doesn't itself need to handle, so it closes the panel here same as upstream's own
            //Escape/inDrawShapeMode case just below.
            if (FloatingPanelHost.IsOpen) {
                if (e.Key == Key.Escape) {
                    FloatingPanelHost.Close();
                    e.Handled = true;
                }
                return;
            }

            UpdateModifiers(e.KeyModifiers);
            RefreshLassoPreviewOnModifierChange(e.KeyModifiers);
            if (e.Key == Key.Escape && DraggedLink is not null) {
                DisposeLinkDrag();
                e.Handled = true;
            }
            //Ports Annotation_OnKeyDown (reference §6/§7): Escape while inDrawShapeMode cancels the pending
            //shape without creating anything - the only Escape handling upstream has besides the link-drag
            //addition above.
            if (e.Key == Key.Escape && inDrawShapeMode) {
                inDrawShapeMode = false;
                CurrentDragOperation = DragOperation.None;
                Viewer.SelectionZone = null;
                e.Handled = true;
            }
            if (CurrentDragOperation == DragOperation.None && HandleClipboardKey(e.Key, e.KeyModifiers))
                e.Handled = true;
            if (HandleMovementKey(e.Key, e.KeyModifiers))
                e.Handled = true;
            ApplyAxisLockModifier(e.KeyModifiers);
            InvalidateVisual();
        }

        //Ports ProductionGraphViewer_KeyDown's Ctrl+C/X/V block (reference §5/§7): Cmd replaces Ctrl on macOS
        //per the Cmd-mapping note (PlatformModifiers.Primary; Linux keeps Ctrl). Paste's origin is the
        //cursor's last-known graph position (hoverScreenPoint,
        //upstream's PointToClient(Cursor.Position)), falling back to the viewport center when the pointer
        //has never crossed the control.
        private bool HandleClipboardKey(Key key, KeyModifiers modifiers) {
            if (!modifiers.HasFlag(PlatformModifiers.Primary))
                return false;

            if (key == Key.C || key == Key.X) {
                Viewer.Context.SetClipboardText?.Invoke(key == Key.X ? NodeClipboard.Cut(Viewer) : NodeClipboard.Copy(Viewer));
                return true;
            }
            if (key == Key.V) {
                if (Viewer.Context.DCache is DataCache cache && Viewer.Context.GetClipboardText?.Invoke() is string clipboardText) {
                    AvaloniaPoint cursorScreenPoint = hoverScreenPoint ?? new AvaloniaPoint(Viewport.Width / 2, Viewport.Height / 2);
                    NodeClipboard.Paste(Viewer, cache, clipboardText, Viewport.ScreenToGraph(cursorScreenPoint));
                }
                return true;
            }
            return false;
        }

        //Ports ProductionGraphViewer_KeyUp's Delete case (reference §7): annotations-if-any-selected, else
        //nodes - TryDeleteSelection/TryDeleteSelectedNodes both carry their own >10 confirm gate.
        protected override void OnKeyUp(KeyEventArgs e) {
            base.OnKeyUp(e);
            if (FloatingPanelHost.IsOpen)
                return;

            UpdateModifiers(e.KeyModifiers);
            RefreshLassoPreviewOnModifierChange(e.KeyModifiers);
            if (CurrentDragOperation == DragOperation.None && e.Key == Key.Delete) {
                if (Viewer.SelectedAnnotations.Count > 0)
                    Viewer.TryDeleteSelection();
                else
                    Viewer.TryDeleteSelectedNodes();
                e.Handled = true;
            }
            ApplyAxisLockModifier(e.KeyModifiers);
            InvalidateVisual();
        }

        //Ports the lasso re-preview shared by ProductionGraphViewer_KeyDown/KeyUp (upstream lines 1125-1128,
        //1151-1154): a modifier key changing mid-lasso re-runs the node and annotation preview so it still
        //matches what OnPointerReleased will commit, even if the modifier changes again before the button
        //goes up (reference: CommitLassoSelection reads the modifier fresh at release time, not the last
        //preview's).
        private void RefreshLassoPreviewOnModifierChange(KeyModifiers modifiers) {
            if (CurrentDragOperation != DragOperation.Selection || Viewer.SelectionZone is not DrawingRectangle zone)
                return;

            Viewer.UpdateSelection(ModifierFor(modifiers));
            Viewer.UpdateAnnotationLassoPreview(zone, ModifierFor(modifiers));
        }

        private void UpdateModifiers(KeyModifiers modifiers) => IsPassthroughBusModifierHeld = modifiers.HasFlag(PlatformModifiers.Primary);

        private static MouseButton ButtonFrom(PointerPointProperties properties) =>
            properties.IsRightButtonPressed ? MouseButton.Right
            : properties.IsMiddleButtonPressed ? MouseButton.Middle
            : MouseButton.Left;

        private void ApplyAxisLockModifier(KeyModifiers modifiers) {
            bool lockDragAxis = modifiers.HasFlag(KeyModifiers.Shift);
            if (Grid.LockDragToAxis == lockDragAxis)
                return;

            Grid.LockDragToAxis = lockDragAxis;
            Grid.DragOrigin = Grid.AlignToGrid(MouseDownElement?.Location ?? new DrawingPoint());
            if (CurrentDragOperation == DragOperation.Item && MouseDownElement is BaseNodeElement node && hoverScreenPoint is AvaloniaPoint hover)
                node.Dragged(Viewport.ScreenToGraph(hover), Grid);
        }

        //Ports ProcessCmdKey's arrow/WASD block (reference §2/§7): moveUnit/panUnit step sizes and the
        //Shift large-step multiplier are copied verbatim. SubwindowOpen doesn't exist in this port yet (no
        //floating panel can be open), so WASD panning isn't gated on it the way upstream's is.
        private bool HandleMovementKey(Key key, KeyModifiers modifiers) {
            bool shift = modifiers.HasFlag(KeyModifiers.Shift);
            int moveUnit = Grid.CurrentGridUnit > 0 ? Grid.CurrentGridUnit : 6;
            double panUnit = 10 / Viewport.ViewScale;
            if (shift) {
                moveUnit = Grid.CurrentMajorGridUnit > Grid.CurrentGridUnit ? Grid.CurrentMajorGridUnit : moveUnit * 4;
                panUnit *= 5;
            }

            switch (key) {
                case Key.Left: MoveSelection(-moveUnit, 0); return true;
                case Key.Right: MoveSelection(moveUnit, 0); return true;
                case Key.Up: MoveSelection(0, -moveUnit); return true;
                case Key.Down: MoveSelection(0, moveUnit); return true;
                case Key.W: Pan(0, (int)panUnit); return true;
                case Key.A: Pan((int)panUnit, 0); return true;
                //Primary-modifier-gated so Cmd+S/Ctrl+S reaches Save (the NativeMenu KeyGesture) instead of
                //also panning the view (reference: docs/upstream-divergences.md's now-retired WASD/Cmd+S entry).
                case Key.S when !modifiers.HasFlag(PlatformModifiers.Primary): Pan(0, -(int)panUnit); return true;
                case Key.D: Pan(-(int)panUnit, 0); return true;
                default: return false;
            }
        }

        //Ports the selectedNodes/selectedAnnotations halves of ProcessCmdKey's arrow branch - both move by
        //the same raw dx/dy, annotations are never grid-snapped anywhere in upstream.
        private void MoveSelection(int dx, int dy) {
            foreach (BaseNodeElement node in Viewer.SelectedNodes)
                node.SetLocation(new DrawingPoint(node.X + dx, node.Y + dy));
            foreach (AnnotationElement annotation in Viewer.SelectedAnnotations) {
                annotation.X += dx;
                annotation.Y += dy;
            }
        }

        //WASD panning always hard-limits to Graph.Bounds in upstream (its UpdateGraphBounds() call there
        //takes no argument, i.e. limitView defaults true) - unlike the mouse-drag pan path, it never
        //coexists with an object drag, so there's no MouseDownElement condition to thread through here.
        private void Pan(int dx, int dy) {
            Viewport.ViewOffset = new DrawingPoint(Viewport.ViewOffset.X + dx, Viewport.ViewOffset.Y + dy);
            Viewport.UpdateGraphBounds(Viewer.Graph.Bounds);
            FloatingPanelHost.Reposition();
        }

        //Ports the Alt/Ctrl(Cmd) three-way split shared by UpdateSelection and MouseUp (reference §1) - Cmd
        //replaces Ctrl on macOS per this phase's plan (docs/upstream-divergences.md); Linux keeps Ctrl.
        private static SelectionModifier ModifierFor(KeyModifiers modifiers) =>
            modifiers.HasFlag(KeyModifiers.Alt) ? SelectionModifier.Remove
            : modifiers.HasFlag(PlatformModifiers.Primary) ? SelectionModifier.Add
            : SelectionModifier.Replace;

        private static DrawingRectangle ComputeZone(DrawingPoint origin, DrawingPoint current) =>
            new(Math.Min(origin.X, current.X), Math.Min(origin.Y, current.Y), Math.Abs(origin.X - current.X), Math.Abs(origin.Y - current.Y));

        public override void Render(DrawingContext context) {
            base.Render(context);
            context.Custom(new DrawOperation(this, new Rect(Bounds.Size)));
        }

        public void Render(SKCanvas canvas) {
            //Ports Grid.Paint's draggedNodeActive gate (ProductionGraphViewer.cs:691): the locked-axis red
            //line only draws while an Item drag is actually dragging a node, matching upstream's
            //`(currentDragOperation == DragOperation.Item) ? MouseDownElement as BaseNodeElement : null`.
            bool draggedNodeActive = CurrentDragOperation == DragOperation.Item && MouseDownElement is BaseNodeElement;
            Viewer.Paint(canvas, fullGraph: false, draggedNodeActive: draggedNodeActive, draggedLink: DraggedLink);

            //everything below draws directly on screen space, matching upstream's ResetTransform() before
            //ToolTipRenderer.Paint/the paused-border rect (reference §3 steps 9-12); tooltip gated on
            //!ViewBeingDragged the same way upstream gates the live hover tooltip, and drawn before the
            //paused border so the border stays topmost like upstream's draw order.
            if (Viewer.TooltipsEnabled && !ViewBeingDragged && hoverScreenPoint is AvaloniaPoint hover)
                DrawHoverTooltip(canvas, hover);

            if (Viewer.Graph.PauseUpdates)
                Viewer.PaintPausedBorder(canvas);
        }

        private void DrawHoverTooltip(SKCanvas canvas, AvaloniaPoint hover) {
            GraphElement? element = HitTest(hover);
            if (element is null)
                return;

            foreach (TooltipInfo tooltip in element.GetToolTips(Viewport.ScreenToGraph(hover)))
                FloatingTooltipRenderer.Draw(canvas, tooltip);
        }

        //Ports GetNodeAtPoint's node-first precedence plus GetAnnotationAtPoint (reference §6): nodes always
        //win over annotations on overlap, mirroring ProductionGraphViewer_MouseDown/Up's probe order.
        public GraphElement? HitTest(AvaloniaPoint screenPoint) {
            DrawingPoint graphPoint = Viewport.ScreenToGraph(screenPoint);
            return (GraphElement?)GetNodeAtPoint(graphPoint) ?? AnnotationLoader.GetAnnotationAtPoint(Annotations, graphPoint);
        }

        internal BaseNodeElement? GetNodeAtPoint(DrawingPoint point) {
            for (int i = NodeElements.Count - 1; i >= 0; i--) {
                BaseNodeElement element = NodeElements[i];
                var roughZone = new DrawingRectangle(element.X - (element.Width / 2) - 50, element.Y - (element.Height / 2) - 50, element.Width + 100, element.Height + 100);
                if (roughZone.Contains(point) && element.ContainsPoint(point))
                    return element;
            }
            return null;
        }

        private sealed class DrawOperation(GraphCanvasControl owner, Rect bounds) : ICustomDrawOperation {
            public Rect Bounds { get; } = bounds;

            public bool HitTest(AvaloniaPoint p) => Bounds.Contains(p);
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context) {
                ISkiaSharpApiLeaseFeature? leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature is null)
                    return;

                using ISkiaSharpApiLease lease = leaseFeature.Lease();
                owner.Render(lease.SkCanvas);
            }
        }
    }
}
