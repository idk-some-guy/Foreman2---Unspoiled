using Foreman.DataCaching;
using Foreman.Graph;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/PlantNodeElement.cs in full - planting/growth node, multiple dynamic
    //output tabs.
    public sealed class PlantNodeElement : BaseNodeElement {
        protected override SKPaint CleanBgPaint => PlantBgPaint;
        private static readonly SKPaint PlantBgPaint = Fill(new SKColor(190, 217, 212));

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

            Width = Math.Max(MinWidth, GetIconWidths(OutputTabs) + 10);
            if (Width % WidthD != 0) {
                Width += WidthD;
                Width -= Width % WidthD;
            }

            base.UpdateState();
        }

        protected override SKBitmap? NodeIcon() => IconCache.PlantingIcon;

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            bool overproducing = PlantViewModel.IsOverproducing();
            var textSlot = new Rectangle(trans.X - (Width / 2) + 40, trans.Y - (Height / 2) + (overproducing ? 32 : 27), Width - 10 - 40, Height - (overproducing ? 64 : 54));

            int textLength = Context.LevelOfDetail == LevelOfDetail.Low
                ? GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, InputName + " Planting", TextHorizontalAlign.Center, TextVerticalAlign.Center)
                : GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, CounterBaseFontSize, textSlot, GraphicsStuff.BuildingQuantityToText(PlantViewModel.ActualSetValue, Context.RoundAssemblerCount) + " tiles", TextHorizontalAlign.Center, TextVerticalAlign.Center);

            SKBitmap icon = IconCache.PlantingIcon ?? IconCache.UnknownIcon;
            int iconX = trans.X - Math.Min((Width / 2) - 10, (textLength / 2) + 32);
            canvas.DrawBitmap(icon, SKRect.Create(iconX, trans.Y - 16, 32, 32));
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {InputName} Growth.\nxN quantity lists number of tiles required for throughput.\nRight click for options.", exclusive);
    }
}
