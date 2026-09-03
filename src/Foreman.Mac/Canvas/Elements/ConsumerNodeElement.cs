using Foreman;
using Foreman.Graph;
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

        protected override SKBitmap? NodeIcon() => ConsumerViewModel.ConsumedItem.Icon;

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            int yoffset = ConsumerViewModel.NodeDirection == NodeDirection.Up ? 5 : 28;
            var titleSlot = new Rectangle(trans.X - (Width / 2) + 5, trans.Y - (Height / 2) + yoffset, Width - 10, 20);
            var textSlot = new Rectangle(titleSlot.X, titleSlot.Y + 20, titleSlot.Width, (Height / 2) - 5);

            GraphicsStuff.DrawText(canvas, TextColor, BoldTypeface, TitleFontSize, titleSlot, ConsumerViewModel.RateType == RateType.Auto ? "Infinite Sink:" : "Required Output:", TextHorizontalAlign.Center, TextVerticalAlign.Near);
            GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, ItemName, TextHorizontalAlign.Center, TextVerticalAlign.Near);
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit quantity of {ItemName} required.\nRight click for options.", exclusive);
    }
}
