using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Models;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/ItemTabElement.cs: draw/tooltip path plus the right-click delete-
    //connection menu (reference §4d).
    public sealed class ItemTabElement : GraphElement {
        public static int TabWidth => IconSize + (Border * 3);
        public static int TabBorder => Border;

        public LinkType LinkType { get; }
        public ItemQualityPair Item { get; }
        public IEnumerable<INodeLinkViewModel> Links =>
            LinkType == LinkType.Input ? nodeViewModel.InputLinks.Where(l => l.Item == Item) : nodeViewModel.OutputLinks.Where(l => l.Item == Item);

        public bool HideItemTab { get; set; }

        private const int IconSize = 32;
        private const int Border = 3;

        //Matches upstream's DrawString anchor point, which reduces algebraically to Bounds.Top + 2 (output)
        //or Bounds.Bottom - 2 (input) regardless of text height - see the Draw() comment below.
        private const int TextEdgeInset = 2;

        private static readonly SKTypeface TextTypeface = SKTypeface.Default;
        private const float TextFontSize = 6f;

        private static readonly SKPaint DirectionPaint = new() { Color = new SKColor(0, 0, 0, 40), Style = SKPaintStyle.Fill, IsAntialias = true };
        private static readonly SKPaint RegularBorderPaint = StrokePaint(new SKColor(105, 105, 105));
        private static readonly SKPaint OverproducedBorderPaint = StrokePaint(new SKColor(184, 134, 11));
        private static readonly SKPaint DisconnectedBorderPaint = StrokePaint(new SKColor(139, 0, 0));
        private static readonly SKPaint FillPaint = new() { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };

        private static SKPaint StrokePaint(SKColor color) => new() { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = Border, IsAntialias = true };

        private readonly NodeElementContext context;
        private readonly INodeViewModel nodeViewModel;

        private SKPaint borderPaint;
        private string text = "";

        public ItemTabElement(ItemQualityPair item, LinkType type, NodeElementContext context, BaseNodeElement node) : base(node) {
            this.context = context;
            nodeViewModel = node.ViewModel;
            Item = item;
            LinkType = type;

            borderPaint = RegularBorderPaint;
            using var measureFont = new SKFont(TextTypeface, TextFontSize);
            int measuredTextHeight = (int)(measureFont.Metrics.Descent - measureFont.Metrics.Ascent);
            Width = TabWidth;
            Height = IconSize + measuredTextHeight + Border + 3;
            X = 0;
            Y = 0;
        }

        public Point GetConnectionPoint() =>
            (LinkType == LinkType.Input && nodeViewModel.NodeDirection == NodeDirection.Up) || (LinkType == LinkType.Output && nodeViewModel.NodeDirection == NodeDirection.Down)
                ? LocalToGraph(new Point(0, Height / 2))
                : LocalToGraph(new Point(0, -Height / 2));

        public void UpdateValues(double recipeRate, double outputRate, bool isOverproduced) {
            borderPaint = RegularBorderPaint;
            text = GraphicsStuff.DoubleToString(recipeRate);
            int textHeight = 10;
            if (isOverproduced) {
                borderPaint = OverproducedBorderPaint;
                text = GraphicsStuff.DoubleToString(outputRate) + "\n" + text;
                textHeight += 10;
            } else if (!Links.Any())
                borderPaint = DisconnectedBorderPaint;

            Height = IconSize + textHeight + Border + 3;
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (style == NodeDrawingStyle.IconsOnly || HideItemTab)
                return;

            Point trans = LocalToGraph(new Point(0, 0));
            int halfW = Bounds.Width / 2;
            int halfH = Bounds.Height / 2;

            GraphicsStuff.FillRoundRect(canvas, trans.X - halfW, trans.Y - halfH, Bounds.Width, Bounds.Height, Border, FillPaint);

            if (context.DynamicLinkWidth || !context.ArrowsOnLinks) {
                using var path = new SKPath();
                if (nodeViewModel.NodeDirection == NodeDirection.Up) {
                    path.MoveTo(trans.X - halfW, trans.Y + halfH);
                    path.LineTo(trans.X + halfW, trans.Y + halfH);
                    path.LineTo(trans.X, trans.Y - halfH);
                } else {
                    path.MoveTo(trans.X - halfW, trans.Y - halfH);
                    path.LineTo(trans.X + halfW, trans.Y - halfH);
                    path.LineTo(trans.X, trans.Y + halfH);
                }
                path.Close();
                canvas.DrawPath(path, DirectionPaint);
            }

            GraphicsStuff.DrawRoundRect(canvas, trans.X - halfW, trans.Y - halfH, Bounds.Width, Bounds.Height, Border, borderPaint);

            if (style != NodeDrawingStyle.Regular && style != NodeDrawingStyle.PrintStyle)
                return;

            //Upstream anchors the text via a point + StringFormat.LineAlignment (Near for output/top-format,
            //Far for input/bottom-format) rather than a box; its point formula reduces to a fixed 2px inset
            //from the tab's own top/bottom edge regardless of text height. The border stroke centers on
            //that same edge (StrokeWidth = Border = 3, so it bleeds ~1.5px inward) - text anchored flush at
            //the edge collides with it, so both slots below carry the same TextEdgeInset and the input slot
            //now uses the matching Far alignment instead of a Near approximation.
            if (LinkType == LinkType.Output) {
                var textSlot = new Rectangle(trans.X - halfW, trans.Y - halfH + TextEdgeInset, Bounds.Width, Bounds.Height - IconSize - Border - TextEdgeInset);
                GraphicsStuff.DrawText(canvas, SKColors.Black, TextTypeface, TextFontSize, textSlot, text, TextHorizontalAlign.Center, TextVerticalAlign.Near);
                DrawIcon(canvas, trans.X - halfW + (int)(Border * 1.5), trans.Y + halfH - Border - IconSize);
            } else {
                var textSlot = new Rectangle(trans.X - halfW, trans.Y - halfH + IconSize + Border, Bounds.Width, Bounds.Height - IconSize - Border - TextEdgeInset);
                GraphicsStuff.DrawText(canvas, SKColors.Black, TextTypeface, TextFontSize, textSlot, text, TextHorizontalAlign.Center, TextVerticalAlign.Far);
                DrawIcon(canvas, trans.X - halfW + (int)(Border * 1.5), trans.Y - halfH + Border);
            }
        }

        private void DrawIcon(SKCanvas canvas, int x, int y) {
            SKBitmap icon = Item.Icon ?? IconCache.UnknownIcon;
            canvas.DrawBitmap(icon, SKRect.Create(x, y, IconSize, IconSize));
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
