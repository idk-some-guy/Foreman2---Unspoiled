using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
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
            canvas.DrawCircle(inputPoint.X, capY1, 6, capFillPaint);
            canvas.DrawCircle(inputPoint.X, capY2, 6, capFillPaint);

            if (Highlighted) {
                SKPaint highlightStrokePaint = GraphicsStuff.RentStrokePaint(SelectionOverlayColor, Math.Max(30, maxLineWidth + 10), SKStrokeCap.Round);
                canvas.DrawLine(inputPoint.X, inputPoint.Y, outputPoint.X, outputPoint.Y, highlightStrokePaint);
            }
        }

        private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            if (PassthroughViewModel.RateType != RateType.Manual)
                return;

            int yoffset = PassthroughViewModel.NodeDirection == NodeDirection.Up ? 28 : 32;
            var titleSlot = new Rectangle(trans.X - (Width / 2) + 5, trans.Y - (Height / 2) + yoffset, Width - 10, 18);
            var textSlot = new Rectangle(titleSlot.X, titleSlot.Y + 18, titleSlot.Width, 20);

            GraphicsStuff.DrawText(canvas, TextColor, BoldTypeface, TitleFontSize, titleSlot, "-Limit-", TextHorizontalAlign.Center, TextVerticalAlign.Near);
            GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, GraphicsStuff.DoubleToString(PassthroughViewModel.DesiredRate), TextHorizontalAlign.Center, TextVerticalAlign.Near);
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {ItemName}.\nRight click for options.", exclusive);
    }
}
