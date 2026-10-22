using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/PlantNodeElement.cs on the §01 card: recipe strip, plant glyph, output tabs set the width.
    public sealed class PlantNodeElement : BaseNodeElement {
        private IPlantNodeViewModel PlantViewModel => (IPlantNodeViewModel)ViewModel;
        private string InputName => PlantViewModel.Seed.FriendlyName ?? "";

        public PlantNodeElement(NodeElementContext context, IPlantNodeViewModel viewModel) : base(context, viewModel) {
            Width = MinWidth;
            Height = BaseSimpleHeight;

            UpdateState();
        }

        protected override void UpdateState() {
            foreach (ItemTabElement oldTab in OutputTabs.Where(tab => !PlantViewModel.Outputs.Contains(tab.Item)).ToList()) {
                OutputTabs.Remove(oldTab);
                SubElements.Remove(oldTab);
            }
            foreach (var item in PlantViewModel.Outputs)
                if (!OutputTabs.Any(tab => tab.Item == item))
                    OutputTabs.Add(new ItemTabElement(item, LinkType.Output, Context, this));

            Height = BaseSimpleHeight;
            base.UpdateState();
        }

        protected override int ComputeWidth() => WidthForTabs(TabSpan(OutputTabs));

        protected override string DisplayName => InputName + " Planting";
        protected override SKBitmap? TitleIcon => PlantViewModel.Seed.Icon;
        protected override SKColor StripColor(CanvasTokens tokens) => tokens.NodeRecipe;
        protected override bool Unavailable => IsUnavailable(PlantViewModel.Seed.Item);

        private protected override void LayoutDetail(CanvasTokens tokens, List<NodeText> detail) =>
            LayoutSubRow(tokens, detail, "Kit.Glyph.RadialPlant", "Plant · " + CountText(PlantViewModel.ActualSetValue), chip: false, value: true);

        protected override SKBitmap? NodeIcon() => IconCache.PlantingIcon;

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {InputName} Growth.\nxN quantity lists number of tiles required for throughput.\nRight click for options.", exclusive);
    }
}
