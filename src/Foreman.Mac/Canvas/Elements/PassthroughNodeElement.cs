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
    //Ports ProductionGraphView/Elements/PassthroughNodeElement.cs: a §01 card ("Passthrough", or a "Limit {rate}" chip),
    //or with simple draw on the 72 × 96 bar, a line in the item's colour at the widest link's width.
    public sealed class PassthroughNodeElement : BaseNodeElement {
        private IPassthroughNodeViewModel PassthroughViewModel => (IPassthroughNodeViewModel)ViewModel;
        private string ItemName => PassthroughViewModel.PassthroughItem.FriendlyName ?? "";
        private bool isBar;

        public PassthroughNodeElement(NodeElementContext context, IPassthroughNodeViewModel viewModel) : base(context, viewModel) {
            Width = MinWidth;
            Height = BaseSimpleHeight;
        }

        protected override void UpdateState() {
            Height = BaseSimpleHeight;
            base.UpdateState();
        }

        private bool BarActive =>
            PassthroughViewModel.SimpleDraw
            && PassthroughViewModel.RateType == RateType.Auto
            && !PassthroughViewModel.KeyNode
            && !PassthroughViewModel.IsOverproducing()
            && !PassthroughViewModel.ManualRateNotMet()
            && PassthroughViewModel.InputLinks.Count > 0
            && PassthroughViewModel.OutputLinks.Count > 0;

        protected override bool HasCard => !isBar;
        protected override bool SimpleDraw => PassthroughViewModel.SimpleDraw;

        protected override int ComputeWidth() {
            isBar = BarActive;
            InputTabs[0].HideItemTab = isBar;
            OutputTabs[0].HideItemTab = isBar;
            return isBar ? PassthroughNodeWidth : WidthForChip(Chip);
        }

        protected override string DisplayName => ItemName;
        protected override SKBitmap? TitleIcon => PassthroughViewModel.PassthroughItem.Icon;
        protected override SKColor StripColor(CanvasTokens tokens) => tokens.NodePassthrough;

        private string? Chip => PassthroughViewModel.RateType == RateType.Manual ? "Limit " + RateText(PassthroughViewModel.DesiredRate) : null;

        private protected override void LayoutDetail(CanvasTokens tokens, List<NodeText> detail) =>
            LayoutSubRow(tokens, detail, null, Chip ?? "Passthrough", Chip is not null);

        protected override SKBitmap? NodeIcon() => null;

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (!isBar || style == NodeDrawingStyle.IconsOnly) {
                base.Draw(canvas, style);
                return;
            }

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

            //The caps sit on the tab points, where the hidden tabs are centred.
            SKPaint capFillPaint = GraphicsStuff.RentFillPaint(lineColor);
            int capRadius = TokenBridge.ScaledInt(6);
            canvas.DrawCircle(inputPoint.X, InputTabs[0].LocalToGraph(new Point(0, 0)).Y, capRadius, capFillPaint);
            canvas.DrawCircle(inputPoint.X, OutputTabs[0].LocalToGraph(new Point(0, 0)).Y, capRadius, capFillPaint);

            //maxLineWidth follows Context.GetLinkWidth, not UiScale, so its highlight padding stays unscaled.
            if (Highlighted) {
                SKPaint highlightStrokePaint = GraphicsStuff.RentStrokePaint(SelectionOverlayColor, Math.Max(30, maxLineWidth + 10), SKStrokeCap.Round);
                canvas.DrawLine(inputPoint.X, inputPoint.Y, outputPoint.X, outputPoint.Y, highlightStrokePaint);
            }
        }

        private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {ItemName}.\nRight click for options.", exclusive);
    }
}
