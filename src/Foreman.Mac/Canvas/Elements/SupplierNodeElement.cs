using Foreman;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/SupplierNodeElement.cs on the §01 card: "Source", or a "Fixed {rate}" chip.
    public sealed class SupplierNodeElement : BaseNodeElement {
        private ISupplierNodeViewModel SupplierViewModel => (ISupplierNodeViewModel)ViewModel;
        private string ItemName => SupplierViewModel.SuppliedItem.FriendlyName ?? "";

        public SupplierNodeElement(NodeElementContext context, ISupplierNodeViewModel viewModel) : base(context, viewModel) {
            Width = MinWidth;
            Height = BaseSimpleHeight;
        }

        protected override void UpdateState() {
            Height = BaseSimpleHeight;
            base.UpdateState();
        }

        protected override string DisplayName => ItemName;
        protected override SKBitmap? TitleIcon => SupplierViewModel.SuppliedItem.Icon;
        protected override SKColor StripColor(CanvasTokens tokens) => tokens.NodeSupplier;
        protected override bool Unavailable => IsUnavailable(SupplierViewModel.SuppliedItem.Item);

        private string? Chip => SupplierViewModel.RateType == RateType.Manual ? "Fixed " + RateText(SupplierViewModel.DesiredRate) : null;

        protected override int ComputeWidth() => WidthForChip(Chip);

        private protected override void LayoutDetail(CanvasTokens tokens, List<NodeText> detail) =>
            LayoutSubRow(tokens, detail, null, Chip ?? "Source", Chip is not null);

        protected override SKBitmap? NodeIcon() => SupplierViewModel.SuppliedItem.Icon;

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit quantity of {ItemName} produced.\nRight click for options.", exclusive);
    }
}
