using Foreman.DataCaching;
using Foreman.Graph;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/SpoilNodeElement.cs in full - spoilage conversion node, single
    //dynamic output tab.
    public sealed class SpoilNodeElement : BaseNodeElement {
        protected override SKPaint CleanBgPaint => SpoilBgPaint;
        private static readonly SKPaint SpoilBgPaint = Fill(new SKColor(190, 217, 212));

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

            base.UpdateState();
        }

        protected override SKBitmap? NodeIcon() => IconCache.SpoilageIcon;

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            bool overproducing = SpoilViewModel.IsOverproducing();
            var textSlot = new Rectangle(trans.X - (Width / 2) + 40, trans.Y - (Height / 2) + (overproducing ? 32 : 27), Width - 10 - 40, Height - (overproducing ? 64 : 54));

            int textLength = Context.LevelOfDetail == LevelOfDetail.Low
                ? GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, InputName + " Spoilage", TextHorizontalAlign.Center, TextVerticalAlign.Center)
                : GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, CounterBaseFontSize, textSlot, GraphicsStuff.BuildingQuantityToText(SpoilViewModel.ActualSetValue, Context.RoundAssemblerCount) + " stacks", TextHorizontalAlign.Center, TextVerticalAlign.Center);

            SKBitmap icon = IconCache.SpoilageIcon ?? IconCache.UnknownIcon;
            int iconX = trans.X - Math.Min((Width / 2) - 10, (textLength / 2) + 32);
            canvas.DrawBitmap(icon, SKRect.Create(iconX, trans.Y - 16, 32, 32));
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {InputName} Spoilage.\nxN quantity lists number of slots required for throughput.\nRight click for options.", exclusive);
    }
}
