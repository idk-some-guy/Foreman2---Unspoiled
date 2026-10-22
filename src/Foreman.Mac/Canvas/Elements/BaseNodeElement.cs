using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.KitSkia;
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

        //Footprints are graph space, multiplied by the display factor; saved layouts keep their spacing.
        protected static int BaseSimpleHeight => TokenBridge.ScaledInt(96);
        protected static int BaseRecipeHeight => TokenBridge.ScaledInt(144);
        protected static int WidthD => TokenBridge.ScaledInt(24);
        protected static int PassthroughNodeWidth => WidthD * 3;
        protected static int MinWidth => WidthD * 6;

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
            ViewModel.NodeValuesChanged += (_, _) => {
                nodeValuesRequireUpdate = true;
                Context.LabelValuesChanged?.Invoke();
            };

            InputTabs = [];
            OutputTabs = [];

            ErrorNotice = new ErrorNoticeElement(context, this);
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
            LayoutNode();
        }

        protected virtual void UpdateValues() {
            bool shortOfSupply = FlowUndersupplied;
            foreach (ItemTabElement tab in InputTabs)
                tab.UpdateValues(ViewModel.GetConsumeRate(tab.Item), 0, false, shortOfSupply);
            foreach (ItemTabElement tab in OutputTabs)
                tab.UpdateValues(ViewModel.GetSupplyRate(tab.Item), ViewModel.GetSupplyUsedRate(tab.Item), ViewModel.IsOverproducing(tab.Item), false);
            LayoutNode();
        }

        //---- card layout (§01), in local coordinates around the node centre ----

        private CanvasTokens? layoutTokens;
        private LevelOfDetail layoutDetail;
        private LayoutInputs layoutInputs;

        //Rate type, key status, direction, simple draw, count rounding, the badge, the solve state and icon view change without a node event until the next solve, so the card compares them each paint.
        private readonly record struct LayoutInputs(RateType RateType, bool KeyNode, string KeyNodeTitle, double DesiredRate, double DesiredSetValue, NodeDirection Direction, ProductionGraph.RateUnit RateUnit, bool SimpleDraw, bool RoundCount, NodeBadgeKind? Badge, NodeSolveState SolveState, bool IconsOnly);

        private LayoutInputs CurrentInputs() =>
            new(ViewModel.RateType, ViewModel.KeyNode, ViewModel.KeyNodeTitle, ViewModel.DesiredRate, ViewModel.DesiredSetValue, ViewModel.NodeDirection, Context.RateUnit, SimpleDraw, Context.RoundAssemblerCount, BadgeKind(), SolveState, Context.IconsOnly);

        protected virtual bool SimpleDraw => false;
        private IReadOnlyList<NodeText> texts = [];
        private SKRect titleIconRect;
        private SKRect titleRect;
        private SKRect keyGlyphRect;
        private SKRect chipRect;
        private (string Key, SKRect Box)? detailGlyph;
        private bool nameTruncated;

        internal IReadOnlyList<NodeText> Texts => texts;
        protected CanvasTokens Tokens => layoutTokens ?? TokenBridge.Current;

        protected abstract string DisplayName { get; }
        protected abstract SKBitmap? TitleIcon { get; }
        protected abstract SKColor StripColor(CanvasTokens tokens);
        protected virtual bool HasCard => true;
        protected virtual int ComputeWidth() => MinWidth;
        private protected abstract void LayoutDetail(CanvasTokens tokens, List<NodeText> detail);

        public SKColor TypeStripColor => StripColor(Tokens);
        public float NameFontSize => Tokens.CalloutFontSize;
        public float DetailFontSize => Tokens.CanvasFontSize;
        internal int NameFontWeight => Tokens.CanvasEmphasizedWeight;

        protected float Left => -Width / 2f;
        protected float Top => -Height / 2f;
        protected float Right => Width / 2f;

        private void LayoutNode() {
            CanvasTokens tokens = TokenBridge.Current;
            layoutTokens = tokens;
            layoutDetail = Context.LevelOfDetail;
            layoutInputs = CurrentInputs();
            pendingBadge = !layoutInputs.IconsOnly && layoutInputs.Badge is NodeBadgeKind kind ? new NodeBadge(kind, BadgeWords[(int)kind]) : null;
            Width = ComputeWidth();
            UpdateTabOrder();

            var laidOut = new List<NodeText>();
            chipRect = SKRect.Empty;
            detailGlyph = null;
            badgeRect = SKRect.Empty;
            if (HasCard) {
                LayoutTitle(tokens, laidOut);
                LayoutDetail(tokens, laidOut);
            }
            texts = laidOut;

            ErrorNotice.Place(LocalBadgeHitRect());
            ErrorNotice.SetVisibility(!badgeRect.IsEmpty && ViewModel.State is NodeState.Error or NodeState.Warning);
        }

        //---- state (§02): badge, solve state, availability ----

        private static readonly string[] BadgeWords = ["Unavailable", "Error", "Short", "Warning", "Over"];
        private NodeBadge? pendingBadge;
        private SKRect badgeRect;

        public bool ShowsKeyboardFocus { get; set; }
        public bool IsFindMatch { get; set; }

        //Stands in for the Core solve state until the solver reports it per node.
        internal NodeSolveState? StaleOverride { get; set; }
        internal NodeSolveState SolveState => StaleOverride ?? NodeSolveState.Solved;

        protected virtual bool Unavailable => false;

        protected static bool IsUnavailable(IItem? item) => item is { IsMissing: false } && (!item.Available || !item.Enabled);

        protected static bool IsUnavailable(IRecipe? recipe) => recipe is { IsMissing: false } && (!recipe.Available || !recipe.Enabled);

        public float ContentOpacity => Unavailable ? Tokens.NodeUnavailableOpacity : 1f;

        private protected NodeBadgeKind? BadgeKind() {
            if (Unavailable)
                return NodeBadgeKind.Unavailable;
            if (ViewModel.State == NodeState.Error)
                return NodeBadgeKind.Error;
            if (Context.FlagOUSuppliedNodes && FlowUndersupplied)
                return NodeBadgeKind.Undersupplied;
            if (ViewModel.State == NodeState.Warning)
                return NodeBadgeKind.Warning;
            if (Context.FlagOUSuppliedNodes && ViewModel.IsOverproducing())
                return NodeBadgeKind.Over;
            return null;
        }

        public NodeBadge? Badge() => badgeRect.IsEmpty ? null : pendingBadge;

        internal SKRect BadgeRect() => badgeRect.IsEmpty ? SKRect.Empty : ToGraph(badgeRect);

        public SKRect? BadgeHitRect() => badgeRect.IsEmpty ? null : ToGraph(LocalBadgeHitRect());

        private SKRect LocalBadgeHitRect() {
            if (badgeRect.IsEmpty)
                return SKRect.Empty;
            float pad = Tokens.Space05;
            SKRect hit = SKRect.Inflate(badgeRect, pad, pad);
            hit.Intersect(SKRect.Create(Left, Top, Width, Height));
            return hit;
        }

        private protected float BadgeWidth(CanvasTokens tokens) {
            if (pendingBadge is not NodeBadge badge)
                return 0;
            float glyph = badge.Kind == NodeBadgeKind.Unavailable ? 0 : tokens.Glyph10 + tokens.NodeGapTight;
            return (2 * tokens.Space15) + glyph + NodeCardPainter.Measure(badge.Text, tokens.CanvasFontSize, tokens.CanvasEmphasizedWeight);
        }

        private protected float PlaceBadge(CanvasTokens tokens, float left, float rowTop, float rowHeight) {
            float width = BadgeWidth(tokens);
            if (width == 0)
                return left;
            badgeRect = SKRect.Create(left, rowTop + ((rowHeight - tokens.NodeBadgeHeight) / 2), width, tokens.NodeBadgeHeight);
            return badgeRect.Right;
        }

        private protected float PlaceBadgeTrailing(CanvasTokens tokens, float rowTop, float rowHeight) {
            float right = Right - tokens.NodeInsetSide;
            float width = BadgeWidth(tokens);
            if (width == 0)
                return right;
            PlaceBadge(tokens, right - width, rowTop, rowHeight);
            return badgeRect.Left - tokens.Space15;
        }

        private protected string CountText(double count) =>
            SolveState == NodeSolveState.NeverSolved ? "—" : "×" + NodeCardPainter.Count(count, Context.RoundAssemblerCount);

        private protected int WidthForSubRow(float content) {
            CanvasTokens tokens = TokenBridge.Current;
            float badge = BadgeWidth(tokens);
            return badge == 0 ? MinWidth : WidthForTabs(content + tokens.Space15 + badge);
        }

        private protected NodeText ValueText(NodeTextRole role, string text, SKRect rect, float size, int weight, SKColor color) =>
            SolveState == NodeSolveState.Stale ? new NodeText(role, text, rect, size, weight, Tokens.TextSecondary, Italic: true) : new NodeText(role, text, rect, size, weight, color);

        private void LayoutTitle(CanvasTokens tokens, List<NodeText> laidOut) {
            float s = (float)tokens.Scale;
            float rowTop = Top + tokens.NodeInsetTop;
            titleIconRect = SKRect.Create(Left + tokens.NodeInsetSide, rowTop, tokens.NodeIcon, tokens.NodeIcon);
            float textLeft = titleIconRect.Right + tokens.Space2;
            titleRect = new SKRect(textLeft, rowTop, Right - tokens.NodeInsetSide, rowTop + tokens.NodeIcon);
            float width = titleRect.Width;
            int weight = tokens.CanvasEmphasizedWeight;

            SKRect nameRect = titleRect;
            keyGlyphRect = SKRect.Empty;
            if (ViewModel.KeyNode) {
                float keyLine = NodeCardPainter.KeyLineHeight * s;
                float glyph = tokens.Glyph10;
                keyGlyphRect = SKRect.Create(textLeft, rowTop + ((keyLine - glyph) / 2), glyph, glyph);
                float keyTextLeft = keyGlyphRect.Right + tokens.NodeGapTight;
                string keyTitle = NodeCardPainter.Fit(ViewModel.KeyNodeTitle ?? "", titleRect.Right - keyTextLeft, tokens.CanvasFontSize, weight, out _);
                laidOut.Add(new NodeText(NodeTextRole.KeyTitle, keyTitle, new SKRect(keyTextLeft, rowTop, titleRect.Right, rowTop + keyLine), tokens.CanvasFontSize, weight, tokens.AccentText));
                nameRect = new SKRect(textLeft, rowTop + keyLine, titleRect.Right, titleRect.Bottom);
            }

            string name = NodeCardPainter.Fit(DisplayName, width, tokens.CalloutFontSize, weight, out nameTruncated);
            laidOut.Add(new NodeText(NodeTextRole.Name, name, nameRect, tokens.CalloutFontSize, weight, tokens.Text));
        }

        //The sub row of every non-recipe card: a word, or a chip once the rate is fixed.
        private protected void LayoutSubRow(CanvasTokens tokens, List<NodeText> laidOut, string? glyphKey, string text, bool chip, bool value = false) {
            float s = (float)tokens.Scale;
            float rowTop = Top + NodeCardPainter.DetailRowTop(tokens);
            float rowHeight = NodeCardPainter.SubRowHeight * s;
            float x = Left + tokens.NodeInsetSide;
            float right = PlaceBadgeTrailing(tokens, rowTop, rowHeight);
            if (glyphKey is not null) {
                float glyph = tokens.GlyphMicro;
                detailGlyph = (glyphKey, SKRect.Create(x, rowTop + ((rowHeight - glyph) / 2), glyph, glyph));
                x += glyph + tokens.Space15;
            }
            if (chip)
                LayoutChip(tokens, laidOut, text, x, rowTop, rowHeight);
            else if (value)
                laidOut.Add(ValueText(NodeTextRole.Detail, NodeCardPainter.Fit(text, right - x, tokens.CanvasFontSize, 400, out _),
                    new SKRect(x, rowTop, right, rowTop + rowHeight), tokens.CanvasFontSize, 400, tokens.TextSecondary));
            else
                laidOut.Add(new NodeText(NodeTextRole.Detail, NodeCardPainter.Fit(text, right - x, tokens.CanvasFontSize, 400, out _),
                    new SKRect(x, rowTop, right, rowTop + rowHeight), tokens.CanvasFontSize, 400, tokens.TextSecondary));
        }

        private protected float LayoutChip(CanvasTokens tokens, List<NodeText> laidOut, string text, float x, float rowTop, float rowHeight) {
            int weight = tokens.CanvasEmphasizedWeight;
            float textWidth = NodeCardPainter.Measure(text, tokens.CanvasFontSize, weight);
            float pad = tokens.Space15;
            chipRect = SKRect.Create(x, rowTop + ((rowHeight - tokens.NodeBadgeHeight) / 2), pad + textWidth + pad, tokens.NodeBadgeHeight);
            laidOut.Add(new NodeText(NodeTextRole.Chip, text, SKRect.Create(x + pad, chipRect.Top, textWidth, chipRect.Height), tokens.CanvasFontSize, weight, tokens.Text));
            return chipRect.Right;
        }

        protected string RateText(double rate) => NodeCardPainter.Rate(rate, Context.RateUnit);

        //Tabs are centred along their edge with a 6 gap, overhanging it by NodeTabOverhang.
        protected static float TabSpan(List<ItemTabElement> tabs) {
            if (tabs.Count == 0)
                return 0;
            float span = TokenBridge.Current.Space15 * (tabs.Count - 1);
            foreach (ItemTabElement tab in tabs)
                span += tab.Width;
            return span;
        }

        protected static int WidthForTabs(float span) => Math.Max(MinWidth, (int)Math.Ceiling(span + (2 * TokenBridge.Current.NodeInsetSide)));

        //A fixed-rate chip wider than the inner width grows the card.
        protected static int WidthForChip(string? chip) {
            if (chip is null)
                return MinWidth;
            CanvasTokens tokens = TokenBridge.Current;
            float chipWidth = (2 * tokens.Space15) + NodeCardPainter.Measure(chip, tokens.CanvasFontSize, tokens.CanvasEmphasizedWeight);
            return WidthForTabs(chipWidth);
        }

        private void UpdateTabOrder() {
            InputTabs = [.. InputTabs.OrderBy(GetItemTabXHeuristic).ThenBy(it => it.Item.Item?.Name).ThenBy(it => it.Item.Quality?.Level).ThenBy(it => it.Item.Quality?.Name)];
            OutputTabs = [.. OutputTabs.OrderBy(GetItemTabXHeuristic).ThenBy(it => it.Item.Item?.Name).ThenBy(it => it.Item.Quality?.Level).ThenBy(it => it.Item.Quality?.Name)];

            bool up = ViewModel.NodeDirection == NodeDirection.Up;
            PlaceTabs(OutputTabs, up);
            PlaceTabs(InputTabs, !up);
        }

        private void PlaceTabs(List<ItemTabElement> tabs, bool topEdge) {
            CanvasTokens tokens = TokenBridge.Current;
            float gap = tokens.Space15;
            float centreY = topEdge ? Top - tokens.NodeTabOverhang + (tokens.NodeTabHeight / 2) : -Top + tokens.NodeTabOverhang - (tokens.NodeTabHeight / 2);
            float x = -TabSpan(tabs) / 2;
            foreach (ItemTabElement tab in tabs) {
                tab.Location = new Point((int)Math.Round(x + (tab.Width / 2f)), (int)Math.Round(centreY));
                x += tab.Width + gap;
            }
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
            if (!ReferenceEquals(layoutTokens, TokenBridge.Current) || layoutDetail != Context.LevelOfDetail || layoutInputs != CurrentInputs())
                nodeStateRequiresUpdate = true;
            if (nodeStateRequiresUpdate)
                UpdateState();
            if (nodeStateRequiresUpdate || nodeValuesRequireUpdate)
                UpdateValues();
            nodeStateRequiresUpdate = false;
            nodeValuesRequireUpdate = false;
        }

        private SKRect ToGraph(SKRect local) {
            Point centre = LocalToGraph(new Point(0, 0));
            local.Offset(centre.X, centre.Y);
            return local;
        }

        private bool FlowUndersupplied => ViewModel.ManualRateNotMet() && this is not SupplierNodeElement;

        //---- the three ring lanes (§02): border ±1, lane A +2…+4, lane B +6…+9; radii and strokes stay fixed ----

        private const float InnerRingInset = 3, LaneACentre = 3, LaneBCentre = 7.5f, FlowStroke = 2, IconRingRadius = 8;

        private SKRect GraphRect() => ToGraph(SKRect.Create(Left, Top, Width, Height));

        private (SKRect Rect, float Radius) RingBase() {
            if (!Context.IconsOnly)
                return (GraphRect(), Tokens.NodeRadius);
            float half = Context.IconsDrawSize / 2f;
            return (ToGraph(new SKRect(-half, -half, half, half)), IconRingRadius);
        }

        private bool BorderIsStateMark => Highlighted || FlowUndersupplied || ViewModel.State == NodeState.Error || ViewModel.IsOverproducing();

        //Selection owns the border lane; flow state still shows through the badge and the tab outlines.
        public RingGeometry BorderRing() {
            CanvasTokens tokens = Tokens;
            (SKRect bounds, float radius) = RingBase();
            if (Highlighted)
                return new RingGeometry(bounds, radius, FlowStroke, tokens.NodeSelected, null);
            float half = NodeCardPainter.CardStrokeWidth / 2;
            if (FlowUndersupplied || ViewModel.State == NodeState.Error)
                return new RingGeometry(SKRect.Inflate(bounds, -half, -half), radius - half, NodeCardPainter.CardStrokeWidth, tokens.Error, null);
            if (ViewModel.IsOverproducing())
                return new RingGeometry(bounds, radius, FlowStroke, tokens.Warning, NodeCardPainter.FlowDash);
            return new RingGeometry(SKRect.Inflate(bounds, -half, -half), radius - half, NodeCardPainter.CardStrokeWidth, tokens.IncreaseContrast ? tokens.NodeTabBorder : tokens.BorderStrong, null);
        }

        public RingGeometry? InnerRing() {
            if (Highlighted || !FlowUndersupplied)
                return null;
            (SKRect bounds, float radius) = RingBase();
            return new RingGeometry(SKRect.Inflate(bounds, -InnerRingInset, -InnerRingInset), radius - InnerRingInset, NodeCardPainter.CardStrokeWidth, Tokens.Error, null);
        }

        public RingGeometry? LaneARing() => IsPlacementFlashing ? PlacementFlashRing() : null;

        public RingGeometry? FocusRing() {
            if (!ShowsKeyboardFocus)
                return null;
            (SKRect bounds, float radius) = RingBase();
            return new RingGeometry(SKRect.Inflate(bounds, LaneBCentre, LaneBCentre), radius + LaneBCentre, Tokens.FocusRingWidth, Tokens.FocusRing, null);
        }

        internal bool IsPlacementFlashing { get; set; }
        internal double PlacementFlashOpacity { get; set; }

        internal RingGeometry PlacementFlashRing() {
            (SKRect bounds, float radius) = RingBase();
            return new RingGeometry(SKRect.Inflate(bounds, LaneACentre, LaneACentre), radius + LaneACentre, FlowStroke, Tokens.Accent, null);
        }

        public NodeCardGeometry CardGeometry() {
            CanvasTokens tokens = Tokens;
            RingGeometry border = BorderRing();
            float stroke = border.StrokeWidth > NodeCardPainter.CardStrokeWidth ? 0 : NodeCardPainter.CardStrokeWidth;
            return new NodeCardGeometry(GraphRect(), tokens.NodeRadius, tokens.ControlBackground, stroke, border.Color, tokens.CardShadowColor.Alpha > 0);
        }

        public SKRect TypeStripRect() {
            if (!HasCard)
                return SKRect.Empty;
            CanvasTokens tokens = Tokens;
            return ToGraph(SKRect.Create(Left, Top + tokens.NodeTypeStripInset, tokens.NodeTypeStrip, Height - (2 * tokens.NodeTypeStripInset)));
        }

        public SKRect TitleTextRect() => ToGraph(titleRect);

        internal SKRect DetailGlyphRect() => detailGlyph is (_, SKRect box) ? ToGraph(box) : SKRect.Empty;

        //The card and the tabs paint at ContentOpacity; the border lane stays opaque between them, and the badge and lanes A and B above.
        public override void Paint(SKCanvas canvas, NodeDrawingStyle style) {
            if (!Visible)
                return;
            int saved = canvas.SaveCount;
            float opacity = ContentOpacity;
            if (opacity < 1)
                NodeCardPainter.BeginOpacityLayer(canvas, opacity);
            Draw(canvas, style);
            canvas.RestoreToCount(saved);

            if (BorderIsStateMark)
                NodeCardPainter.DrawRing(canvas, BorderRing());
            if (InnerRing() is RingGeometry inner)
                NodeCardPainter.DrawRing(canvas, inner);

            if (opacity < 1)
                NodeCardPainter.BeginOpacityLayer(canvas, opacity);
            foreach (GraphElement element in SubElements)
                element.Paint(canvas, style);
            canvas.RestoreToCount(saved);
            DrawOuterMarks(canvas, style);
        }

        private void DrawOuterMarks(SKCanvas canvas, NodeDrawingStyle style) {
            if ((style == NodeDrawingStyle.Regular || style == NodeDrawingStyle.PrintStyle) && Badge() is NodeBadge badge)
                NodeCardPainter.DrawBadge(canvas, BadgeRect(), badge, Tokens);
            if (LaneARing() is RingGeometry laneA && PlacementFlashOpacity > 0)
                NodeCardPainter.DrawRing(canvas, laneA, (float)Math.Clamp(PlacementFlashOpacity, 0, 1));
            if (FocusRing() is RingGeometry focus)
                NodeCardPainter.DrawRing(canvas, focus);
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            Point trans = LocalToGraph(new Point(0, 0));
            if (style == NodeDrawingStyle.IconsOnly) {
                if (NodeIcon() is SKBitmap nodeIcon)
                    canvas.DrawBitmap(nodeIcon, SKRect.Create(trans.X - (Context.IconsDrawSize / 2), trans.Y - (Context.IconsDrawSize / 2), Context.IconsDrawSize, Context.IconsDrawSize));
                return;
            }

            CanvasTokens tokens = Tokens;
            NodeCardPainter.DrawCard(canvas, CardGeometry(), tokens);
            NodeCardPainter.DrawStrip(canvas, TypeStripRect(), TypeStripColor, tokens.NodeTypeStripRadius);

            if (style == NodeDrawingStyle.Regular || style == NodeDrawingStyle.PrintStyle)
                DetailsDraw(canvas, trans);
        }

        protected virtual void DetailsDraw(SKCanvas canvas, Point trans) {
            CanvasTokens tokens = Tokens;
            SKRect icon = titleIconRect;
            icon.Offset(trans.X, trans.Y);
            NodeCardPainter.DrawIcon(canvas, TitleIcon, icon);
            if (!keyGlyphRect.IsEmpty) {
                SKRect key = keyGlyphRect;
                key.Offset(trans.X, trans.Y);
                NodeCardPainter.DrawGlyph(canvas, "Kit.Glyph.Node.Key", key, tokens.AccentText, tokens.NodeBadgeStroke);
            }
            if (detailGlyph is (string glyphKey, SKRect glyphBox)) {
                glyphBox.Offset(trans.X, trans.Y);
                NodeCardPainter.DrawGlyph(canvas, glyphKey, glyphBox, tokens.TextSecondary);
            }
            if (!chipRect.IsEmpty) {
                SKRect chip = chipRect;
                chip.Offset(trans.X, trans.Y);
                NodeCardPainter.DrawChip(canvas, chip, tokens);
            }
            foreach (NodeText text in texts)
                DrawNodeText(canvas, text, trans);
        }

        private protected virtual void DrawNodeText(SKCanvas canvas, NodeText text, Point trans) => NodeCardPainter.DrawText(canvas, text, trans.X, trans.Y);

        protected abstract SKBitmap? NodeIcon();

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            GraphElement? element = SubElements.FirstOrDefault(it => it.ContainsPoint(graphPoint));
            List<TooltipInfo>? subTooltips = element?.GetToolTips(graphPoint);
            List<TooltipInfo> myTooltips = GetMyToolTips(graphPoint, subTooltips is null || subTooltips.Count == 0);

            if (subTooltips is not null)
                myTooltips.AddRange(subTooltips);
            if (nameTruncated && TitleTextRect().Contains(graphPoint.X, graphPoint.Y)) {
                SKRect title = TitleTextRect();
                myTooltips.Insert(0, new TooltipInfo(Context.Viewport.GraphToScreen(new Point((int)title.MidX, (int)title.Bottom)), Direction.Up, DisplayName));
            }

            return myTooltips;
        }

        protected abstract List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive);

        protected static List<TooltipInfo> ExclusiveHelpTooltip(string text, bool exclusive) =>
            exclusive ? [new TooltipInfo(new Avalonia.Point(10, 10), Direction.None, text)] : [];

        //Ports MouseDown's location-snapshot half (reference §2) - claiming graphViewer.MouseDownElement is
        //GraphCanvasControl's job, inlined at the pointer-press site rather than the constructor.
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

        internal GraphElement HoverTargetAt(Point graphPoint) => SubElements.FirstOrDefault(element => element.ContainsPoint(graphPoint)) ?? this;

        //Whether a tab or the error notice claims this point ahead of the node's own body.
        public bool HasSubElementAt(Point graphPoint) =>
            SubElements.OfType<ItemTabElement>().Any(tab => tab.ContainsPoint(graphPoint)) || ErrorNotice.ContainsPoint(graphPoint);

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
