using Foreman;
using Foreman.Graph;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/SupplierNodeElement.cs in full - "Infinite Source:" / "Exact Input:" node.
    public sealed class SupplierNodeElement : BaseNodeElement {
        protected override SKPaint CleanBgPaint => SupplierBgPaint;
        private static readonly SKPaint SupplierBgPaint = Fill(new SKColor(231, 214, 224));

        private ISupplierNodeViewModel SupplierViewModel => (ISupplierNodeViewModel)ViewModel;
        private string ItemName => SupplierViewModel.SuppliedItem.FriendlyName ?? "";

        public SupplierNodeElement(NodeElementContext context, ISupplierNodeViewModel viewModel) : base(context, viewModel) {
            Width = MinWidth;
            Height = BaseSimpleHeight;
        }

        protected override SKBitmap? NodeIcon() => SupplierViewModel.SuppliedItem.Icon;

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            int yoffset = SupplierViewModel.NodeDirection == NodeDirection.Up ? 32 : 5;
            var titleSlot = new Rectangle(trans.X - (Width / 2) + 5, trans.Y - (Height / 2) + yoffset, Width - 10, 20);
            var textSlot = new Rectangle(titleSlot.X, titleSlot.Y + 20, titleSlot.Width, (Height / 2) - 5);

            GraphicsStuff.DrawText(canvas, TextColor, BoldTypeface, TitleFontSize, titleSlot, SupplierViewModel.RateType == RateType.Auto ? "Infinite Source:" : "Exact Input:", TextHorizontalAlign.Center, TextVerticalAlign.Near);
            GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, ItemName, TextHorizontalAlign.Center, TextVerticalAlign.Near);
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit quantity of {ItemName} produced.\nRight click for options.", exclusive);
    }
}
