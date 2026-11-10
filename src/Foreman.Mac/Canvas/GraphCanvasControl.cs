using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCanvas = Avalonia.Controls.Canvas;
using AvaloniaPoint = Avalonia.Point;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using KitMotion = Foreman.Mac.Kit.Motion;

namespace Foreman.Mac.Canvas {
    //Ports upstream's NewNodeType (reference §3/§10's AddNewNode/AddItem call sites).
    public enum NewNodeType { Disconnected, Supplier, Consumer }

    //Ports the chooser-dependent tail of DraggedLinkElement.EndDrag (reference §3's "exactly one end bound,
    public readonly record struct NewNodeLinkDragRequest(NewNodeType NodeType, ItemQualityPair Item, AvaloniaPoint ScreenPoint, DrawingPoint EndpointLocation, BaseNodeElement OriginElement, NodeDirection Direction);

    //Ports ProductionGraphViewer's OnPaint transform setup, MouseWheel zoom, middle/right-drag pan,
    //GetNodeAtPoint hit-testing, and MouseMove's per-frame hover-tooltip recompute (reference §6); the paint
    //pipeline itself (GetPaintingOrder, LOD/dynamic-link-width style selection, link rendering) is
    //GraphViewer's job (reference §3, §8.12) - this control just hosts the Avalonia surface, owns pointer
    //input, and delegates painting to it. Link-drag lifecycle (reference §3: StartLinkDrag/DisposeLinkDrag,
    //the ghost's MouseDownElement redirection, passthrough-bus creation) lives here too, alongside the rest
    //of the drag/mouse-routing state (MouseDownElement, CurrentDragOperation) rather than on GraphViewer.
    public sealed partial class GraphCanvasControl : Decorator {
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

        public event EventHandler? ViewScaleChanged;

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

        //Ports inDrawShapeMode (reference §1/§6): armed by BeginDrawShape (the background right-click menu's
        //"Add Shape" entry, since there's no toolbar button or keyboard shortcut per reference §6), cleared by
        //FinishDrawShape or an Escape key-down.
        private bool inDrawShapeMode;
        private AvaloniaPoint mouseDownStartScreenPoint;
        private DrawingPoint viewDragOriginGraphPoint;
        private DrawingPoint selectionZoneOriginGraphPoint;
        private bool linkBodyPressed;
        private AvaloniaPoint? hoverScreenPoint;

        static GraphCanvasControl() {
            PanProgressProperty.Changed.AddClassHandler<GraphCanvasControl>((c, e) => c.OnPanProgress(e.GetNewValue<double>()));
            PlacementFlashProgressProperty.Changed.AddClassHandler<GraphCanvasControl>((c, e) => c.OnPlacementFlashProgress(e.GetNewValue<double>()));
        }

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
            paletteFrame = new PopoverFrame { Name = "QuickAddPopover", Mode = PopoverFrameMode.Flush };
            paletteFrame.Closed += (_, _) => Focus();
            paletteHost.Surface.Children.Add(paletteFrame);
            radialFallbackPopup = new Popup {
                IsLightDismissEnabled = true,
                Placement = PlacementMode.AnchorAndGravity,
                PlacementAnchor = PopupAnchor.TopLeft,
                PlacementGravity = PopupGravity.BottomRight,
                PlacementTarget = this,
            };
            radialFallbackPopup.Closed += (_, _) => Focus();
            pausedCapsule = new PausedCapsule();
            pausedCapsule.Click += (_, _) => ResumeCalculations();
            Viewer.PausedChanged += (_, _) => {
                pausedCapsule.IsVisible = Viewer.IsPaused;
                RequestRedraw();
            };
            emptyState = BuildEmptyState();
            IsTabStop = false;
            Viewer.ElementsChanged += OnElementsChanged;
            Viewer.NodeReplaced += OnNodeReplaced;
            Child = new Panel { Children = { overlay, emptyState, pausedCapsule, paletteHost, radialFallbackPopup } };
            FloatingPanelHost = new FloatingPanelHost(overlay, Viewport, this);
            AddHandler(PointerPressedEvent, NoteDismissingPress, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            Viewer.Context.SetClipboardText = SetClipboardText;
            Viewer.Context.GetClipboardTextAsync = GetClipboardTextAsync;
            Viewer.Context.StartLinkDrag = StartLinkDrag;
            Viewer.Context.EditNode = EditNode;
            Viewer.Context.LabelValuesChanged = StartLabelFade;
            Viewer.ShowAnnotationPropertiesDialog = ShowAnnotationProperties;
            ClipToBounds = true;
            Focusable = true;
            KitSkia.TokenBridge.Changed += OnCanvasTokensChanged;
        }

        private void OnCanvasTokensChanged(object? sender, EventArgs e) => InvalidateVisual();

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

        internal void EditNode(BaseNodeElement node) {
            if (Viewer.Context.DCache is not DataCache)
                return;

            Viewer.SetSelection([node]);
        }

        internal GraphLoadResult LoadDocument(DataCache cache, string json, bool setEnablesFromJson = true) {
            ClosePanels();
            return Viewer.LoadDocument(cache, json, setEnablesFromJson);
        }

        internal GraphLoadResult LoadDocument(DataCache cache, GraphViewerSaveDocument document, bool setEnablesFromJson = true) {
            ClosePanels();
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
            ShowRadialMenu(request);
            DisposeLinkDrag();
            RequestRedraw();
        }

        internal RadialMenu? OpenRadial => openRadial;
        internal MenuPanel? OpenRadialFallback => radialFallbackPopup.IsOpen ? radialFallbackPopup.Child as MenuPanel : null;
        internal Popup RadialFallbackPopup => radialFallbackPopup;

        private const double RadialCancelDistance = 140;
        private readonly Popup radialFallbackPopup;
        private RadialMenu? openRadial;
        private RadialRing? closedRing;

        internal void ShowRadialMenu(NewNodeLinkDragRequest request) {
            ResetPointerDragState();
            ClosePalette();
            CloseRadial();
            if (closedRing is not null) {
                paletteHost.Surface.Children.Remove(closedRing);
                closedRing = null;
            }

            var radial = new RadialMenu(RadialSlicesFor(request));
            radial.Chosen += (_, choice) => {
                CloseRadial();
                RunRadialChoice(request, choice);
            };
            radial.Dismissed += (_, _) => CloseRadial();
            radial.MenuRequested += (_, _) => ShowRadialFallback(radial, request);

            RadialRing ring = radial.Ring;
            ring.Theme ??= KitTheme("Kit.RadialRing");
            paletteHost.Surface.Children.Add(ring);
            paletteHost.UpdateLayout();
            AvaloniaCanvas.SetLeft(ring, Math.Max(0, Math.Min(request.ScreenPoint.X - (ring.Width / 2), Bounds.Width - ring.Width)));
            AvaloniaCanvas.SetTop(ring, Math.Max(0, Math.Min(request.ScreenPoint.Y - (ring.Height / 2), Bounds.Height - ring.Height)));
            paletteHost.UpdateLayout();
            openRadial = radial;
            ring.IsOpen = true;
        }

        private void CloseRadial() {
            if (openRadial is null)
                return;
            RadialRing ring = openRadial.Ring;
            openRadial = null;
            ring.IsOpen = false;
            closedRing = ring;
            Focus();
        }

        private bool IsWithinRadialCancelDistance(AvaloniaPoint screenPoint) =>
            openRadial is not null && ((Vector)(screenPoint - openRadial.Ring.Bounds.Center)).Length <= RadialCancelDistance;

        private bool IsInsideRadial(object? source) =>
            source is Visual visual && openRadial?.Ring is RadialRing ring && (ReferenceEquals(visual, ring) || ring.IsVisualAncestorOf(visual));

        private void ShowRadialFallback(RadialMenu radial, NewNodeLinkDragRequest request) {
            var menu = new MenuPanel();
            foreach (MenuRow row in radial.FallbackRows(choice => {
                radialFallbackPopup.IsOpen = false;
                RunRadialChoice(request, choice);
            }))
                menu.Items.Add(row);
            menu.RequestClose += (_, _) => radialFallbackPopup.IsOpen = false;
            radialFallbackPopup.Child = menu;
            radialFallbackPopup.PlacementRect = new Rect(request.ScreenPoint, new Size(1, 1));
            radialFallbackPopup.IsOpen = true;
            menu.Focus();
        }

        private void RunRadialChoice(NewNodeLinkDragRequest request, RadialChoice choice) {
            switch (choice) {
                case RadialChoice.Add:
                    ShowQuickAdd(new AddRequest(request.EndpointLocation, AddScope.All, LinkContextFor(request)));
                    break;
                case RadialChoice.Passthrough:
                    CreatePassthroughFromLinkDrag(request);
                    break;
                case RadialChoice.Source:
                    PlaceLinkedRole(request, NodeType.Supplier);
                    break;
                case RadialChoice.Output:
                    PlaceLinkedRole(request, NodeType.Consumer);
                    break;
                case RadialChoice.Spoil:
                    PlaceSpoilPlant(request, NodeType.Spoil, NodeDirection.Up);
                    break;
                case RadialChoice.Plant:
                    PlaceSpoilPlant(request, NodeType.Plant, NodeDirection.Up);
                    break;
                case RadialChoice.Unspoil:
                    PlaceOrBrowseOrigins(request, NodeType.Spoil);
                    break;
                case RadialChoice.Unplant:
                    PlaceOrBrowseOrigins(request, NodeType.Plant);
                    break;
                default:
                    DisposeLinkDrag();
                    break;
            }
        }

        private List<RadialChoice> RadialSlicesFor(NewNodeLinkDragRequest request) {
            IItem? item = request.Item.Item;
            bool isDefaultQuality = Viewer.Context.DCache is not DataCache cache || request.Item.Quality == cache.DefaultQuality;
            var choices = new List<RadialChoice> { RadialChoice.Add };
            if (request.NodeType == NewNodeType.Supplier) {
                choices.Add(RadialChoice.Source);
                choices.Add(RadialChoice.Passthrough);
                if (item is not null && item.SpoilOrigins.Count > 0)
                    choices.Add(RadialChoice.Unspoil);
                if (item is not null && isDefaultQuality && item.PlantOrigins.Count > 0)
                    choices.Add(RadialChoice.Unplant);
            } else {
                choices.Add(RadialChoice.Output);
                choices.Add(RadialChoice.Passthrough);
                if (item?.SpoilResult is not null)
                    choices.Add(RadialChoice.Spoil);
                if (item?.PlantResult is not null)
                    choices.Add(RadialChoice.Plant);
            }
            choices.Add(RadialChoice.Cancel);
            return choices;
        }

        private void PlaceLinkedRole(NewNodeLinkDragRequest request, NodeType role) {
            if (request.Item.Item is not IItem item)
                return;
            PlaceNode(AddCatalog.Candidate(item), new AddRequest(request.EndpointLocation, AddScope.Items, LinkContextFor(request)), role);
        }

        private void PlaceSpoilPlant(NewNodeLinkDragRequest request, NodeType role, NodeDirection direction) {
            if (request.Item.Item is not IItem item)
                return;
            PlaceNode(AddCatalog.Candidate(item), new AddRequest(request.EndpointLocation, AddScope.Items, LinkContextFor(request)), role, spoilPlantDirection: direction);
        }

        private void PlaceOrBrowseOrigins(NewNodeLinkDragRequest request, NodeType role) {
            if (request.Item.Item is not IItem item)
                return;
            IReadOnlyCollection<IItem> origins = role == NodeType.Spoil ? item.SpoilOrigins : item.PlantOrigins;
            AddLinkContext link = LinkContextFor(request);
            if (origins.Count <= 1) {
                PlaceNode(AddCatalog.Candidate(item), new AddRequest(request.EndpointLocation, AddScope.Items, link), role, spoilPlantDirection: NodeDirection.Down);
                return;
            }
            BrowseRequested?.Invoke(this, new AddRequest(request.EndpointLocation, AddScope.Items, link, OriginsRole: role));
        }

        private AddLinkContext LinkContextFor(NewNodeLinkDragRequest request) =>
            new(request.Item, request.NodeType, TemperatureRangeFor(request), request.OriginElement, request.Direction);

        private FRange TemperatureRangeFor(NewNodeLinkDragRequest request) {
            if (request.Item.Item is not IFluid fluid || !fluid.IsTemperatureDependent)
                return new FRange(0, 0, true);
            LinkType direction = request.NodeType == NewNodeType.Consumer ? LinkType.Output : LinkType.Input;
            return LinkChecker.GetTemperatureRange(fluid, request.OriginElement.ViewModel, direction, true, Viewer.Session);
        }

        internal void CreatePassthroughFromLinkDrag(NewNodeLinkDragRequest request) {
            NodeId newNodeId = Viewer.Session.Editor.CreatePassthroughNode(request.Item, request.EndpointLocation);
            Viewer.Session.Editor.SetDirection(newNodeId, request.Direction);
            ClearOfOrigin(newNodeId, request.OriginElement);
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

        private void ClearOfOrigin(NodeId placedId, BaseNodeElement origin) {
            if (!Viewer.NodeElementDictionary.TryGetValue(placedId, out BaseNodeElement? placed) || placed == origin)
                return;
            KitSkia.CanvasTokens tokens = KitSkia.TokenBridge.Current;
            DrawingPoint at = placed.Location;
            DrawingPoint from = origin.Location;
            int away = at.Y < from.Y ? -1 : 1;
            float placedReach = (placed.Height / 2f) + tokens.NodeTabOverhang;
            float originReach = (origin.Height / 2f) + tokens.NodeTabOverhang;
            float gap = (away * (at.Y - from.Y)) - placedReach - originReach;
            float crossGap = Math.Abs(at.X - from.X) - (placed.Width / 2f) - (origin.Width / 2f) - (2 * tokens.NodeTabOverhang);
            if (gap >= tokens.Space5 || crossGap >= tokens.Space5)
                return;

            int y = at.Y + (away * (int)Math.Ceiling(tokens.Space5 - gap));
            if (Grid.ShowGrid && Grid.CurrentGridUnit > 0) {
                int snapped = Grid.AlignToGrid(y);
                if ((snapped - y) * away < 0)
                    snapped += away * Grid.CurrentGridUnit;
                y = snapped;
            }
            Viewer.Session.Editor.SetLocation(placedId, new DrawingPoint(at.X, y));
        }

        //Fire-and-forget clipboard write, matching upstream's WinForms Clipboard.SetText - see
        //docs/upstream-divergences.md.
        private void SetClipboardText(string text) {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                Async.Fire(clipboard.SetTextAsync(text), nameof(SetClipboardText));
        }

        internal Func<Task<string?>>? ClipboardReader { get; set; }

        //A sync clipboard read on the UI thread deadlocks X11.
        private async Task<string?> GetClipboardTextAsync() {
            try {
                if (ClipboardReader is { } reader)
                    return await reader();
                return TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Failed to read the clipboard");
                return null;
            }
        }

        private int interactionGeneration;

        private async Task ShowNodeContextMenuAsync(BaseNodeElement node, DrawingPoint graphPoint) {
            int generation = interactionGeneration;
            List<MenuEntry> entries = await node.MouseUpRightAsync(graphPoint);
            if (generation == interactionGeneration && Viewer.NodeElementDictionary.TryGetValue(node.ViewModel.Id, out BaseNodeElement? live) && live == node)
                ShowContextMenu(entries);
        }

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

        internal static readonly StyledProperty<double> PanProgressProperty =
            AvaloniaProperty.Register<GraphCanvasControl, double>("PanProgress");

        private DrawingPoint panOrigin;
        private DrawingPoint panTarget;
        private CancellationTokenSource? panCancellation;

        internal void PanIntoView(DrawingRectangle nodeBounds, double margin) {
            if (Viewport.PanIntoViewTarget(nodeBounds, margin) is not DrawingPoint target)
                return;

            panCancellation?.Cancel();
            panCancellation = null;
            if (KitMotion.ReduceMotion) {
                Viewport.PanIntoView(nodeBounds, margin);
                RequestRedraw();
                return;
            }

            panOrigin = Viewport.ViewOffset;
            panTarget = target;
            var cancellation = new CancellationTokenSource();
            panCancellation = cancellation;
            _ = RunPan(cancellation.Token);
        }

        private async Task RunPan(CancellationToken token) {
            var pan = new Animation {
                Duration = KitMotion.EntranceDuration,
                Easing = KitMotion.EntranceEasing,
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(PanProgressProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(PanProgressProperty, 1d) } },
                },
            };
            try {
                await pan.RunAsync(this, token);
            } catch (OperationCanceledException) {
                return;
            }

            if (!token.IsCancellationRequested)
                SetCurrentValue(PanProgressProperty, 1d);
        }

        private void OnPanProgress(double progress) {
            Viewport.ViewOffset = new DrawingPoint(
                panOrigin.X + (int)Math.Round((panTarget.X - panOrigin.X) * progress),
                panOrigin.Y + (int)Math.Round((panTarget.Y - panOrigin.Y) * progress));
            Viewport.UpdateGraphBounds();
            RequestRedraw();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e) {
            base.OnSizeChanged(e);
            Viewport.SetSize(e.NewSize.Width, e.NewSize.Height, Viewer.Graph.Bounds);
            FloatingPanelHost.Reposition();
            if (OpenPalette is not null && paletteScreenAnchor is null)
                paletteFrame.AnchorPoint = Viewport.GraphToScreen(paletteGraphAnchor);
            InvalidateVisual();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            if (labelFade is not null || alignTween is not null)
                QueueMotionFrame();
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
            KitSkia.TokenBridge.Changed -= OnCanvasTokensChanged;
            panCancellation?.Cancel();
            panCancellation = null;
            EndPlacementFlash();
            TrackHoverTarget(null);
            FinishAlign();
            EndLabelFade();
            Viewer.Dispose();
            foreach (Bitmap bitmap in addIconCache.Values)
                bitmap.Dispose();
            addIconCache.Clear();
        }

        //Ports ProductionGraphViewer_MouseWheel's own focus guard (reference §7, upstream lines 1065-1067
        //`if (ContainsFocus && !this.Focused) return;`): a hosted panel always holds real Avalonia focus
        //while it's open (FloatingPanelHost.Show focuses its content), so IsOpen alone stands in for that
        //check - the wheel must do nothing at all, not zoom the canvas out from under an open panel.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);
            if (IsPanelOpen)
                return;

            AvaloniaPoint position = e.GetPosition(this);
            Viewport.ZoomAt(position, e.Delta.Y > 0, Viewer.Graph.Bounds);
            ViewScaleChanged?.Invoke(this, EventArgs.Empty);
            FloatingPanelHost.Reposition();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            TrackHoverTarget(null);
            if (Viewer.SetHoveredLink(null))
                RequestRedraw();
            if (Viewer.Context.HoveredTab is null)
                return;
            Viewer.Context.HoveredTab = null;
            RequestRedraw();
        }

        //A Left click on a node claims MouseDownElement and forwards to BaseNodeElement.MouseDown (reference
        //§2); Focus() picks up keyboard input (arrow keys, Shift axis-lock) the same click starts.
        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            interactionGeneration++;
            FinishAlign();
            pressReachedCanvas = true;
            dismissingPress = false;
            if (IsInsidePalette(e.Source))
                return;
            if (IsInsideRadial(e.Source)) {
                e.Handled = true;
                return;
            }
            AvaloniaPoint screenPoint = e.GetPosition(this);
            if (openRadial is not null) {
                if (IsWithinRadialCancelDistance(screenPoint))
                    radialPress = true;
                else {
                    CloseRadial();
                    dismissingPress = true;
                }
                e.Handled = true;
                return;
            }

            //Ports ToolTipRenderer.ClearFloatingControls() being the first statement in
            //ProductionGraphViewer_MouseDown, unconditional on every click (reference §7): a click that
            //lands on the panel itself never reaches here at all (it's a real Avalonia child in front of
            //us), so only an outside click can close it - and, matching upstream, that same click still
            //falls through to the canvas handling below instead of being swallowed.
            if (FloatingPanelHost.IsOpen) {
                var pressedPoint = new DrawingPoint((int)screenPoint.X, (int)screenPoint.Y);
                if (FloatingPanelHost.Bounds.Contains(pressedPoint)) {
                    e.Handled = true;
                    return;
                }
                FloatingPanelHost.Close();
            }

            Focus();
            HideKeyboardFocus();
            mouseDownStartScreenPoint = screenPoint;
            DrawingPoint graphPoint = Viewport.ScreenToGraph(screenPoint);
            PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
            UpdateModifiers(e.KeyModifiers);
            ApplyAxisLockModifier(e.KeyModifiers);

            //Ghost-first priority (reference §1's clickedElement resolution): a link drag in progress claims
            //every click, ignoring whatever node/annotation is actually under the cursor.
            GraphElement? clickedElement = (GraphElement?)DraggedLink ?? HitTest(screenPoint);
            if (properties.IsLeftButtonPressed && clickedElement is BaseNodeElement pressedNode)
                FocusedNode = pressedNode;

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

            if (properties.IsLeftButtonPressed && e.ClickCount == 2 && clickedElement is null && !inDrawShapeMode) {
                ResetPointerDragState();
                ShowQuickAdd(new AddRequest(graphPoint, AddScope.All));
                e.Handled = true;
                return;
            }

            if (TryPressLink(clickedElement, e, properties, graphPoint))
                return;

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
                        if (clickedElement is BaseNodeElement nodeToSelect && !nodeToSelect.HasSubElementAt(graphPoint))
                            Viewer.SetSelection([nodeToSelect]);
                        else
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
            if (IsInsidePalette(e.Source) || dismissingPress)
                return;
            AvaloniaPoint screenPoint = e.GetPosition(this);
            if (openRadial is not null) {
                if (!IsWithinRadialCancelDistance(screenPoint))
                    CloseRadial();
                return;
            }

            //Mirrors OnPointerPressed's chrome-swallow guard (reference §7): a move over the panel itself
            //must not start a canvas lasso, drag a node underneath, or recompute the (irrelevant, covered)
            //hover tooltip - only the panel's own controls should see it.
            if (FloatingPanelHost.IsOpen) {
                var movedPoint = new DrawingPoint((int)screenPoint.X, (int)screenPoint.Y);
                if (FloatingPanelHost.Bounds.Contains(movedPoint))
                    return;
            }

            hoverScreenPoint = screenPoint;
            DrawingPoint graphPoint = Viewport.ScreenToGraph(screenPoint);
            BaseNodeElement? hoveredNode = GetNodeAtPoint(graphPoint);
            Viewer.Context.HoveredTab = hoveredNode?.SubElements.OfType<ItemTabElement>().FirstOrDefault(tab => tab.ContainsPoint(graphPoint));
            TrackHoverTarget(hoveredNode?.HoverTargetAt(graphPoint) ?? AnnotationLoader.GetAnnotationAtPoint(Annotations, graphPoint));
            UpdateLinkHover(graphPoint, hoveredNode);
            PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
            UpdateModifiers(e.KeyModifiers);
            ApplyAxisLockModifier(e.KeyModifiers);

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
                        else if (properties.IsLeftButtonPressed && !linkBodyPressed)
                            CurrentDragOperation = inDrawShapeMode ? DragOperation.DrawShape : DragOperation.Selection;
                    }
                    break;

                case DragOperation.Item:
                    //Leader/follower split (reference §2): the directly-dragged node runs its own grid-
                    //snap/axis-lock logic; every other selected node and annotation gets the resulting raw,
                    //unaligned delta instead (SetLocation/X+=/Y+=), preserving relative offset off-grid.
                    if (MouseDownElement is BaseNodeElement groupDragNode && Viewer.SelectedNodes.Contains(groupDragNode)) {
                        Viewer.CarryWaypoints([.. Viewer.SelectedNodes], () => {
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
                        });
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
                    } else if (MouseDownElement is WaypointElement draggedWaypoint) {
                        draggedWaypoint.Link.PreviewWaypoint(draggedWaypoint.Index, WaypointElement.DragTarget(graphPoint, Grid));
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
            if (dismissingPress || IsInsidePalette(e.Source)) {
                dismissingPress = false;
                return;
            }
            AvaloniaPoint releaseScreenPoint = e.GetPosition(this);
            if (radialPress) {
                radialPress = false;
                e.Handled = true;
                if (openRadial is null)
                    return;
                if (IsWithinRadialCancelDistance(releaseScreenPoint))
                    openRadial.CommitArmed();
                else
                    CloseRadial();
                return;
            }

            //Mirrors OnPointerPressed's chrome-swallow guard (reference §7): a press landing on the panel
            //already returns before claiming MouseDownElement/CurrentDragOperation, but without this the
            //matching release still reached the hit-test below and opened a node's context menu (or acted
            //on whatever's underneath) right through the panel's own chrome.
            if (FloatingPanelHost.IsOpen) {
                var releasedPoint = new DrawingPoint((int)releaseScreenPoint.X, (int)releaseScreenPoint.Y);
                if (FloatingPanelHost.Bounds.Contains(releasedPoint)) {
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
                            Async.Fire(ShowNodeContextMenuAsync(rightClickedNode, graphPoint), nameof(ShowNodeContextMenuAsync));
                        else if (rightClickTarget is AnnotationElement rightClickedAnnotation)
                            ShowContextMenu(rightClickedAnnotation.BuildRightClickMenu());
                        else if (LinkOf(rightClickTarget) is LinkElement rightClickedLink) {
                            if (!Viewer.SelectedLinks.Contains(rightClickedLink))
                                Viewer.SelectLink(rightClickedLink, additive: false);
                            ShowContextMenu(BuildLinkMenu(rightClickedLink, graphPoint));
                        }
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
                    } else if (CurrentDragOperation == DragOperation.Item) {
                        if (MouseDownElement is WaypointElement releasedWaypoint)
                            Viewer.CommitWaypointPreview(releasedWaypoint.Link);
                        else
                            Viewer.Session.NotifyMutated();
                    } else if (CurrentDragOperation == DragOperation.None && MouseDownElement is BaseNodeElement clickedNode) {
                        SelectionModifier modifier = ModifierFor(e.KeyModifiers);
                        if (modifier == SelectionModifier.Remove) {
                            Viewer.SelectedNodes.Remove(clickedNode);
                            clickedNode.Highlighted = false;
                            Viewer.RaiseSelectionChanged();
                        } else if (modifier == SelectionModifier.Add) {
                            if (clickedNode.Highlighted)
                                Viewer.SelectedNodes.Remove(clickedNode);
                            else
                                Viewer.SelectedNodes.Add(clickedNode);
                            clickedNode.Highlighted = !clickedNode.Highlighted;
                            Viewer.RaiseSelectionChanged();
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

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            dismissingPress = false;
            ResetPointerDragState();
        }

        private void ResetPointerDragState() {
            CurrentDragOperation = DragOperation.None;
            MouseDownElement = null;
            ViewBeingDragged = false;
            linkBodyPressed = false;
            Viewer.SelectionZone = null;
            Viewer.DiscardWaypointPreviews();
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

        private List<MenuEntry> BuildBackgroundMenu(DrawingPoint graphPoint) => [
            MenuEntry.Item("Add Item", () => AddItemAsync(graphPoint)),
            MenuEntry.Item("Add Recipe", () => AddRecipeAsync(graphPoint)),
            MenuEntry.Divider,
            MenuEntry.Item("Add Text", () => Async.Fire(AddTextAnnotationAsync(graphPoint), nameof(AddTextAnnotationAsync))),
            MenuEntry.Item("Add Shape", () => BeginDrawShape(graphPoint)),
        ];

        internal void AddItemAsync(DrawingPoint graphPoint, AvaloniaPoint? screenAnchor = null) => ShowQuickAdd(new AddRequest(graphPoint, AddScope.Items), screenAnchor);
        internal void AddRecipeAsync(DrawingPoint graphPoint, AvaloniaPoint? screenAnchor = null) => ShowQuickAdd(new AddRequest(graphPoint, AddScope.Recipes), screenAnchor);

        internal event EventHandler<AddRequest>? BrowseRequested;
        internal AddRequest? ActiveAddRequest { get; private set; }
        internal QuickAddPalette? OpenPalette => paletteFrame.IsOpen ? paletteFrame.Content as QuickAddPalette : null;
        internal PopoverFrame PaletteFrame => paletteFrame;
        internal bool IsPanelOpen => FloatingPanelHost.IsOpen || OpenPalette is not null || openRadial is not null;
        private readonly Dictionary<SKBitmap, Bitmap> addIconCache = [];
        private readonly PopoverHost paletteHost = new();
        private readonly PopoverFrame paletteFrame;
        private DrawingPoint paletteGraphAnchor;
        private AvaloniaPoint? paletteScreenAnchor;
        private bool dismissingPress;
        private bool pressReachedCanvas;
        private bool radialPress;

        internal void ClosePanels() {
            FloatingPanelHost.Close();
            ClosePalette();
            CloseRadial();
            radialFallbackPopup.IsOpen = false;
        }

        private void ClosePalette() {
            if (paletteFrame.IsOpen)
                paletteFrame.IsOpen = false;
        }

        private bool IsInsidePalette(object? source) =>
            source is Visual visual && paletteFrame.IsOpen && (ReferenceEquals(visual, paletteFrame) || paletteFrame.IsVisualAncestorOf(visual));

        private void NoteDismissingPress(object? sender, PointerPressedEventArgs e) {
            bool reachedCanvas = pressReachedCanvas;
            pressReachedCanvas = false;
            if (!reachedCanvas && e.Handled && e.Source is Visual visual && ReferenceEquals(visual, paletteHost))
                dismissingPress = true;
        }

        private static Avalonia.Styling.ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as Avalonia.Styling.ControlTheme ?? throw new InvalidOperationException($"GraphCanvasControl: kit theme '{key}' not found.");
        }

        internal void ShowQuickAdd(AddRequest request, AvaloniaPoint? screenAnchor = null) {
            if (Viewer.Context.DCache is not DataCache cache)
                return;
            ResetPointerDragState();
            FloatingPanelHost.Close();

            AddRequest current = request;
            ActiveAddRequest = current;
            var vm = new QuickAddViewModel(new AddCatalog(cache, ChooserSettings), request, Viewer.Graph.DefaultAssemblerQuality, bmp => IconBaker.GetOrBake(addIconCache, bmp, 22));
            var palette = new QuickAddPalette { DataContext = vm };
            palette.PickCommitted += (_, c) => {
                current = PlaceCandidate(c, current with { Link = vm.Link });
                ActiveAddRequest = current;
            };
            palette.BrowseRequested += (_, q) => {
                var browseRequest = current with { SeedText = q, Link = vm.Link };
                ClosePalette();
                BrowseRequested?.Invoke(this, browseRequest);
            };
            palette.CloseRequested += (_, _) => ClosePalette();

            paletteFrame.Theme ??= KitTheme("Kit.PopoverFrame");
            paletteGraphAnchor = request.Location;
            paletteScreenAnchor = screenAnchor;
            paletteFrame.Content = palette;
            paletteFrame.AnchorPoint = screenAnchor ?? Viewport.GraphToScreen(request.Location);
            paletteFrame.IsOpen = true;
            paletteHost.UpdateLayout();
            palette.FocusInitialControl();
        }

        internal AddRequest PlaceCandidate(AddCandidate candidate, AddRequest request, NodeType? roleOverride = null, IQuality? quality = null,
            NodeDirection? spoilPlantDirection = null, IItem? explicitOrigin = null) {
            (AddRequest next, NodeId placed) = PlaceNodeCore(candidate, request, roleOverride, quality, spoilPlantDirection, explicitOrigin);
            if (Viewer.NodeElementDictionary.TryGetValue(placed, out BaseNodeElement? element)) {
                StartPlacementFlash(element);
                Announce(element.NodeName + " added");
            }
            return next;
        }

        private AddRequest PlaceNode(AddCandidate candidate, AddRequest request, NodeType? roleOverride = null, IQuality? quality = null,
            NodeDirection? spoilPlantDirection = null, IItem? explicitOrigin = null) =>
            PlaceNodeCore(candidate, request, roleOverride, quality, spoilPlantDirection, explicitOrigin).Next;

        private (AddRequest Next, NodeId Placed) PlaceNodeCore(AddCandidate candidate, AddRequest request, NodeType? roleOverride, IQuality? quality,
            NodeDirection? spoilPlantDirection, IItem? explicitOrigin) {
            if (Viewer.Context.DCache is not DataCache cache)
                return (request, NodeId.Invalid);

            IQuality q = quality ?? Viewer.Graph.DefaultAssemblerQuality ?? cache.DefaultQuality!;
            NodeDirection direction = spoilPlantDirection ?? request.Link?.Direction ?? Viewer.Graph.DefaultNodeDirection;
            NewNodeType outer = request.Link?.NodeType ?? NewNodeType.Disconnected;

            NodeId placed;
            if (candidate.Kind == AddKind.Recipe) {
                var baseItem = request.Link?.Item ?? default;
                var ctx = new NewNodeContext(baseItem, request.Location, outer, request.Link?.OriginElement, direction, request.Link is not null);
                placed = ProcessNodeRequest(ctx, NodeType.Recipe, new RecipeQualityPair(candidate.Recipe!, RecipeQualityLock.PlacementQuality(candidate.Recipe!, q, cache)), direction);
            } else {
                var item = new ItemQualityPair(candidate.Item!, q);
                var ctx = new NewNodeContext(item, request.Location, outer, request.Link?.OriginElement, direction, request.Link is not null);
                NodeType role = roleOverride ?? (outer == NewNodeType.Consumer ? NodeType.Consumer : NodeType.Supplier);
                placed = ProcessNodeRequest(ctx, role, default, direction, explicitOrigin);
            }

            AddRequest next = request with {
                Location = request.Link is null
                    ? new DrawingPoint(request.Location.X, request.Location.Y + 120)
                    : new DrawingPoint(request.Location.X + 120, request.Location.Y)
            };
            return (next, placed);
        }

        internal Func<TimeSpan> MotionClock { get; set; } = () => System.Diagnostics.Stopwatch.GetElapsedTime(0);

        private ProgressTween? labelFade;
        private ProgressTween? alignTween;
        private bool motionFrameQueued;

        internal bool MotionFrameQueued => motionFrameQueued;
        private IReadOnlyList<GraphViewer.AlignMove>? alignMoves;

        private void StartLabelFade() {
            if (KitMotion.ReduceMotion)
                return;
            if (labelFade is not null && Viewer.Context.LabelFadeProgress <= 0)
                return;
            labelFade = new ProgressTween(MotionClock, KitMotion.TabLabelCrossFadeDuration, KitMotion.CrossFadeEasing);
            Viewer.Context.LabelFadeGeneration++;
            Viewer.Context.LabelFadeProgress = 0;
            QueueMotionFrame();
        }

        private void EndLabelFade() {
            labelFade = null;
            Viewer.Context.LabelFadeProgress = 1;
            RequestRedraw();
        }

        public void AlignSelection() {
            FinishAlign();
            IReadOnlyList<GraphViewer.AlignMove> moves = Viewer.PlanAlignSelection();
            if (moves.Count == 0)
                return;
            if (KitMotion.ReduceMotion) {
                Viewer.ApplyAlign(moves, 1, notify: true);
                RequestRedraw();
                return;
            }
            alignMoves = moves;
            alignTween = new ProgressTween(MotionClock, KitMotion.AlignTweenDuration, KitMotion.EntranceEasing);
            QueueMotionFrame();
        }

        private void QueueMotionFrame() {
            if (motionFrameQueued || TopLevel.GetTopLevel(this) is not { } top)
                return;
            motionFrameQueued = true;
            top.RequestAnimationFrame(_ => TickMotion());
        }

        internal void TickMotion() {
            motionFrameQueued = false;
            if (labelFade is { } fade) {
                Viewer.Context.LabelFadeProgress = fade.Progress;
                RequestRedraw();
                if (fade.Done)
                    EndLabelFade();
            }
            if (alignTween is { } tween && alignMoves is { } moves) {
                if (tween.Done)
                    FinishAlign();
                else {
                    Viewer.ApplyAlign(moves, tween.Progress, notify: false);
                    RequestRedraw();
                }
            }
            if (labelFade is not null || alignTween is not null)
                QueueMotionFrame();
        }

        private void FinishAlign() {
            alignTween = null;
            if (alignMoves is not { } moves)
                return;
            alignMoves = null;
            Viewer.ApplyAlign(moves, 1, notify: true);
            RequestRedraw();
        }

        internal static readonly StyledProperty<double> PlacementFlashProgressProperty =
            AvaloniaProperty.Register<GraphCanvasControl, double>("PlacementFlashProgress");

        internal BaseNodeElement? PlacementFlashNode { get; private set; }
        private CancellationTokenSource? placementFlashCancellation;

        private static TimeSpan PlacementFlashDuration => KitMotion.HoverDuration + KitMotion.PlacementFlashHold + KitMotion.CrossFadeDuration;

        internal static double PlacementFlashOpacityAt(TimeSpan elapsed, bool reduceMotion) {
            TimeSpan rise = KitMotion.HoverDuration;
            TimeSpan fallStart = rise + KitMotion.PlacementFlashHold;
            if (elapsed <= TimeSpan.Zero)
                return 0;
            if (elapsed < rise) {
                double t = elapsed / rise;
                return reduceMotion ? KitMotion.CrossFadeEasing.Ease(t) : KitMotion.EntranceEasing.Ease(t);
            }
            if (elapsed <= fallStart)
                return 1;
            double f = Math.Min(1, (elapsed - fallStart) / KitMotion.CrossFadeDuration);
            return 1 - (reduceMotion ? KitMotion.CrossFadeEasing.Ease(f) : KitMotion.LeavingEasing.Ease(f));
        }

        private void StartPlacementFlash(BaseNodeElement node) {
            EndPlacementFlash();
            PlacementFlashNode = node;
            node.IsPlacementFlashing = true;
            node.PlacementFlashOpacity = 0;
            var cancellation = new CancellationTokenSource();
            placementFlashCancellation = cancellation;
            _ = RunPlacementFlash(cancellation.Token);
        }

        private async Task RunPlacementFlash(CancellationToken token) {
            var clock = new Animation {
                Duration = PlacementFlashDuration,
                Easing = new Avalonia.Animation.Easings.LinearEasing(),
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(PlacementFlashProgressProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(PlacementFlashProgressProperty, 1d) } },
                },
            };
            try {
                await clock.RunAsync(this, token);
            } catch (OperationCanceledException) {
                return;
            }
            if (!token.IsCancellationRequested)
                EndPlacementFlash();
        }

        private void OnPlacementFlashProgress(double progress) {
            if (PlacementFlashNode is not BaseNodeElement node)
                return;
            if (!Viewer.NodeElements.Contains(node)) {
                EndPlacementFlash();
                return;
            }
            node.PlacementFlashOpacity = PlacementFlashOpacityAt(PlacementFlashDuration * progress, KitMotion.ReduceMotion);
            RequestRedraw();
        }

        private void EndPlacementFlash() {
            placementFlashCancellation?.Cancel();
            placementFlashCancellation = null;
            if (PlacementFlashNode is not BaseNodeElement node)
                return;
            node.IsPlacementFlashing = false;
            node.PlacementFlashOpacity = 0;
            PlacementFlashNode = null;
            RequestRedraw();
        }

        private Services.AppSettings? fallbackChooserSettings;
        private Services.AppSettings ChooserSettings => AnnotationSettings ?? (fallbackChooserSettings ??= new Services.AppSettings());

        private readonly record struct NewNodeContext(ItemQualityPair BaseItem, DrawingPoint NewLocation, NewNodeType OuterNodeType, BaseNodeElement? OriginElement, NodeDirection NewNodeDirection, bool OffsetLocationToItemTabLevel, bool LinkToOrigin = true);

        private NodeId ProcessNodeRequest(NewNodeContext ctx, NodeType nodeType, RecipeQualityPair recipe, NodeDirection direction, IItem? explicitOrigin = null) {
            if (Viewer.Context.DCache is not DataCache cache)
                return NodeId.Invalid;

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
                case NodeType.Spoil when direction == NodeDirection.Down && itemNN is not null && qualityNN is not null && ResolveOrigin(explicitOrigin, itemNN.SpoilOrigins) is IItem spoilOrigin:
                    IQuality? originQuality = SpoilQuality.InputFor(spoilOrigin, qualityNN);
                    newNodeId = Viewer.Session.Editor.CreateSpoilNode(new ItemQualityPair(spoilOrigin, originQuality ?? qualityNN), itemNN, ctx.NewLocation);
                    if (originQuality is null)
                        ctx = ctx with { LinkToOrigin = false };
                    break;
                case NodeType.Plant when direction == NodeDirection.Up && itemNN?.PlantResult is IPlantProcess plantUp && qualityNN is not null:
                    newNodeId = Viewer.Session.Editor.CreatePlantNode(plantUp, qualityNN, ctx.NewLocation);
                    break;
                case NodeType.Plant when direction == NodeDirection.Down && itemNN is not null && ResolveOrigin(explicitOrigin, itemNN.PlantOrigins) is IItem plantOrigin:
                    if (plantOrigin.PlantResult is IPlantProcess plantDown && cache.DefaultQuality is IQuality defaultPlantQuality)
                        newNodeId = Viewer.Session.Editor.CreatePlantNode(plantDown, defaultPlantQuality, ctx.NewLocation);
                    break;
                case NodeType.Recipe when recipe:
                    newNodeId = Viewer.Session.Editor.CreateRecipeNode(recipe, ctx.NewLocation);
                    ApplyAssemblerFuelAutoSelection(ctx, newNodeId, recipe.Recipe!, itemNN);
                    break;
            }

            ProcessCreatedNode(ctx, newNodeId);
            return newNodeId;
        }

        private static IItem? ResolveOrigin(IItem? explicitOrigin, IReadOnlyCollection<IItem> origins) =>
            explicitOrigin ?? (origins.Count == 1 ? origins.First() : null);

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
        //node creation itself), and the link runs through baseItem in the direction the drag started from
        //when the new node carries that item.
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

            //CreateLink throws on a node id the registry no longer holds, and the origin can be deleted while its request is pending.
            bool originStillExists = ctx.OriginElement is not null && Viewer.NodeElementDictionary.ContainsKey(ctx.OriginElement.ViewModel.Id);

            if (originStillExists)
                Viewer.Session.Editor.SetDirection(newNodeId, ctx.NewNodeDirection);

            if (ctx.OffsetLocationToItemTabLevel && Viewer.NodeElementDictionary.TryGetValue(newNodeId, out BaseNodeElement? tabAlignElement)) {
                int yoffset = ctx.OuterNodeType == NewNodeType.Consumer ? -tabAlignElement.Height / 2
                    : ctx.OuterNodeType == NewNodeType.Supplier ? tabAlignElement.Height / 2
                    : 0;
                yoffset *= ctx.NewNodeDirection == NodeDirection.Up ? 1 : -1;
                if (yoffset != 0)
                    Viewer.Session.Editor.SetLocation(newNodeId, new DrawingPoint(ctx.NewLocation.X, ctx.NewLocation.Y + yoffset));
                if (originStillExists)
                    ClearOfOrigin(newNodeId, ctx.OriginElement!);
            }

            if (ctx.OuterNodeType == NewNodeType.Consumer && originStillExists && ctx.LinkToOrigin && NodeCarries(newNodeId, LinkType.Input, ctx.BaseItem))
                Viewer.Session.Editor.CreateLink(ctx.OriginElement!.ViewModel.Id, newNodeId, ctx.BaseItem);
            else if (ctx.OuterNodeType == NewNodeType.Supplier && originStillExists && ctx.LinkToOrigin && NodeCarries(newNodeId, LinkType.Output, ctx.BaseItem))
                Viewer.Session.Editor.CreateLink(newNodeId, ctx.OriginElement!.ViewModel.Id, ctx.BaseItem);

            Viewer.Graph.UpdateNodeValues();
            Viewer.Graph.UpdateNodeStates(false);
            if (Viewer.NodeElementDictionary.TryGetValue(newNodeId, out BaseNodeElement? element))
                Viewer.SetSelection([element]);
            RequestRedraw();
        }

        private bool NodeCarries(NodeId nodeId, LinkType side, ItemQualityPair item) =>
            Viewer.Session.View.TryGetNode(nodeId, out INodeViewModel? node) && node is not null && (side == LinkType.Input ? node.Inputs : node.Outputs).Contains(item);

        private void OnNodeReplaced(object? sender, NodeViewModelReplacedEventArgs e) {
            if (!Viewer.Session.View.TryGetNode(e.New, out INodeViewModel? node) || node is not IRecipeNodeViewModel copy)
                return;
            string removed = e.RemovedLinkCount switch { 0 => "", 1 => ", 1 link removed", _ => $", {e.RemovedLinkCount} links removed" };
            Announce($"{copy.BaseRecipe.Recipe?.FriendlyName} switched to {copy.BaseRecipe.Quality?.FriendlyName} quality{removed}");
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
            var sheet = new TextPropertiesSheet(element) { RequestRedraw = RequestRedraw, Settings = AnnotationSettings };
            return await SheetHost.For(owner).ShowAsync(sheet).ConfigureAwait(true);
        }

        private async Task<bool> ShowRealShapePropertiesDialogAsync(ShapeAnnotationElement element) {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var sheet = new ShapePropertiesSheet(element) { RequestRedraw = RequestRedraw, Settings = AnnotationSettings };
            return await SheetHost.For(owner).ShowAsync(sheet).ConfigureAwait(true);
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
            bool confirmed = await ShowTextPropertiesDialogAsync(element).ConfigureAwait(true);
            if (confirmed)
                Viewer.Session.NotifyMutated();
            RequestRedraw();
        }

        private async Task ShowExistingShapePropertiesAsync(ShapeAnnotationElement element) {
            bool confirmed = await ShowShapePropertiesDialogAsync(element).ConfigureAwait(true);
            if (confirmed)
                Viewer.Session.NotifyMutated();
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
            if (IsPanelOpen) {
                if (e.Key == Key.Escape) {
                    ClosePanels();
                    e.Handled = true;
                    return;
                }
                //A KeyDown marked Handled never reaches the native TextInput that follows it.
                if (FocusIsInsideFloatingPanel(e))
                    return;
                e.Handled = true;
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
            if (!e.Handled && HandleNavigationKey(e))
                e.Handled = true;
            if (CurrentDragOperation == DragOperation.None && HandleClipboardKey(e.Key, e.KeyModifiers))
                e.Handled = true;
            if (!e.Handled && HandleMovementKey(e.Key, e.KeyModifiers))
                e.Handled = true;
            ApplyAxisLockModifier(e.KeyModifiers);
            InvalidateVisual();
        }

        //Check e.Source, not FocusManager: a synthetic dispatch leaves the ambient focus elsewhere.
        private bool FocusIsInsideFloatingPanel(KeyEventArgs e) {
            if (e.Source is TextBox || IsInsidePalette(e.Source))
                return true;
            return e.Source is Visual sourceVisual && FloatingPanelHost.Content is Visual panelRoot &&
                (ReferenceEquals(sourceVisual, panelRoot) || panelRoot.IsVisualAncestorOf(sourceVisual));
        }

        //Ports ProductionGraphViewer_KeyDown's Ctrl+C/X/V block (reference §5/§7): Cmd replaces Ctrl on macOS
        //per the Cmd-mapping note (PlatformModifiers.Primary; Linux keeps Ctrl). Paste's origin is the
        //cursor's last-known graph position (hoverScreenPoint,
        //upstream's PointToClient(Cursor.Position)), falling back to the viewport center when the pointer
        //has never crossed the control.
        private bool HandleClipboardKey(Key key, KeyModifiers modifiers) {
            if (!modifiers.HasFlag(PlatformModifiers.Primary))
                return false;

            if (key == Key.C) {
                CopySelection();
                return true;
            }
            if (key == Key.X) {
                CutSelection();
                return true;
            }
            if (key == Key.V) {
                PasteFromClipboard();
                return true;
            }
            return false;
        }

        public void CopySelection() => Viewer.Context.SetClipboardText?.Invoke(NodeClipboard.Copy(Viewer));

        public void CutSelection() => Viewer.Context.SetClipboardText?.Invoke(NodeClipboard.Cut(Viewer));

        public void PasteFromClipboard() {
            interactionGeneration++;
            AvaloniaPoint cursorScreenPoint = hoverScreenPoint ?? new AvaloniaPoint(Viewport.Width / 2, Viewport.Height / 2);
            Async.Fire(PasteFromClipboardAsync(cursorScreenPoint), nameof(PasteFromClipboardAsync));
        }

        internal async Task PasteFromClipboardAsync(AvaloniaPoint cursorScreenPoint) {
            if (Viewer.Context.GetClipboardTextAsync is { } read && await read() is string clipboardText && Viewer.Context.DCache is DataCache cache) {
                NodeClipboard.Paste(Viewer, cache, clipboardText, Viewport.ScreenToGraph(cursorScreenPoint));
                RequestRedraw();
            }
        }

        public void DeleteSelection() {
            if (Viewer.RemoveSelectedWaypoint())
                return;
            bool hadLinks = Viewer.SelectedLinks.Count > 0;
            if (hadLinks)
                Viewer.DeleteSelectedLinks();
            if (Viewer.SelectedAnnotations.Count > 0)
                Viewer.TryDeleteSelection();
            else if (Viewer.SelectedNodes.Count > 0 || !hadLinks)
                Viewer.TryDeleteSelectedNodes();
            ValidateFocus();
        }

        //Ports ProductionGraphViewer_KeyUp's Delete case (reference §7): annotations-if-any-selected, else
        //nodes - TryDeleteSelection/TryDeleteSelectedNodes both carry their own >10 confirm gate.
        protected override void OnKeyUp(KeyEventArgs e) {
            base.OnKeyUp(e);
            if (IsPanelOpen)
                return;

            UpdateModifiers(e.KeyModifiers);
            RefreshLassoPreviewOnModifierChange(e.KeyModifiers);
            if (CurrentDragOperation == DragOperation.None && e.Key == Key.Delete) {
                DeleteSelection();
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
            if (CurrentDragOperation == DragOperation.Item && hoverScreenPoint is AvaloniaPoint hover) {
                if (MouseDownElement is BaseNodeElement node)
                    node.Dragged(Viewport.ScreenToGraph(hover), Grid);
                else if (MouseDownElement is WaypointElement waypoint)
                    waypoint.Link.PreviewWaypoint(waypoint.Index, WaypointElement.DragTarget(Viewport.ScreenToGraph(hover), Grid));
            }
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

            bool pan = !modifiers.HasFlag(PlatformModifiers.Primary);
            if (modifiers.HasFlag(KeyModifiers.Alt) && key is Key.Left or Key.Right or Key.Up or Key.Down)
                return false;
            switch (key) {
                case Key.Left: MoveSelection(-moveUnit, 0); return true;
                case Key.Right: MoveSelection(moveUnit, 0); return true;
                case Key.Up: MoveSelection(0, -moveUnit); return true;
                case Key.Down: MoveSelection(0, moveUnit); return true;
                case Key.W when pan: Pan(0, (int)panUnit); return true;
                case Key.A when pan: Pan((int)panUnit, 0); return true;
                case Key.S when pan: Pan(0, -(int)panUnit); return true;
                case Key.D when pan: Pan(-(int)panUnit, 0); return true;
                default: return false;
            }
        }

        //Ports the selectedNodes/selectedAnnotations halves of ProcessCmdKey's arrow branch - both move by
        //the same raw dx/dy, annotations are never grid-snapped anywhere in upstream.
        private void MoveSelection(int dx, int dy) {
            bool moved = false;
            Viewer.CarryWaypoints([.. Viewer.SelectedNodes], () => {
                foreach (BaseNodeElement node in Viewer.SelectedNodes) {
                    node.SetLocation(new DrawingPoint(node.X + dx, node.Y + dy));
                    moved = true;
                }
            });
            foreach (AnnotationElement annotation in Viewer.SelectedAnnotations) {
                annotation.X += dx;
                annotation.Y += dy;
                moved = true;
            }
            if (moved)
                Viewer.Session.NotifyMutated();
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
            foreach (TooltipInfo tooltip in HoverTooltips())
                FloatingTooltipRenderer.Draw(canvas, tooltip);

            if (Viewer.IsPaused)
                Viewer.PaintPausedFrame(canvas);
        }

        private readonly PausedCapsule pausedCapsule;

        public event EventHandler? CalculationsResumed;

        public void ResumeCalculations() {
            if (!Viewer.IsPaused)
                return;
            Viewer.IsPaused = false;
            Viewer.Graph.UpdateNodeValues();
            CalculationsResumed?.Invoke(this, EventArgs.Empty);
        }

        internal List<TooltipInfo> HoverTooltips() {
            if (!hoverTooltipReady || !Viewer.TooltipsEnabled || ViewBeingDragged || hoverScreenPoint is not AvaloniaPoint hover || HitTest(hover) is not GraphElement element)
                return [];
            return element.GetToolTips(Viewport.ScreenToGraph(hover));
        }

        internal static readonly StyledProperty<double> HoverTooltipDelayProgressProperty =
            AvaloniaProperty.Register<GraphCanvasControl, double>("HoverTooltipDelayProgress");

        private object? hoverTooltipTarget;
        private bool hoverTooltipReady;
        private CancellationTokenSource? hoverTooltipCancellation;

        private void TrackHoverTarget(object? target) {
            if (ReferenceEquals(target, hoverTooltipTarget))
                return;
            hoverTooltipTarget = target;
            hoverTooltipCancellation?.Cancel();
            hoverTooltipCancellation = null;
            if (hoverTooltipReady) {
                hoverTooltipReady = false;
                RequestRedraw();
            }
            if (target is null)
                return;
            var cancellation = new CancellationTokenSource();
            hoverTooltipCancellation = cancellation;
            _ = RunHoverTooltipDelay(cancellation.Token);
        }

        private async Task RunHoverTooltipDelay(CancellationToken token) {
            var clock = new Animation {
                Duration = KitMotion.TooltipDelay,
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(HoverTooltipDelayProgressProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(HoverTooltipDelayProgressProperty, 1d) } },
                },
            };
            try {
                await clock.RunAsync(this, token);
            } catch (OperationCanceledException) {
                return;
            }
            if (token.IsCancellationRequested)
                return;
            hoverTooltipReady = true;
            RequestRedraw();
        }

        //Ports GetNodeAtPoint's node-first precedence plus GetAnnotationAtPoint (reference §6): nodes always
        //win over annotations on overlap, mirroring ProductionGraphViewer_MouseDown/Up's probe order.
        public GraphElement? HitTest(AvaloniaPoint screenPoint) {
            DrawingPoint graphPoint = Viewport.ScreenToGraph(screenPoint);
            if (GetNodeAtPoint(graphPoint) is BaseNodeElement node)
                return node;
            if (Viewer.LinkTargetAt(graphPoint) is ({ } link, var waypoint))
                return (GraphElement?)waypoint ?? link;
            return AnnotationLoader.GetAnnotationAtPoint(Annotations, graphPoint);
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
