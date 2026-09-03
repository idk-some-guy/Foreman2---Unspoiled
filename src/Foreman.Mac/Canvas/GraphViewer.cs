using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas.Elements;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    public sealed record GraphLoadResult(bool Success, string? ErrorMessage) {
        public static readonly GraphLoadResult Ok = new(true, null);
        public static GraphLoadResult Failure(string message) => new(false, message);
    }

    //Ports the three-way split ProductionGraphViewer_MouseUp/UpdateSelection read off Control.ModifierKeys
    //(reference §1): Replace = neither modifier, Add = Ctrl (Cmd on this port), Remove = Alt.
    public enum SelectionModifier { Replace, Add, Remove }

    //Ports ProductionGraphViewer's element-lifecycle wiring (Session_NodeViewModelAdded/Removed,
    //Session_LinkViewModelAdded/Removed, Session_GraphCleared - reference §2) and its paint orchestration
    //(GetPaintingOrder, Paint, LOD/IconsOnly/NodeCountForSimpleView style selection, DynamicLinkWidth
    //pre-pass - reference §3, §8.12). Mouse-drag/selection/right-click/DraggedLinkElement concerns are P4
    //and stay out; this only drives the read-only element tree from session events plus the read-only half
    //of the paint pipeline. GraphCanvasControl owns the Avalonia control surface, pointer input, and the
    //hover-tooltip overlay (already built in Task 6); this class owns everything upstream's viewer keeps in
    //graph/session/element-collection state.
    public sealed partial class GraphViewer : IDisposable {
        private const float MinLinkWidth = 3f;
        private const float MaxLinkWidth = 35f;
        //stands in for upstream's Control.DeviceDpi (reference: AnnotationLoader's header comment); internal
        //so GraphViewerSaveAssembler (same assembly, Canvas/ folder) can read it back out on save.
        internal const int AnnotationDeviceDpi = 96;

        public Viewport Viewport { get; }
        public GridManager Grid { get; }
        public ProductionGraph Graph { get; }
        public ProductionGraphSession Session { get; }
        public NodeElementContext Context { get; }
        public PointingArrowRenderer ArrowRenderer { get; }

        public List<BaseNodeElement> NodeElements { get; } = [];
        public Dictionary<NodeId, BaseNodeElement> NodeElementDictionary { get; } = [];
        public List<LinkElement> LinkElements { get; } = [];
        public Dictionary<LinkId, LinkElement> LinkElementDictionary { get; } = [];
        public List<AnnotationElement> Annotations { get; } = [];

        public bool IconsOnly { get; set; }
        public int NodeCountForSimpleView { get; set; } = 200;
        public bool TooltipsEnabled { get; set; } = true;

        //Ports selectedNodes/currentSelectionNodes (reference §1 "Selection model") - main list vs. the
        //transient lasso preview, cleared every commit. Exposed mutable, matching NodeElements/Annotations'
        //loose-encapsulation style elsewhere in this class, since GraphCanvasControl's MouseUp lasso commit
        //mutates them directly (ExceptWith/UnionWith/Clear) without touching Highlighted itself - the last
        //UpdateSelection preview during the drag already left every node's flag matching the commit outcome.
        public HashSet<BaseNodeElement> SelectedNodes { get; } = [];
        public HashSet<BaseNodeElement> CurrentSelectionNodes { get; } = [];

        //Ports selectedAnnotations (reference §1/§2): annotation selection itself is Task 6's job, but node
        //dragging's follower loop already needs somewhere to read a mixed selection's annotations from, so
        //this exists now, empty until Task 6 starts populating it.
        public HashSet<AnnotationElement> SelectedAnnotations { get; } = [];

        //Ports SelectionZone (reference §1): null whenever no lasso is being dragged, so Paint's draw guard
        //collapses to a single null check instead of also tracking DragOperation itself.
        public Rectangle? SelectionZone { get; set; }

        //Ports MainForm's dark bg (23,23,23)/fg (124,124,124) constants (upstream MainForm.cs:37) - the
        //canvas half of ChangeTheme's control-tree walk, since this control has no WinForms BackColor of its
        //own to inherit the recursive BackColor/ForeColor assignment from.
        private static readonly SKColor DarkBackground = new(23, 23, 23);
        private static readonly SKColor DarkForeground = new(124, 124, 124);
        private static readonly SKColor LightBackground = SKColors.White;
        private static readonly SKColor LightForeground = new(200, 200, 200);

        public SKColor BackgroundColor { get; private set; } = LightBackground;

        //Fixed color/width, safe as a shared static across every viewer.
        private static readonly SKPaint PausedBorderPaint = new() { Color = new SKColor(255, 80, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 5 };

        //Per-viewer mutate-reset paint: StrokeWidth tracks this viewer's own ViewScale per frame.
        private readonly SKPaint _selectionZonePaint = new() { Color = new SKColor(100, 100, 200), Style = SKPaintStyle.Stroke, IsAntialias = true };

        public GraphViewer(Viewport viewport, GridManager grid) {
            Viewport = viewport;
            Grid = grid;
            Graph = new ProductionGraph();
            Session = new ProductionGraphSession(Graph);
            Context = new NodeElementContext(Session.View, viewport) {
                LinkWidthLookup = GetLinkWidth,
                Editor = Session.Editor,
                GetNodeElement = id => NodeElementDictionary.TryGetValue(id, out BaseNodeElement? element) ? element : null,
                SelectedNodes = SelectedNodes,
                ClearSelection = ClearSelection,
                TryDeleteSelectedNodes = TryDeleteSelectedNodes,
                FlipSelectedNodes = FlipSelectedNodes,
                AutoconnectSelectionInputs = AutoconnectSelectionInputs,
                AutoconnectSelectionOutputs = AutoconnectSelectionOutputs,
            };
            Context.EnableExtraProductivityForNonMiners = Graph.EnableExtraProductivityForNonMiners;
            ArrowRenderer = new PointingArrowRenderer(viewport);

            Session.NodeViewModelAdded += OnNodeViewModelAdded;
            Session.NodeViewModelRemoved += OnNodeViewModelRemoved;
            Session.LinkViewModelAdded += OnLinkViewModelAdded;
            Session.LinkViewModelRemoved += OnLinkViewModelRemoved;
            Session.NodeValuesUpdated += (_, _) => UpdateNodeVisuals();
            Session.GraphCleared += OnGraphCleared;
            Session.Attach();
        }

        private float GetLinkWidth(LinkId id) =>
            LinkElementDictionary.TryGetValue(id, out LinkElement? element) ? element.LinkWidth : Context.StaticLinkWidth;

        //Ports ApplyLoadedSaveDocument's read-only slice (reference: upstream ProductionGraphViewer.cs
        //ApplyLoadedSaveDocument/ApplySaveUi): the DataCache to load into is already resolved by the caller
        //(MainWindow.ResolveChosenPresetAsync for a file load, or the settings preset switch for a reload) -
        //this only applies the parsed document onto it.
        public GraphLoadResult LoadDocument(DataCache cache, string json, bool setEnablesFromJson = true) {
            GraphViewerSaveDocument? document = GraphSaveCodec.ReadViewer(json);
            if (document is null)
                return GraphLoadResult.Failure("This save file is too old or corrupt. Try opening it in the previous Foreman release and saving it again, then open the new file here.");
            return LoadDocument(cache, document, setEnablesFromJson);
        }

        public GraphLoadResult LoadDocument(DataCache cache, GraphViewerSaveDocument document, bool setEnablesFromJson = true) {
            Context.DCache = cache;
            Graph.ClearGraph();

            if (document.Ui is GraphViewerUiSaveData ui) {
                Graph.SelectedRateUnit = ui.Unit;
                Graph.EnableExtraProductivityForNonMiners = ui.ExtraProdForNonMiners;
                Context.EnableExtraProductivityForNonMiners = ui.ExtraProdForNonMiners;
                Graph.AssemblerSelector.DefaultSelectionStyle = ui.AssemblerSelectorStyle;
                Graph.ModuleSelector.DefaultSelectionStyle = ui.ModuleSelectorStyle;
                Viewport.ViewOffset = ui.ViewOffset;
                Viewport.ViewScale = ui.ViewScale;
                Viewport.UpdateGraphBounds();

                foreach (string fuelName in ui.FuelPriorityList) {
                    if (cache.Items.TryGetValue(fuelName, out IItem? fuelItem) && fuelItem is not null)
                        Graph.FuelSelector.UseFuel(fuelItem);
                }

                //Ports ApplySaveUi's setEnablesFromJson branch (upstream ProductionGraphViewer.cs:1289-1295):
                //a plain file load wants the save's own enabled set; a preset-switch reload (MainWindow.
                //ReloadForPresetAsync) passes false so the freshly booted cache's own default-enabled state
                //survives instead of being zeroed against the OLD preset's enabled-name list.
                if (setEnablesFromJson) {
                    ApplyEnabledList(cache.Beacons.Values, cache.Beacons, ui.EnabledBeacons, (b, e) => b.Enabled = e);
                    ApplyEnabledList(cache.Assemblers.Values, cache.Assemblers, ui.EnabledAssemblers, (a, e) => a.Enabled = e);
                    cache.RocketAssembler?.Enabled = cache.Assemblers.TryGetValue("rocket-silo", out IAssembler? silo) && silo?.Enabled == true;
                    ApplyEnabledList(cache.Modules.Values, cache.Modules, ui.EnabledModules, (m, e) => m.Enabled = e);
                    ApplyEnabledList(cache.Recipes.Values, cache.Recipes, ui.EnabledRecipes, (r, e) => r.Enabled = e);
                }
            }

            foreach (AnnotationElement annotation in AnnotationLoader.LoadFromSave(document.Annotations, document.AnnotationDpi, AnnotationDeviceDpi))
                AddAnnotationElement(annotation);

            ProductionGraph.NewNodeBatch batch = GraphSaveLoader.LoadProductionGraph(Graph, cache, document.ProductionGraph, applySolverSettings: true);
            if (batch.NewNodes.Count == 0 && document.ProductionGraph.Nodes.Count > 0)
                return GraphLoadResult.Failure("The production graph in this save could not be loaded (nodes failed to import).");

            Graph.UpdateNodeValues();

            //Ports upstream's post-load UpdateGraphBounds() call (ProductionGraphViewer.cs:1361/1537),
            //clamping the just-restored view against the graph's now-known bounds - the earlier
            //Viewport.UpdateGraphBounds() call above runs before any node exists, so it can only ever see
            //an empty rectangle and never actually clamps anything.
            Viewport.UpdateGraphBounds(Graph.Bounds);
            return GraphLoadResult.Ok;
        }

        //Ports ApplySaveUi's ApplyEnabledList helper (upstream ProductionGraphViewer.cs:1298-1309): resets
        //every entity to disabled, then re-enables only the ones the save's own name list carries.
        private static void ApplyEnabledList<T>(
            IEnumerable<T> all,
            IReadOnlyDictionary<string, T> byName,
            IReadOnlyList<string> enabledNames,
            Action<T, bool> setEnabled) where T : class {
            foreach (T item in all)
                setEnabled(item, false);
            foreach (string name in enabledNames) {
                if (byName.TryGetValue(name, out T? entry))
                    setEnabled(entry, true);
            }
        }

        //----------------------------------------------Import (reference §2/§5, upstream
        //ProductionGraphViewer.cs:1311-1363): merges an external document into the live graph rather than
        //replacing it, unlike LoadDocument above. NodeClipboard's fragment paste and MainWindow's Import
        //Graph command both route through this pair, differing only in applySolverSettings and in how far
        //up the caller already parsed the payload.

        //Ports ImportNodesFromDocument's merge/offset/selection algorithm: centers the newly inserted
        //nodes' centroid on origin via a grid-aligned rigid offset, preserving their relative layout, then
        //replaces the selection with just the imported nodes - SetSelection already syncs Highlighted.
        //Returns the number of nodes actually imported, so callers can tell an empty batch apart from a
        //real merge.
        public int ImportNodesFromDocument(DataCache cache, ProductionGraphSaveDocument document, Point origin, bool applySolverSettings) {
            HashSet<NodeId> existingIds = [.. NodeElementDictionary.Keys];
            ProductionGraph.NewNodeBatch batch = Graph.InsertNodesFromDocument(cache, document, applySolverSettings);
            if (batch.NewNodes.Count == 0)
                return 0;

            List<BaseNodeElement> importedElements = [.. NodeElementDictionary.Where(kv => !existingIds.Contains(kv.Key)).Select(kv => kv.Value)];
            if (importedElements.Count == 0)
                return 0;

            long xAverage = 0;
            long yAverage = 0;
            foreach (BaseNodeElement element in importedElements) {
                xAverage += element.ViewModel.Location.X;
                yAverage += element.ViewModel.Location.Y;
            }
            var centroid = new Point((int)(xAverage / importedElements.Count), (int)(yAverage / importedElements.Count));
            Point offset = Grid.AlignToGrid(Point.Subtract(origin, (Size)centroid));

            foreach (BaseNodeElement element in importedElements)
                element.SetLocation(Point.Add(element.ViewModel.Location, (Size)offset));

            SetSelection(importedElements);
            Viewport.UpdateGraphBounds(Graph.Bounds);
            Graph.UpdateNodeValues();
            return importedElements.Count;
        }

        //Ports ImportNodesFromFragment's node half: parses either a bare graph fragment or a full viewer
        //save via ReadGraphPayload's dual-accept, silently no-oping on anything that isn't a current-format
        //graph (matching upstream's LogLine rather than throwing) - Import Graph's own corrupt-file message
        //comes from its caller validating ReadGraphPayload directly instead.
        public int ImportNodesFromFragment(DataCache cache, string json, Point origin, bool applySolverSettings) {
            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadGraphPayload(json);
            if (document is null) {
                ErrorLogging.LogLine("ImportNodesFromFragment: clipboard JSON is not a current-format graph or viewer save fragment");
                return 0;
            }
            return ImportNodesFromDocument(cache, document, origin, applySolverSettings);
        }

        //----------------------------------------------Selection functions (reference §1)

        //Ports SetSelection: replaces the full selection outright, syncing every node's Highlighted flag
        //with membership.
        public void SetSelection(IEnumerable<BaseNodeElement> newSelection) {
            foreach (BaseNodeElement element in SelectedNodes)
                element.Highlighted = false;

            SelectedNodes.Clear();
            SelectedNodes.UnionWith(newSelection);

            foreach (BaseNodeElement element in SelectedNodes)
                element.Highlighted = true;
        }

        //Ports UpdateSelection: recomputes every node's Highlighted flag as a lasso preview for the given
        //modifier - SelectedNodes itself isn't touched until CommitLassoSelection.
        public void UpdateSelection(SelectionModifier modifier) {
            foreach (BaseNodeElement element in NodeElements)
                element.Highlighted = false;

            switch (modifier) {
                case SelectionModifier.Remove:
                    foreach (BaseNodeElement node in SelectedNodes)
                        node.Highlighted = true;
                    foreach (BaseNodeElement node in CurrentSelectionNodes)
                        node.Highlighted = false;
                    break;
                case SelectionModifier.Add:
                    foreach (BaseNodeElement node in SelectedNodes)
                        node.Highlighted = true;
                    foreach (BaseNodeElement node in CurrentSelectionNodes)
                        node.Highlighted = true;
                    break;
                default:
                    foreach (BaseNodeElement node in CurrentSelectionNodes)
                        node.Highlighted = true;
                    break;
            }
        }

        //Ports the lasso-commit half of ProductionGraphViewer_MouseUp's Left/Selection branch: folds
        //CurrentSelectionNodes into SelectedNodes per modifier, without touching Highlighted - the drag's
        //last UpdateSelection preview already left every flag matching this outcome.
        public void CommitLassoSelection(SelectionModifier modifier) {
            if (modifier == SelectionModifier.Remove)
                SelectedNodes.ExceptWith(CurrentSelectionNodes);
            else {
                if (modifier != SelectionModifier.Add)
                    SelectedNodes.Clear();
                SelectedNodes.UnionWith(CurrentSelectionNodes);
            }
            CurrentSelectionNodes.Clear();
        }

        //Extended to also clear annotation selection (reference §1/§4a's MouseDown clear-on-empty-click and
        //§6's ClearSelection, both of which clear nodes and annotations together upstream).
        public void ClearSelection() {
            foreach (BaseNodeElement element in NodeElements)
                element.Highlighted = false;
            SelectedNodes.Clear();
            CurrentSelectionNodes.Clear();
            ClearAnnotationSelection();
        }

        //Ports TryDeleteSelectedNodes' >10 confirm (reference §4b): upstream blocks synchronously on a
        //WinForms MessageBox; this port has no synchronous confirm dialog, so ConfirmBulkDelete is an
        //injectable hook (null = proceed unconfirmed) - see docs/upstream-divergences.md.
        public Func<int, bool>? ConfirmBulkDelete { get; set; }

        public void TryDeleteSelectedNodes() {
            if (SelectedNodes.Count > 10 && ConfirmBulkDelete is { } confirm && !confirm(SelectedNodes.Count))
                return;

            foreach (BaseNodeElement node in SelectedNodes.ToList())
                Session.Editor.DeleteNode(node.ViewModel.Id);
            SelectedNodes.Clear();
            Graph.UpdateNodeValues();
        }

        public void FlipSelectedNodes() {
            foreach (BaseNodeElement node in SelectedNodes.ToList()) {
                if (Session.Editor.RequestNodeController(node.ViewModel.Id) is BaseNodeController controller)
                    controller.SetDirection(node.ViewModel.NodeDirection == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up);
            }
        }

        //Ports AlignSelected (reference §8/§11 step 11's Align Selected wiring): grid-snaps every selected
        //node in place, no-op for anything not on the grid already.
        public void AlignSelected() {
            foreach (BaseNodeElement node in SelectedNodes)
                node.SetLocation(Grid.AlignToGrid(node.Location));
        }

        //Ports AutoconnectDisconnectedInputs (reference §8): the whole-graph pass behind the Autoconnect
        //toolbar button. GraphAutoconnect.ConnectDisconnectedInputs already runs Graph.UpdateNodeValues()
        //internally when it creates a link; this only adds the UpdateNodeStates(false) upstream's own
        //button handler layers on top.
        public int AutoconnectDisconnectedInputs() {
            int linksCreated = GraphAutoconnect.ConnectDisconnectedInputs(Session);
            if (linksCreated > 0)
                Graph.UpdateNodeStates(false);
            return linksCreated;
        }

        //Ports the node right-click menu's selection-scoped auto-connect pair (reference §4b), consolidated
        //onto Core's GraphAutoconnect scoped overloads instead of a second hand-rolled algorithm (reference
        //§11 step 11).
        public void AutoconnectSelectionInputs() =>
            GraphAutoconnect.ConnectDisconnectedInputs(Session, [.. SelectedNodes.Select(n => n.ViewModel.Id)]);

        public void AutoconnectSelectionOutputs() =>
            GraphAutoconnect.ConnectDisconnectedOutputs(Session, [.. SelectedNodes.Select(n => n.ViewModel.Id)]);

        //draggedLink carries GraphCanvasControl's in-flight DraggedLinkElement (reference §3's GetPaintingOrder
        //entry, upstream ProductionGraphViewer.cs:642-651): this class has no drag-lifecycle state of its own
        //(that's GraphCanvasControl's job, see its header comment), so the caller threads the ghost through.
        public IEnumerable<GraphElement> GetPaintingOrder(GraphElement? draggedLink = null) {
            foreach (AnnotationElement element in Annotations)
                yield return element;
            if (draggedLink is not null)
                yield return draggedLink;
            foreach (LinkElement element in LinkElements)
                yield return element;
            foreach (BaseNodeElement element in NodeElements)
                yield return element;
        }

        //Ports Paint(Graphics, FullGraph) (reference §3 steps 2-8): visibility pass, grid, DynamicLinkWidth
        //pre-pass, PrePaint pass, then the style-selected main draw loop. Doesn't include steps 9-12 (the
        //ResetTransform-and-beyond screen-space overlays); those are PaintOverlays/PaintPausedBorder below,
        //called separately so GraphCanvasControl can interleave its own hover-tooltip draw between them in
        //upstream's exact z-order (arrows, tooltip, paused border last/topmost).
        //Ports MainForm.SetDarkMode/SetLightMode (upstream MainForm.cs:36-46): recolors the canvas clear
        //color and hands the same bg/fg pair to GridManager.SetGridColors, matching ChangeTheme's own call
        //into ProductionGraphViewer's grid when it walks the control tree. Unlike upstream's dark-restores-
        //to-DefaultBackColor/DefaultForeColor split (WinForms SystemColors with no Avalonia/macOS
        //equivalent), the light path here returns to this port's own cold-start light constants - see
        //docs/upstream-divergences.md.
        public void ApplyTheme(bool dark) {
            SKColor bg = dark ? DarkBackground : LightBackground;
            SKColor fg = dark ? DarkForeground : LightForeground;
            BackgroundColor = bg;
            Grid.SetGridColors(bg, fg);
        }

        //Ports ImageExportForm.ExportBitmap's two transform variants (reference §3): upstream sets its own
        //Graphics.Transform before calling graphViewer.Paint(fullGraph: true), since ProductionGraphViewer.
        //Paint never resets/builds a transform itself there. This port's Paint always does, off the live
        //Viewport, so PNG export instead passes a stand-in Width/Height/ViewScale/ViewOffset through here
        //rather than mutating the live Viewport (which has clamping side effects via UpdateGraphBounds).
        public readonly record struct ExportTransform(double Width, double Height, float ViewScale, Point ViewOffset);

        public void Paint(SKCanvas canvas, bool fullGraph = false, bool draggedNodeActive = false, GraphElement? draggedLink = null, bool clearBackground = true, ExportTransform? exportTransform = null) {
            if (clearBackground)
                canvas.Clear(BackgroundColor);
            canvas.Save();
            double width = exportTransform?.Width ?? Viewport.Width;
            double height = exportTransform?.Height ?? Viewport.Height;
            float viewScale = exportTransform?.ViewScale ?? Viewport.ViewScale;
            Point viewOffset = exportTransform?.ViewOffset ?? Viewport.ViewOffset;
            canvas.Translate((float)(width / 2), (float)(height / 2));
            canvas.Scale(viewScale);
            canvas.Translate(viewOffset.X, viewOffset.Y);

            Rectangle visibilityBounds = fullGraph ? Graph.Bounds : Viewport.VisibleGraphBounds;
            foreach (GraphElement element in GetPaintingOrder(draggedLink))
                element.UpdateVisibility(visibilityBounds);
            if (fullGraph)
                foreach (AnnotationElement annotation in Annotations)
                    annotation.ForceVisible();

            if (!fullGraph)
                Grid.Paint(canvas, Viewport.ViewScale, Viewport.VisibleGraphBounds, draggedNodeActive);

            UpdateLinkWidths();

            foreach (GraphElement element in GetPaintingOrder(draggedLink))
                element.PrePaint();

            int visibleElements = GetPaintingOrder(draggedLink).Count(e => e.Visible && e is BaseNodeElement);
            NodeDrawingStyle style = fullGraph ? NodeDrawingStyle.PrintStyle
                : IconsOnly ? NodeDrawingStyle.IconsOnly
                : (visibleElements > NodeCountForSimpleView || Viewport.ViewScale < 0.2f) ? NodeDrawingStyle.Simple
                : NodeDrawingStyle.Regular;
            foreach (GraphElement element in GetPaintingOrder(draggedLink))
                element.Paint(canvas, style);

            //selection zone (reference §1 Paint: selectionPen, RGB(100,100,200), 2 screen-px wide regardless
            //of zoom - hence the ViewScale-compensated stroke width, drawn in the same graph-space transform
            //as everything above so it tracks pan/zoom like the elements it's lassoing).
            if (!fullGraph && SelectionZone is Rectangle zone && zone.Width > 0 && zone.Height > 0) {
                _selectionZonePaint.StrokeWidth = 2f / Viewport.ViewScale;
                canvas.DrawRect(SKRect.Create(zone.X, zone.Y, zone.Width, zone.Height), _selectionZonePaint);
            }

            canvas.Restore();

            if (!fullGraph)
                ArrowRenderer.Paint(canvas, Graph);
        }

        //Ports UpdateNodeVisuals (upstream ProductionGraphViewer.cs:653-660), minus the trailing Invalidate()
        //call - GraphViewer doesn't own control-level redraw (GraphCanvasControl does), so whoever mutated
        //the values invalidates the canvas itself, same as every other value/state change in this port.
        public void UpdateNodeVisuals() {
            try {
                foreach (BaseNodeElement node in NodeElements)
                    node.RequestStateUpdate();
            } catch (OverflowException ex) {
                ErrorLogging.LogException(ex, "UpdateNodeVisuals overflow while refreshing node elements");
            }
        }

        public void PaintPausedBorder(SKCanvas canvas) {
            canvas.DrawRect(0, 0, (float)Viewport.Width - 3, (float)Viewport.Height - 3, PausedBorderPaint);
        }

        //Final-review I2: GraphCanvasControl.OnDetachedFromVisualTree is this class's only caller now (it
        //owns the GraphViewer it constructs), and Avalonia can raise a detach more than once across a
        //control's lifetime (e.g. a reattach cycle) - the guard keeps a second call a no-op instead of a
        //double-dispose of _selectionZonePaint/Grid's own SKPaints.
        public bool IsDisposed { get; private set; }

        public void Dispose() {
            if (IsDisposed)
                return;
            IsDisposed = true;
            _selectionZonePaint.Dispose();
            Grid.Dispose();
        }

        private void UpdateLinkWidths() {
            if (!Context.DynamicLinkWidth) {
                foreach (LinkElement element in LinkElements)
                    element.LinkWidth = MinLinkWidth;
                return;
            }

            double itemMax = 0;
            double fluidMax = 0;
            foreach (LinkElement element in LinkElements) {
                if (element.ConsumerElement is not BaseNodeElement consumerElement)
                    continue;
                if (element.Item.Item is IFluid fluid && !fluid.Name.StartsWith("§§", StringComparison.Ordinal))
                    fluidMax = Math.Max(fluidMax, consumerElement.ViewModel.GetConsumeRate(element.Item));
                else if (element.Item.Item is not null)
                    itemMax = Math.Max(itemMax, consumerElement.ViewModel.GetConsumeRate(element.Item));
            }
            itemMax += itemMax == 0 ? 1 : 0;
            fluidMax += fluidMax == 0 ? 1 : 0;

            foreach (LinkElement element in LinkElements)
                element.LinkWidth = element.Item.Item is IFluid
                    ? (float)Math.Min(MinLinkWidth + ((MaxLinkWidth - MinLinkWidth) * (element.ViewModel.Throughput / fluidMax)), MaxLinkWidth)
                    : (float)Math.Min(MinLinkWidth + ((MaxLinkWidth - MinLinkWidth) * (element.ViewModel.Throughput / itemMax)), MaxLinkWidth);
        }

        private void OnNodeViewModelAdded(object? sender, NodeViewModelEventArgs e) {
            BaseNodeElement? element = CreateNodeElement(e.ViewModel);
            if (element is null)
                return;
            NodeElementDictionary.Add(e.ViewModel.Id, element);
            NodeElements.Add(element);
        }

        private void OnNodeViewModelRemoved(object? sender, NodeViewModelEventArgs e) {
            if (!NodeElementDictionary.TryGetValue(e.ViewModel.Id, out BaseNodeElement? element))
                return;
            NodeElementDictionary.Remove(e.ViewModel.Id);
            NodeElements.Remove(element);
            SelectedNodes.Remove(element);
            element.Dispose();
        }

        private void OnLinkViewModelAdded(object? sender, LinkViewModelEventArgs e) {
            INodeLinkViewModel link = e.ViewModel;
            if (!NodeElementDictionary.TryGetValue(link.SupplierId, out BaseNodeElement? supplier) ||
                !NodeElementDictionary.TryGetValue(link.ConsumerId, out BaseNodeElement? consumer))
                return;

            var element = new LinkElement(Context, link, supplier, consumer);
            LinkElementDictionary.Add(link.Id, element);
            LinkElements.Add(element);

            supplier.RequestStateUpdate();
            consumer.RequestStateUpdate();
        }

        private void OnLinkViewModelRemoved(object? sender, LinkViewModelEventArgs e) {
            if (!LinkElementDictionary.TryGetValue(e.ViewModel.Id, out LinkElement? element))
                return;

            LinkElementDictionary.Remove(e.ViewModel.Id);
            LinkElements.Remove(element);
            element.Dispose();

            if (NodeElementDictionary.TryGetValue(e.ViewModel.SupplierId, out BaseNodeElement? supplier))
                supplier.RequestStateUpdate();
            if (NodeElementDictionary.TryGetValue(e.ViewModel.ConsumerId, out BaseNodeElement? consumer))
                consumer.RequestStateUpdate();
        }

        private void OnGraphCleared(object? sender, EventArgs e) {
            foreach (BaseNodeElement element in NodeElements.ToList())
                element.Dispose();
            foreach (LinkElement element in LinkElements.ToList())
                element.Dispose();
            NodeElementDictionary.Clear();
            NodeElements.Clear();
            LinkElementDictionary.Clear();
            LinkElements.Clear();
            ClearAnnotations();
        }

        private void ClearAnnotations() {
            foreach (AnnotationElement annotation in Annotations.ToList())
                annotation.Dispose();
            Annotations.Clear();
            SelectedAnnotations.Clear();
        }

        private BaseNodeElement? CreateNodeElement(INodeViewModel viewModel) => viewModel switch {
            ISupplierNodeViewModel supplier => new SupplierNodeElement(Context, supplier),
            IConsumerNodeViewModel consumer => new ConsumerNodeElement(Context, consumer),
            IPassthroughNodeViewModel passthrough => new PassthroughNodeElement(Context, passthrough),
            IRecipeNodeViewModel recipe => new RecipeNodeElement(Context, recipe),
            ISpoilNodeViewModel spoil => new SpoilNodeElement(Context, spoil),
            IPlantNodeViewModel plant => new PlantNodeElement(Context, plant),
            _ => null,
        };
    }
}
