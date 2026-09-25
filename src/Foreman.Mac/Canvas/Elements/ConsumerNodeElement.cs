using Foreman;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/ConsumerNodeElement.cs on the §01 card: "Output", or a "Fixed {rate}" chip.
    public sealed class ConsumerNodeElement : BaseNodeElement {
        private IConsumerNodeViewModel ConsumerViewModel => (IConsumerNodeViewModel)ViewModel;
        private string ItemName => ConsumerViewModel.ConsumedItem.FriendlyName ?? "";

        public ConsumerNodeElement(NodeElementContext context, IConsumerNodeViewModel viewModel) : base(context, viewModel) {
            Width = MinWidth;
            Height = BaseSimpleHeight;
        }

        protected override void UpdateState() {
            Height = BaseSimpleHeight;
            base.UpdateState();
        }

        protected override string DisplayName => ItemName;
        protected override SKBitmap? TitleIcon => ConsumerViewModel.ConsumedItem.Icon;
        protected override SKColor StripColor(CanvasTokens tokens) => tokens.NodeConsumer;

        private string? Chip => ConsumerViewModel.RateType == RateType.Manual ? "Fixed " + RateText(ConsumerViewModel.DesiredRate) : null;

        protected override int ComputeWidth() => WidthForChip(Chip);

        private protected override void LayoutDetail(CanvasTokens tokens, List<NodeText> detail) =>
            LayoutSubRow(tokens, detail, null, Chip ?? "Output", Chip is not null);

        protected override SKBitmap? NodeIcon() => ConsumerViewModel.ConsumedItem.Icon;

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit quantity of {ItemName} required.\nRight click for options.", exclusive);
    }
}
