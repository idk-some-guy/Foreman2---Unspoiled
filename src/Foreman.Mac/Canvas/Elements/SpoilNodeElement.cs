using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/SpoilNodeElement.cs on the §01 card: recipe strip, spoil glyph, one output tab.
    public sealed class SpoilNodeElement : BaseNodeElement {
        private ISpoilNodeViewModel SpoilViewModel => (ISpoilNodeViewModel)ViewModel;
        private string InputName => SpoilViewModel.InputItem.FriendlyName ?? "";

        public SpoilNodeElement(NodeElementContext context, ISpoilNodeViewModel viewModel) : base(context, viewModel) {
            Width = MinWidth;
            Height = BaseSimpleHeight;

            UpdateState();
        }

        protected override void UpdateState() {
            //we are guaranteed exactly one output; swap it if the spoil item's preset target changed under us.
            ItemTabElement oldTab = OutputTabs[0];
            if (oldTab.Item != SpoilViewModel.OutputItem) {
                OutputTabs.Clear();
                SubElements.Remove(oldTab);
                OutputTabs.Add(new ItemTabElement(SpoilViewModel.OutputItem, LinkType.Output, Context, this));
            }

            Height = BaseSimpleHeight;
            base.UpdateState();
        }

        protected override string DisplayName => InputName + " Spoilage";
        protected override SKBitmap? TitleIcon => SpoilViewModel.InputItem.Icon;
        protected override SKColor StripColor(CanvasTokens tokens) => tokens.NodeRecipe;

        private protected override void LayoutDetail(CanvasTokens tokens, List<NodeText> detail) =>
            LayoutSubRow(tokens, detail, "Kit.Glyph.RadialSpoil", "Spoil · ×" + NodeCardPainter.Count(SpoilViewModel.ActualSetValue, Context.RoundAssemblerCount), chip: false);

        protected override SKBitmap? NodeIcon() => IconCache.SpoilageIcon;

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {InputName} Spoilage.\nxN quantity lists number of slots required for throughput.\nRight click for options.", exclusive);
    }
}
