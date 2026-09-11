using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/PassthroughNodeElement.cs draw path, including the simple-draw
    //line-only mode. The simple-draw toggle right-click menu (AddRClickMenuOptions) is P4 and stays out.
    //
    //Upstream sizes the simple-draw line from the connected LinkElements' live LinkWidth; this reads
    //Context.GetLinkWidth, which GraphViewer wires to the real LinkElementDictionary lookup.
    public sealed class PassthroughNodeElement : BaseNodeElement {
        protected override SKPaint CleanBgPaint => PassthroughBgPaint;
        private static readonly SKPaint PassthroughBgPaint = Fill(new SKColor(200, 200, 200));

        private IPassthroughNodeViewModel PassthroughViewModel => (IPassthroughNodeViewModel)ViewModel;
        private string ItemName => PassthroughViewModel.PassthroughItem.FriendlyName ?? "";

        public PassthroughNodeElement(NodeElementContext context, IPassthroughNodeViewModel viewModel) : base(context, viewModel) {
            Width = PassthroughNodeWidth;
            Height = BaseSimpleHeight;
        }

        //No tab-driven width here (unlike RecipeNodeElement), just the fixed-size re-derive a scale change needs.
        protected override void UpdateState() {
            Width = PassthroughNodeWidth;
            Height = BaseSimpleHeight;
            base.UpdateState();
        }

        protected override SKBitmap? NodeIcon() => null;

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            bool simpleDrawActive = style != NodeDrawingStyle.IconsOnly
                && PassthroughViewModel.SimpleDraw
                && PassthroughViewModel.RateType == RateType.Auto
                && !PassthroughViewModel.KeyNode
                && !PassthroughViewModel.IsOverproducing()
                && !PassthroughViewModel.ManualRateNotMet()
                && PassthroughViewModel.InputLinks.Count > 0
                && PassthroughViewModel.OutputLinks.Count > 0;

            if (!simpleDrawActive) {
                InputTabs[0].HideItemTab = false;
                OutputTabs[0].HideItemTab = false;
                base.Draw(canvas, style);
                return;
            }

            InputTabs[0].HideItemTab = true;
            OutputTabs[0].HideItemTab = true;

            float maxLineWidth = PassthroughViewModel.InputLinks.Concat(PassthroughViewModel.OutputLinks).Select(l => Context.GetLinkWidth(l.Id)).Max();
            Point inputPoint = InputTabs[0].GetConnectionPoint();
            Point outputPoint = OutputTabs[0].GetConnectionPoint();
            if (PassthroughViewModel.PassthroughItem.Item is not IItem passthroughItem)
                return;

            SKColor lineColor = ToSkColor(passthroughItem.AverageColor);
            SKPaint lineStrokePaint = GraphicsStuff.RentStrokePaint(lineColor, maxLineWidth, SKStrokeCap.Round);
            canvas.DrawLine(inputPoint.X, inputPoint.Y, outputPoint.X, outputPoint.Y, lineStrokePaint);

            if (style != NodeDrawingStyle.Regular)
                return;

            SKPaint capFillPaint = GraphicsStuff.RentFillPaint(lineColor);
            float capY1 = Math.Min(outputPoint.Y, inputPoint.Y) + (ItemTabElement.TabWidth / 2f);
            float capY2 = Math.Max(outputPoint.Y, inputPoint.Y) - (ItemTabElement.TabWidth / 2f);
            int capRadius = TokenBridge.ScaledInt(6);
            canvas.DrawCircle(inputPoint.X, capY1, capRadius, capFillPaint);
            canvas.DrawCircle(inputPoint.X, capY2, capRadius, capFillPaint);

            //maxLineWidth follows Context.GetLinkWidth, not UiScale, so its highlight padding stays unscaled.
            if (Highlighted) {
                SKPaint highlightStrokePaint = GraphicsStuff.RentStrokePaint(SelectionOverlayColor, Math.Max(30, maxLineWidth + 10), SKStrokeCap.Round);
                canvas.DrawLine(inputPoint.X, inputPoint.Y, outputPoint.X, outputPoint.Y, highlightStrokePaint);
            }
        }

        private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            if (PassthroughViewModel.RateType != RateType.Manual)
                return;

            int edgeInset = TokenBridge.ScaledInt(5);
            int titleHeight = TokenBridge.ScaledInt(18);
            int textHeight = TokenBridge.ScaledInt(20);
            int yoffset = PassthroughViewModel.NodeDirection == NodeDirection.Up ? TokenBridge.ScaledInt(28) : TokenBridge.ScaledInt(32);
            var titleSlot = new Rectangle(trans.X - (Width / 2) + edgeInset, trans.Y - (Height / 2) + yoffset, Width - (edgeInset * 2), titleHeight);
            var textSlot = new Rectangle(titleSlot.X, titleSlot.Y + titleHeight, titleSlot.Width, textHeight);

            GraphicsStuff.DrawText(canvas, TextColor, BoldTypeface, TitleFontSize, titleSlot, "-Limit-", TextHorizontalAlign.Center, TextVerticalAlign.Near);
            GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, GraphicsStuff.DoubleToString(PassthroughViewModel.DesiredRate), TextHorizontalAlign.Center, TextVerticalAlign.Near);
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {ItemName}.\nRight click for options.", exclusive);
    }
}
