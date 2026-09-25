using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/ItemTabElement.cs on the §01 tab, plus the right-click delete-connection menu (reference §4d).
    public sealed class ItemTabElement : GraphElement {
        private enum TabStroke { Regular, Overproduced, Disconnected }

        public LinkType LinkType { get; }
        public ItemQualityPair Item { get; }
        public IEnumerable<INodeLinkViewModel> Links =>
            LinkType == LinkType.Input ? nodeViewModel.InputLinks.Where(l => l.Item == Item) : nodeViewModel.OutputLinks.Where(l => l.Item == Item);

        public bool HideItemTab { get; set; }

        private readonly NodeElementContext context;
        private readonly INodeViewModel nodeViewModel;
        private readonly LabelFade rate = new();

        private TabStroke stroke;
        private NodeText rateText;

        public ItemTabElement(ItemQualityPair item, LinkType type, NodeElementContext context, BaseNodeElement node) : base(node) {
            this.context = context;
            nodeViewModel = node.ViewModel;
            Item = item;
            LinkType = type;
            Layout();
            X = 0;
            Y = 0;
        }

        internal NodeText RateText => rateText;
        internal LabelState RateLabel => rate.State(context);

        public void UpdateValues(double recipeRate, double outputRate, bool isOverproduced) {
            stroke = isOverproduced ? TabStroke.Overproduced : !Links.Any() ? TabStroke.Disconnected : TabStroke.Regular;
            rate.Set(NodeCardPainter.Rate(recipeRate, context.RateUnit), context);
            Layout();
        }

        //22 tall, at least 44 wide: 3 leading · icon 16 · 4 · rate · 6 trailing; the tab grows and never trims the rate.
        private void Layout() {
            CanvasTokens tokens = TokenBridge.Current;
            int weight = tokens.CanvasEmphasizedWeight;
            float textWidth = NodeCardPainter.Measure(rate.Text, tokens.CanvasFontSize, weight);
            float textInset = tokens.NodeGapTight + tokens.NodeTabIcon + tokens.Space1;
            Width = (int)Math.Ceiling(Math.Max(tokens.NodeTabMinWidth, textInset + textWidth + tokens.Space15));
            Height = (int)Math.Round(tokens.NodeTabHeight);
            float textLeft = (-Width / 2f) + textInset;
            rateText = new NodeText(NodeTextRole.TabRate, rate.Text, new SKRect(textLeft, -Height / 2f, textLeft + textWidth, Height / 2f), tokens.CanvasFontSize, weight, tokens.Text);
        }

        public TabGeometry TabGeometry() {
            CanvasTokens tokens = TokenBridge.Current;
            Point centre = LocalToGraph(new Point(0, 0));
            SKColor strokeColor = stroke switch {
                TabStroke.Overproduced => tokens.Warning,
                TabStroke.Disconnected => tokens.Error,
                _ => tokens.NodeTabBorder,
            };
            return new TabGeometry(SKRect.Create(centre.X - (Width / 2f), centre.Y - (Height / 2f), Width, Height), tokens.NodeTabRadius, NodeCardPainter.TabStrokeWidth, strokeColor, tokens.WellBackground, tokens.NodeTabOverhang);
        }

        public Point GetConnectionPoint() =>
            (LinkType == LinkType.Input && nodeViewModel.NodeDirection == NodeDirection.Up) || (LinkType == LinkType.Output && nodeViewModel.NodeDirection == NodeDirection.Down)
                ? LocalToGraph(new Point(0, Height / 2))
                : LocalToGraph(new Point(0, -Height / 2));

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (style == NodeDrawingStyle.IconsOnly || HideItemTab)
                return;

            TabGeometry geometry = TabGeometry();
            NodeCardPainter.DrawRoundedBox(canvas, geometry.Rect, geometry.Radius, geometry.Fill, geometry.StrokeWidth, geometry.Stroke);

            if (style != NodeDrawingStyle.Regular && style != NodeDrawingStyle.PrintStyle)
                return;

            CanvasTokens tokens = TokenBridge.Current;
            float iconLeft = geometry.Rect.Left + tokens.NodeGapTight;
            NodeCardPainter.DrawIcon(canvas, Item.Icon ?? IconCache.UnknownIcon, SKRect.Create(iconLeft, geometry.Rect.MidY - (tokens.NodeTabIcon / 2), tokens.NodeTabIcon, tokens.NodeTabIcon));
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
