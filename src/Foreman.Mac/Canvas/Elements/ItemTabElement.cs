using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/ItemTabElement.cs on the §01 tab, plus the right-click delete-connection menu (reference §4d).
    public sealed class ItemTabElement : GraphElement {
        private enum TabStroke { Regular, Overproduced, Short }

        public LinkType LinkType { get; }
        public ItemQualityPair Item { get; }
        public IEnumerable<INodeLinkViewModel> Links =>
            LinkType == LinkType.Input ? nodeViewModel.InputLinks.Where(l => l.Item == Item) : nodeViewModel.OutputLinks.Where(l => l.Item == Item);

        public bool HideItemTab { get; set; }

        private readonly NodeElementContext context;
        private readonly BaseNodeElement node;
        private readonly INodeViewModel nodeViewModel;
        private readonly LabelFade rate = new();

        private TabStroke stroke;
        private NodeText rateText;
        private bool showsRate = true;
        private double rateValue;

        public ItemTabElement(ItemQualityPair item, LinkType type, NodeElementContext context, BaseNodeElement node) : base(node) {
            this.context = context;
            this.node = node;
            nodeViewModel = node.ViewModel;
            Item = item;
            LinkType = type;
            Layout();
            X = 0;
            Y = 0;
        }

        internal NodeText RateText => rateText;
        internal bool DrawsRate => showsRate;
        internal LabelState RateLabel => rate.State(context);

        public string AutomationName {
            get {
                string lead = $"{Item.FriendlyName ?? ""}, {(LinkType == LinkType.Output ? "output" : "input")}";
                if (node.SolveState == NodeSolveState.NeverSolved)
                    return lead + ", not yet solved";
                string text = $"{lead}, {NodeCardPainter.Number(rateValue)} per {NodeCardPainter.UnitWord(context.RateUnit)}";
                return stroke switch {
                    TabStroke.Overproduced => text + ", overproducing",
                    TabStroke.Short => text + ", short of supply",
                    _ => text,
                };
            }
        }

        public void UpdateValues(double recipeRate, double outputRate, bool isOverproduced, bool isShort) {
            rateValue = recipeRate;
            stroke = isOverproduced ? TabStroke.Overproduced : isShort ? TabStroke.Short : TabStroke.Regular;
            rate.Set(node.SolveState == NodeSolveState.NeverSolved ? "—" : NodeCardPainter.Rate(recipeRate, context.RateUnit), context);
            Layout();
        }

        //22 tall, at least 44 wide: 3 leading · icon 16 · 4 · rate · 6 trailing; the tab grows and never trims the rate.
        private void Layout() {
            CanvasTokens tokens = TokenBridge.Current;
            int weight = tokens.CanvasEmphasizedWeight;
            showsRate = context.IsTextLegible(tokens.CanvasFontSize);
            float textWidth = NodeCardPainter.Measure(rate.Text, tokens.CanvasFontSize, weight);
            float textInset = tokens.NodeGapTight + tokens.NodeTabIcon + tokens.Space1;
            Width = (int)Math.Ceiling(Math.Max(tokens.NodeTabMinWidth, textInset + textWidth + tokens.Space15));
            Height = (int)Math.Round(tokens.NodeTabHeight);
            float textLeft = (-Width / 2f) + textInset;
            var rect = new SKRect(textLeft, -Height / 2f, textLeft + textWidth, Height / 2f);
            rateText = node.SolveState == NodeSolveState.Stale
                ? new NodeText(NodeTextRole.TabRate, rate.Text, rect, tokens.CanvasFontSize, weight, tokens.TextSecondary, Italic: true)
                : new NodeText(NodeTextRole.TabRate, rate.Text, rect, tokens.CanvasFontSize, weight, stroke == TabStroke.Short ? tokens.Error : tokens.Text);
        }

        public TabGeometry TabGeometry() {
            CanvasTokens tokens = TokenBridge.Current;
            Point centre = LocalToGraph(new Point(0, 0));
            float width = showsRate ? Width : Height;
            SKRect rect = SKRect.Create(centre.X - (width / 2f), centre.Y - (Height / 2f), width, Height);
            if (!context.IsExporting && ReferenceEquals(context.ArmedTab, this))
                return new TabGeometry(rect, tokens.NodeTabRadius, ArmedStrokeWidth, tokens.Accent, tokens.AccentSoft, tokens.NodeTabOverhang);
            SKColor strokeColor = stroke switch {
                TabStroke.Overproduced => tokens.Warning,
                TabStroke.Short => tokens.Error,
                _ => tokens.NodeTabBorder,
            };
            SKColor fill = !context.IsExporting && ReferenceEquals(context.HoveredTab, this) ? tokens.ControlBackgroundHover : tokens.WellBackground;
            float[]? dash = stroke == TabStroke.Overproduced ? NodeCardPainter.FlowDash : null;
            return new TabGeometry(rect, tokens.NodeTabRadius, NodeCardPainter.TabStrokeWidth, strokeColor, fill, tokens.NodeTabOverhang, dash);
        }

        private const float ArmedStrokeWidth = 2;

        public Point GetConnectionPoint() =>
            (LinkType == LinkType.Input && nodeViewModel.NodeDirection == NodeDirection.Up) || (LinkType == LinkType.Output && nodeViewModel.NodeDirection == NodeDirection.Down)
                ? LocalToGraph(new Point(0, Height / 2))
                : LocalToGraph(new Point(0, -Height / 2));

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if ((style != NodeDrawingStyle.Regular && style != NodeDrawingStyle.PrintStyle) || HideItemTab)
                return;

            TabGeometry geometry = TabGeometry();
            NodeCardPainter.DrawRoundedBox(canvas, geometry.Rect, geometry.Radius, geometry.Fill, geometry.StrokeWidth, geometry.Stroke, geometry.Dash);

            CanvasTokens tokens = TokenBridge.Current;
            float iconLeft = showsRate ? geometry.Rect.Left + tokens.NodeGapTight : geometry.Rect.MidX - (tokens.NodeTabIcon / 2);
            NodeCardPainter.DrawIcon(canvas, Item.Icon ?? IconCache.UnknownIcon, SKRect.Create(iconLeft, geometry.Rect.MidY - (tokens.NodeTabIcon / 2), tokens.NodeTabIcon, tokens.NodeTabIcon));
            if (!showsRate)
                return;
            Point centre = LocalToGraph(new Point(0, 0));
            NodeCardPainter.DrawLabel(canvas, rateText, centre.X, centre.Y, rate.State(context));
        }

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            if (Parent is not BaseNodeElement)
                return [];

            //RecipeNodeViewModel ingredient/product naming and LinkChecker temperature-range resolution are
            //deferred (RecipeNodeElement and its session-backed link resolution aren't ported yet); both fall
            //back to the plain item name until those land.
            string tooltipText = Item.FriendlyName ?? "";

            Direction direction = (LinkType == LinkType.Input && nodeViewModel.NodeDirection == NodeDirection.Up) || (LinkType == LinkType.Output && nodeViewModel.NodeDirection == NodeDirection.Down)
                ? Direction.Up
                : Direction.Down;

            return [
                new TooltipInfo(context.Viewport.GraphToScreen(GetConnectionPoint()), direction, tooltipText),
                new TooltipInfo(new Avalonia.Point(10, 10), Direction.None, "Drag to create a new connection.\nRight click for options.")
            ];
        }

        //Ports ItemTabElement.MouseUp's right-click body (reference §4d): a single "Delete connections" item
        //covering every link on this node for this item+direction, disabled when there are none.
        public List<MenuEntry> BuildRightClickMenu() {
            var connections = new List<LinkId>(Links.Select(l => l.Id));
            return [
                MenuEntry.Item("Delete connections", () => {
                    foreach (LinkId linkId in connections)
                        context.Editor?.DeleteLink(linkId);
                    context.Editor?.Graph.UpdateNodeValues();
                }, enabled: connections.Count > 0)
            ];
        }
    }
}
