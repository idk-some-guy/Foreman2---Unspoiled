using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/PlantNodeElement.cs in full - planting/growth node, multiple dynamic
    //output tabs.
    public sealed class PlantNodeElement : BaseNodeElement {
        internal TowerElement TowerElement { get; }
        private IPlantNodeViewModel PlantViewModel => (IPlantNodeViewModel)ViewModel;
        private string InputName => PlantViewModel.Seed.FriendlyName ?? "";

        public PlantNodeElement(NodeElementContext context, IPlantNodeViewModel viewModel) : base(context, viewModel) {
            TowerElement = new TowerElement(context, this);
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

        private bool ShowsTower => PlantViewModel.Tower is not null && Context.LevelOfDetail != LevelOfDetail.Low;

        private string DetailText => "Plant · " + CountText(PlantViewModel.ActualSetValue);

        private double TowerBonus => TowerElement.IsMissing ? 0 : PlantViewModel.GetProductivityBonus();

        private static string BonusText(double bonus) => bonus.ToString("+0%", DisplayCulture.Format);

        protected override int ComputeWidth() {
            int tabs = WidthForTabs(TabSpan(OutputTabs));
            if (PlantViewModel.Tower is null)
                return tabs;

            CanvasTokens tokens = TokenBridge.Current;
            float content = tokens.GlyphMicro + tokens.Space15 + NodeCardPainter.Measure(DetailText, tokens.CanvasFontSize, 400);
            if (ShowsTower) {
                content += tokens.Space2 + tokens.NodeTabIcon;
                if (TowerBonus > 0)
                    content += tokens.Space1 + NodeCardPainter.Measure(BonusText(TowerBonus), tokens.CanvasFontSize, tokens.CanvasEmphasizedWeight);
            }
            float badge = BadgeWidth(tokens);
            return Math.Max(tabs, WidthForTabs(content + (badge > 0 ? tokens.Space15 + badge : 0)));
        }

        protected override string DisplayName => InputName + " Planting";
        protected override SKBitmap? TitleIcon => PlantViewModel.Seed.Icon;
        protected override SKColor StripColor(CanvasTokens tokens) => tokens.NodeRecipe;
        protected override bool Unavailable => IsUnavailable(PlantViewModel.Seed.Item);

        private protected override string? AutomationBuilding() {
            if (PlantViewModel.Tower is not AssemblerQualityPair tower)
                return null;
            return TowerBonus > 0 ? $"{tower.FriendlyName}, productivity {BonusText(TowerBonus)}" : tower.FriendlyName;
        }

        private protected override void LayoutDetail(CanvasTokens tokens, List<NodeText> detail) {
            LayoutSubRow(tokens, detail, "Kit.Glyph.RadialPlant", DetailText, chip: false, value: true);
            LayoutTower(tokens, detail);
        }

        private void LayoutTower(CanvasTokens tokens, List<NodeText> detail) {
            TowerElement.SetVisibility(false);
            TowerElement.Place(SKRect.Empty, null);
            if (!ShowsTower)
                return;

            NodeText plant = detail.Last(t => t.Role == NodeTextRole.Detail);
            float limit = plant.Rect.Right;
            float x = plant.Rect.Left + NodeCardPainter.Measure(plant.Text, plant.Size, plant.Weight) + tokens.Space2;
            float icon = tokens.NodeTabIcon;
            var iconRect = SKRect.Create(x, plant.Rect.Top + ((plant.Rect.Height - icon) / 2), icon, icon);
            if (iconRect.Right > limit + 0.01f)
                return;

            SKRect? bonusRect = null;
            if (TowerBonus > 0) {
                string text = BonusText(TowerBonus);
                int weight = tokens.CanvasEmphasizedWeight;
                float left = iconRect.Right + tokens.Space1;
                SKRect rect = new(left, plant.Rect.Top, left + NodeCardPainter.Measure(text, tokens.CanvasFontSize, weight), plant.Rect.Bottom);
                if (rect.Right <= limit + 0.01f) {
                    bonusRect = rect;
                    detail.Add(new NodeText(NodeTextRole.ExtraProductivity, text, rect, tokens.CanvasFontSize, weight, tokens.TextSecondary));
                }
            }
            TowerElement.Place(iconRect, bonusRect);
            TowerElement.SetVisibility(true);
        }

        protected override SKBitmap? NodeIcon() => IconCache.PlantingIcon;

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {InputName} Growth.\nxN quantity lists number of tiles required for throughput.\nRight click for options.", exclusive);
    }
}
