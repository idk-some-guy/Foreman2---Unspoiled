using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Models;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/BaseNodeElement.cs: Draw/DetailsDraw dispatch, UpdateState/
    //UpdateValues, PrePaint, ContainsPoint, the GetToolTips cascade, UpdateTabOrder, drag (SetLocation,
    //MouseDown, Dragged), and MouseUpAction's base menu items (reference §4b). The ~280-line paste-options
    //body (AddRClickMenuOptions, reference §4c) stays a no-op here; RecipeNodeElement overrides it.
    public abstract class BaseNodeElement : GraphElement {
        public bool Highlighted { get; set; }
        public INodeViewModel ViewModel { get; }

        public override int X { get => ViewModel.Location.X; set => throw new NotSupportedException("Node location is driven by the view model in this read-only port."); }
        public override int Y { get => ViewModel.Location.Y; set => throw new NotSupportedException("Node location is driven by the view model in this read-only port."); }
        public override Point Location { get => ViewModel.Location; set => throw new NotSupportedException("Node location is driven by the view model in this read-only port."); }

        //Ports SetLocation (reference §2): routes through Context.Editor/GetNodeElement rather than a
        //graphViewer reference, since node elements in this port only ever see NodeElementContext.
        public void SetLocation(Point location) {
            if (location == Location)
                return;

            Context.Editor?.SetLocation(ViewModel.Id, location);
            RequestStateUpdate();
            foreach (BaseNodeElement linkedNode in ViewModel.InputLinks.Select(l => Context.GetNodeElement?.Invoke(l.SupplierId)).OfType<BaseNodeElement>())
                linkedNode.RequestStateUpdate();
            foreach (BaseNodeElement linkedNode in ViewModel.OutputLinks.Select(l => Context.GetNodeElement?.Invoke(l.ConsumerId)).OfType<BaseNodeElement>())
                linkedNode.RequestStateUpdate();
        }

        //Fixed colors, safe as shared statics across every viewer.
        protected abstract SKPaint CleanBgPaint { get; }
        private static readonly SKPaint ErrorBgPaint = Fill(new SKColor(255, 127, 80));
        private static readonly SKPaint ManualRateFilterPaint = Fill(new SKColor(0, 0, 0, 50));

        private static readonly SKPaint EqualFlowBorderPaint = Fill(new SKColor(0, 100, 0));
        private static readonly SKPaint OverproducingFlowBorderPaint = Fill(new SKColor(184, 134, 11));
        private static readonly SKPaint UndersuppliedFlowBorderPaint = Fill(new SKColor(139, 0, 0));

        protected static readonly SKColor SelectionOverlayColor = new(100, 100, 200, 100);
        private static readonly SKPaint SelectionOverlayPaint = Fill(SelectionOverlayColor);

        protected static readonly SKColor TextColor = SKColors.Black;
        protected static readonly SKTypeface RegularTypeface = SKTypeface.Default;
        protected static readonly SKTypeface BoldTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        protected const float BaseFontSize = 10f;
        protected const float CounterBaseFontSize = 14f;
        protected const float TitleFontSize = 9.2f;

        //most values are attempted to fit the grid (6 * 2^n) - ex: 72 = 6 * (4+8)
        protected const int BaseSimpleHeight = 96;
        protected const int BaseRecipeHeight = 144;
        protected const int TabPadding = 7;
        protected const int WidthD = 24;
        protected const int PassthroughNodeWidth = WidthD * 3;
        protected const int MinWidth = WidthD * 6;
        protected const int BorderSpacing = 1;

        protected List<ItemTabElement> InputTabs { get; set; }
        protected List<ItemTabElement> OutputTabs { get; set; }

        private bool nodeStateRequiresUpdate;
        private bool nodeValuesRequireUpdate;

        //Ports MouseDownLocation/MouseDownNodeLocation/DragStarted (reference §2): graph-space snapshot of
        //where the drag began, taken at MouseDown so a drag always starts from the true origin rather than
        //wherever it first crossed the threshold.
        private Point mouseDownLocation;
        private Point mouseDownNodeLocation;
        private bool dragStarted;

        protected NodeElementContext Context { get; }
        protected ErrorNoticeElement ErrorNotice { get; }

        protected BaseNodeElement(NodeElementContext context, INodeViewModel viewModel) : base(parent: null) {
            Context = context;
            ViewModel = viewModel;
            ViewModel.NodeStateChanged += (_, _) => nodeStateRequiresUpdate = true;
            ViewModel.NodeValuesChanged += (_, _) => nodeValuesRequireUpdate = true;

            InputTabs = [];
            OutputTabs = [];

            ErrorNotice = new ErrorNoticeElement(context, this) { Location = new Point(-Width / 2, -Height / 2) };
            ErrorNotice.SetVisibility(false);

            foreach (ItemQualityPair item in ViewModel.Inputs)
                InputTabs.Add(new ItemTabElement(item, LinkType.Input, context, this));
            foreach (ItemQualityPair item in ViewModel.Outputs)
                OutputTabs.Add(new ItemTabElement(item, LinkType.Output, context, this));
        }

        public void RequestStateUpdate() => nodeStateRequiresUpdate = true;

        //Test-only counter (nothing outside Foreman.Mac.UiTests reads this) proving a state resync actually
        //ran, since UpdateState's real effects (tab order, error-notice placement) have no other cheap,
        //deterministic hook to assert against from outside the element tree.
        internal int UpdateStateCallCount { get; private set; }

        protected virtual void UpdateState() {
            UpdateStateCallCount++;
            ErrorNotice.SetVisibility(ViewModel.State == NodeState.Error || ViewModel.State == NodeState.Warning);
            ErrorNotice.X = -Width / 2;
            ErrorNotice.Y = -Height / 2;

            UpdateTabOrder();
        }

        protected virtual void UpdateValues() {
            foreach (ItemTabElement tab in InputTabs)
                tab.UpdateValues(ViewModel.GetConsumeRate(tab.Item), 0, false);
            foreach (ItemTabElement tab in OutputTabs)
                tab.UpdateValues(ViewModel.GetSupplyRate(tab.Item), ViewModel.GetSupplyUsedRate(tab.Item), ViewModel.IsOverproducing(tab.Item));
        }

        private void UpdateTabOrder() {
            InputTabs = [.. InputTabs.OrderBy(GetItemTabXHeuristic).ThenBy(it => it.Item.Item?.Name).ThenBy(it => it.Item.Quality?.Level).ThenBy(it => it.Item.Quality?.Name)];
            OutputTabs = [.. OutputTabs.OrderBy(GetItemTabXHeuristic).ThenBy(it => it.Item.Item?.Name).ThenBy(it => it.Item.Quality?.Level).ThenBy(it => it.Item.Quality?.Name)];

            int x = -GetIconWidths(OutputTabs) / 2;
            int y = ViewModel.NodeDirection == NodeDirection.Up ? (-Height / 2) + 1 : (Height / 2) - 1;
            foreach (ItemTabElement tab in OutputTabs) {
                x += TabPadding;
                tab.Location = new Point(x + (tab.Width / 2), y);
                x += tab.Width;
            }

            x = -GetIconWidths(InputTabs) / 2;
            y = ViewModel.NodeDirection == NodeDirection.Up ? (Height / 2) - 1 : (-Height / 2) + 1;
            foreach (ItemTabElement tab in InputTabs) {
                x += TabPadding;
                tab.Location = new Point(x + (tab.Width / 2), y);
                x += tab.Width;
            }
        }

        protected static int GetIconWidths(List<ItemTabElement> tabs) {
            int result = TabPadding;
            foreach (ItemTabElement tab in tabs)
                result += tab.Bounds.Width + TabPadding;
            return result;
        }

        private int GetItemTabXHeuristic(ItemTabElement tab) {
            int total = 0;
            foreach (INodeLinkViewModel link in tab.Links) {
                if (!Context.View.TryGetNode(link.SupplierId, out INodeViewModel? supplier) || supplier is null ||
                    !Context.View.TryGetNode(link.ConsumerId, out INodeViewModel? consumer) || consumer is null)
                    continue;
                Point diff = Point.Subtract(supplier.Location, (Size)consumer.Location);
                total += Convert.ToInt32(Math.Atan2(tab.LinkType == LinkType.Input ? diff.X : -diff.X, diff.Y) * 1000 + (diff.Y > 0 ? 1 : 0));
            }
            return total;
        }

        //Resolves a tab by item for LinkElement's endpoint lookup (upstream BaseNodeElement.cs:156-168).
        public ItemTabElement GetOutputLineItemTab(ItemQualityPair item) {
            if (nodeStateRequiresUpdate)
                UpdateState();
            nodeStateRequiresUpdate = false;
            return OutputTabs.First(it => it.Item == item);
        }

        public ItemTabElement GetInputLineItemTab(ItemQualityPair item) {
            if (nodeStateRequiresUpdate)
                UpdateState();
            nodeStateRequiresUpdate = false;
            return InputTabs.First(it => it.Item == item);
        }

        public override void UpdateVisibility(Rectangle graphZone, int xborder = 0, int yborder = 0) {
            base.UpdateVisibility(graphZone, xborder, yborder + 30);
        }

        public override bool ContainsPoint(Point graphPoint) {
            if (!Visible)
                return false;
            if (base.ContainsPoint(graphPoint))
                return true;

            foreach (ItemTabElement tab in SubElements.OfType<ItemTabElement>())
                if (tab.ContainsPoint(graphPoint))
                    return true;
            return ErrorNotice.ContainsPoint(graphPoint);
        }

        public override void PrePaint() {
            if (nodeStateRequiresUpdate)
                UpdateState();
            if (nodeStateRequiresUpdate || nodeValuesRequireUpdate)
                UpdateValues();
            nodeStateRequiresUpdate = false;
            nodeValuesRequireUpdate = false;
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            Point trans = LocalToGraph(new Point(0, 0));
            if (style == NodeDrawingStyle.IconsOnly) {
                if (NodeIcon() is SKBitmap nodeIcon)
                    canvas.DrawBitmap(nodeIcon, SKRect.Create(trans.X - (Context.IconsDrawSize / 2), trans.Y - (Context.IconsDrawSize / 2), Context.IconsDrawSize, Context.IconsDrawSize));
                return;
            }

            SKPaint bgPaint = ViewModel.State == NodeState.Error ? ErrorBgPaint : CleanBgPaint;
            SKPaint borderPaint = ViewModel.ManualRateNotMet() && this is not SupplierNodeElement ? UndersuppliedFlowBorderPaint
                : ViewModel.IsOverproducing() ? OverproducingFlowBorderPaint : EqualFlowBorderPaint;

            GraphicsStuff.FillRoundRect(canvas, trans.X - (Width / 2) + BorderSpacing, trans.Y - (Height / 2) + BorderSpacing, Width - (2 * BorderSpacing), Height - (2 * BorderSpacing), 10, borderPaint);

            int yoffset = ViewModel.KeyNode && this is not ConsumerNodeElement ? 15 : 0;
            int heightOffset = ViewModel.KeyNode ? (this is ConsumerNodeElement or SupplierNodeElement ? 15 : 30) : 0;
            GraphicsStuff.FillRoundRect(canvas, trans.X - (Width / 2) + BorderSpacing + 3, trans.Y - (Height / 2) + BorderSpacing + 3 + yoffset, Width - (2 * BorderSpacing) - 6, Height - (2 * BorderSpacing) - 6 - heightOffset, 7, bgPaint);
            if (ViewModel.RateType == RateType.Manual)
                GraphicsStuff.FillRoundRect(canvas, trans.X - (Width / 2) + 3, trans.Y - (Height / 2) + 3, Width - 6, Height - 6, 7, ManualRateFilterPaint);

            if (Context.FlagOUSuppliedNodes && borderPaint != EqualFlowBorderPaint)
                GraphicsStuff.FillRoundRectTLFlag(canvas, trans.X - (Width / 2) + 3, trans.Y - (Height / 2) + 3, (Width / 2) - 6, (Height / 2) - 6, 7, borderPaint);
            if (ViewModel.State == NodeState.Warning)
                GraphicsStuff.FillRoundRectTLFlag(canvas, trans.X - (Width / 2) + 3, trans.Y - (Height / 2) + 3, (Width / 2) - 6, (Height / 2) - 6, 7, ErrorBgPaint);

            if (style == NodeDrawingStyle.Regular || style == NodeDrawingStyle.PrintStyle)
                DetailsDraw(canvas, trans);

            if (Highlighted)
                GraphicsStuff.FillRoundRect(canvas, trans.X - (Width / 2), trans.Y - (Height / 2), Width, Height, 8, SelectionOverlayPaint);
        }

        protected static SKPaint Fill(SKColor color) => new() { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };

        protected abstract void DetailsDraw(SKCanvas canvas, Point trans);
        protected abstract SKBitmap? NodeIcon();

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            GraphElement? element = SubElements.FirstOrDefault(it => it.ContainsPoint(graphPoint));
            List<TooltipInfo>? subTooltips = element?.GetToolTips(graphPoint);
            List<TooltipInfo> myTooltips = GetMyToolTips(graphPoint, subTooltips is null || subTooltips.Count == 0);

            if (subTooltips is not null)
                myTooltips.AddRange(subTooltips);

            return myTooltips;
        }

        protected abstract List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive);

        protected static List<TooltipInfo> ExclusiveHelpTooltip(string text, bool exclusive) =>
            exclusive ? [new TooltipInfo(new Avalonia.Point(10, 10), Direction.None, text)] : [];

        //Ports MouseDown's location-snapshot half (reference §2) - claiming graphViewer.MouseDownElement is
        //GraphCanvasControl's job, inlined at the pointer-press site same as Task 1 left it.
        public void MouseDown(Point graphPoint) {
            mouseDownLocation = graphPoint;
            mouseDownNodeLocation = Location;
            dragStarted = false;
        }

        //Ports Dragged (reference §2/§3). First post-threshold call only arms the drag and moves nothing, so
        //there's no jump on the threshold frame; a tab hit there redirects into a link drag via
        //Context.StartLinkDrag (GraphCanvasControl.StartLinkDrag, wired at construction) instead of arming
        //the node move. Once armed, grid snap is unconditional and Grid.LockDragToAxis clamps whichever axis
        //sits closer to Grid.DragOrigin.
        public void Dragged(Point graphPoint, GridManager grid) {
            if (!dragStarted) {
                ItemTabElement? tabHit = SubElements.OfType<ItemTabElement>().FirstOrDefault(tab => tab.ContainsPoint(mouseDownLocation));
                if (tabHit is null)
                    dragStarted = true;
                else
                    Context.StartLinkDrag?.Invoke(this, tabHit.LinkType, tabHit.Item);
                return;
            }

            Size offset = (Size)Point.Subtract(graphPoint, (Size)mouseDownLocation);
            Point newLocation = grid.AlignToGrid(Point.Add(mouseDownNodeLocation, offset));
            if (grid.LockDragToAxis) {
                Point lockedDragOffset = Point.Subtract(graphPoint, (Size)grid.DragOrigin);
                if (Math.Abs(lockedDragOffset.X) > Math.Abs(lockedDragOffset.Y))
                    newLocation.Y = grid.DragOrigin.Y;
                else
                    newLocation.X = grid.DragOrigin.X;
            }

            if (Location == newLocation)
                return;

            SetLocation(newLocation);
            UpdateTabOrder();
            foreach (BaseNodeElement linkedNode in ViewModel.InputLinks.Select(l => Context.GetNodeElement?.Invoke(l.SupplierId)).OfType<BaseNodeElement>())
                linkedNode.UpdateTabOrder();
            foreach (BaseNodeElement linkedNode in ViewModel.OutputLinks.Select(l => Context.GetNodeElement?.Invoke(l.ConsumerId)).OfType<BaseNodeElement>())
                linkedNode.UpdateTabOrder();
        }

        //Ports the non-drag half of MouseUp's subelement-first routing (reference §4, upstream lines
        //258-269): a tab or the error notice claims the point before falling back to this node's own
        //action - a plain left click on the node body opens its edit panel (upstream's MouseUpAction,
        //reference §8), matching EditNode's own node-type dispatch (Context.EditNode, wired by
        //GraphCanvasControl).
        public void MouseUpLeft(Point graphPoint) {
            if (SubElements.OfType<ItemTabElement>().Any(tab => tab.ContainsPoint(graphPoint)))
                return;
            if (ErrorNotice.ContainsPoint(graphPoint))
                ErrorNotice.Autoresolve();
            else
                Context.EditNode?.Invoke(this);
        }

        public List<MenuEntry> MouseUpRight(Point graphPoint) {
            ItemTabElement? tab = SubElements.OfType<ItemTabElement>().FirstOrDefault(t => t.ContainsPoint(graphPoint));
            if (tab is not null)
                return tab.BuildRightClickMenu();
            if (ErrorNotice.ContainsPoint(graphPoint))
                return ErrorNotice.BuildRightClickMenu();
            return BuildRightClickMenu();
        }

        //Ports MouseUpAction's right-click body (reference §4b), minus the auto-connect items (a
        //consolidation candidate the reference itself flags as not a verbatim re-port target) and
        //AddRClickMenuOptions' own ~280-line body (reference §4c, RecipeNodeElement's job).
        public List<MenuEntry> BuildRightClickMenu() {
            bool inMultiSelection = Context.SelectedNodes is { Count: > 1 } selected && selected.Contains(this);
            var entries = new List<MenuEntry> {
                MenuEntry.Item("Delete node", () => {
                    Context.Editor?.DeleteNode(ViewModel.Id);
                    Context.Editor?.Graph.UpdateNodeValues();
                })
            };
            if (inMultiSelection)
                entries.Add(MenuEntry.Item("Delete selected nodes", () => Context.TryDeleteSelectedNodes?.Invoke()));

            entries.Add(MenuEntry.Divider);
            entries.Add(MenuEntry.Item("Flip node", () =>
                Context.Editor?.SetDirection(ViewModel.Id, ViewModel.NodeDirection == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up)));
            if (inMultiSelection)
                entries.Add(MenuEntry.Item("Flip selected nodes", () => Context.FlipSelectedNodes?.Invoke()));

            if (Context.SelectedNodes is { Count: > 0 } withSelection) {
                entries.Add(MenuEntry.Divider);
                entries.Add(MenuEntry.Item("Clear selection", () => Context.ClearSelection?.Invoke()));
                AddAutoconnectSelectionOptions(entries, withSelection, Context);
            }

            bool nodeInSelection = Context.SelectedNodes is null or { Count: 0 } || Context.SelectedNodes.Contains(this);
            AddRClickMenuOptions(entries, nodeInSelection);

            entries.Add(MenuEntry.Divider);
            entries.Add(MenuEntry.Item("Copy key node status", () =>
                Context.SetClipboardText?.Invoke(GraphSaveCodec.WriteKeyNodeClipboardToString(ViewModel.KeyNode, ViewModel.KeyNodeTitle, writeIndented: false))));

            if (nodeInSelection)
                TryAddPasteKeyNodeStatus(entries);

            return entries;
        }

        //Ports the paste-side half of upstream's try/catch around Clipboard.GetText()/ReadKeyNodeClipboard
        //(reference §4b): a clipboard holding unrelated text throws on deserialize rather than returning
        //null, so this stays wrapped exactly like upstream's "Failed to apply clipboard node options" guard.
        private void TryAddPasteKeyNodeStatus(List<MenuEntry> entries) {
            string? clipboardText = Context.GetClipboardText?.Invoke();
            if (clipboardText is null)
                return;

            KeyNodeClipboardSaveData? keyNodeStatus;
            try {
                keyNodeStatus = GraphSaveCodec.ReadKeyNodeClipboard(clipboardText);
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Failed to apply clipboard node options");
                return;
            }
            if (keyNodeStatus is null)
                return;

            entries.Add(MenuEntry.Item("Paste key node status", () => {
                IEnumerable<BaseNodeElement> targets = Context.SelectedNodes is { Count: > 0 } selected && selected.Contains(this) ? selected : [this];
                foreach (BaseNodeElement target in targets) {
                    if (Context.Editor?.RequestNodeController(target.ViewModel.Id) is BaseNodeController controller) {
                        controller.SetKeyNode(keyNodeStatus.KeyNode);
                        controller.SetKeyNodeTitle(keyNodeStatus.Title);
                    }
                }
            }));
        }

        //Ports the "Auto-connect disconnected inputs/outputs" pair (reference §4b/§8): matchedIO/matchedOI
        //gate on the CURRENT selection as a whole, not on whether `this` is part of it (upstream has no
        //Contains(this) guard here, unlike the Delete/Flip "selected" variants above). Both directions ride
        //through Core's GraphAutoconnect scoped overloads (reference §11 step 11) rather than a hand-rolled
        //duplicate of the algorithm.
        private static void AddAutoconnectSelectionOptions(List<MenuEntry> entries, HashSet<BaseNodeElement> selected, NodeElementContext context) {
            var openInputs = new HashSet<ItemQualityPair>(selected.SelectMany(n => n.ViewModel.Inputs.Where(i => !n.ViewModel.InputLinks.Any(l => l.Item == i))));
            var openOutputs = new HashSet<ItemQualityPair>(selected.SelectMany(n => n.ViewModel.Outputs.Where(o => !n.ViewModel.OutputLinks.Any(l => l.Item == o))));
            var availableInputs = new HashSet<ItemQualityPair>(selected.SelectMany(n => n.ViewModel.Inputs));
            var availableOutputs = new HashSet<ItemQualityPair>(selected.SelectMany(n => n.ViewModel.Outputs));
            bool matchedIO = openInputs.Overlaps(availableOutputs);
            bool matchedOI = openOutputs.Overlaps(availableInputs);
            if (!matchedIO && !matchedOI)
                return;

            entries.Add(MenuEntry.Divider);
            if (matchedIO)
                entries.Add(MenuEntry.Item("Auto-connect disconnected inputs", () => context.AutoconnectSelectionInputs?.Invoke()));
            if (matchedOI)
                entries.Add(MenuEntry.Item("Auto-connect disconnected outputs", () => context.AutoconnectSelectionOutputs?.Invoke()));
        }

        protected virtual void AddRClickMenuOptions(List<MenuEntry> entries, bool nodeInSelection) { }
    }
}
