using Foreman;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/ConsumerNodeElement.cs in full - "Infinite Sink:" / "Required Output:" node.
    public sealed class ConsumerNodeElement : BaseNodeElement {
        protected override SKPaint CleanBgPaint => ConsumerBgPaint;
        private static readonly SKPaint ConsumerBgPaint = Fill(new SKColor(249, 237, 195));

        private IConsumerNodeViewModel ConsumerViewModel => (IConsumerNodeViewModel)ViewModel;
        private string ItemName => ConsumerViewModel.ConsumedItem.FriendlyName ?? "";

        public ConsumerNodeElement(NodeElementContext context, IConsumerNodeViewModel viewModel) : base(context, viewModel) {
            Width = MinWidth;
            Height = BaseSimpleHeight;
        }

        //No tab-driven width here (unlike RecipeNodeElement), just the fixed-size re-derive a scale change needs.
        protected override void UpdateState() {
            Width = MinWidth;
            Height = BaseSimpleHeight;
            base.UpdateState();
        }

        protected override SKBitmap? NodeIcon() => ConsumerViewModel.ConsumedItem.Icon;

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            int edgeInset = TokenBridge.ScaledInt(5);
            int titleHeight = TokenBridge.ScaledInt(20);
            int yoffset = ConsumerViewModel.NodeDirection == NodeDirection.Up ? edgeInset : TokenBridge.ScaledInt(28);
            var titleSlot = new Rectangle(trans.X - (Width / 2) + edgeInset, trans.Y - (Height / 2) + yoffset, Width - (edgeInset * 2), titleHeight);
            var textSlot = new Rectangle(titleSlot.X, titleSlot.Y + titleHeight, titleSlot.Width, (Height / 2) - edgeInset);

            GraphicsStuff.DrawText(canvas, TextColor, BoldTypeface, TitleFontSize, titleSlot, ConsumerViewModel.RateType == RateType.Auto ? "Infinite Sink:" : "Required Output:", TextHorizontalAlign.Center, TextVerticalAlign.Near);
            GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, ItemName, TextHorizontalAlign.Center, TextVerticalAlign.Near);
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit quantity of {ItemName} required.\nRight click for options.", exclusive);
    }
}
