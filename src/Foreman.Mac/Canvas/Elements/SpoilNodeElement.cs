using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
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

            Width = MinWidth;
            Height = BaseSimpleHeight;

            base.UpdateState();
        }

        protected override SKBitmap? NodeIcon() => IconCache.SpoilageIcon;

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            bool overproducing = SpoilViewModel.IsOverproducing();
            int textLeftInset = TokenBridge.ScaledInt(40);
            int textRightInset = TokenBridge.ScaledInt(10);
            var textSlot = new Rectangle(trans.X - (Width / 2) + textLeftInset, trans.Y - (Height / 2) + TokenBridge.ScaledInt(overproducing ? 32 : 27), Width - textRightInset - textLeftInset, Height - TokenBridge.ScaledInt(overproducing ? 64 : 54));

            int textLength = Context.LevelOfDetail == LevelOfDetail.Low
                ? GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, InputName + " Spoilage", TextHorizontalAlign.Center, TextVerticalAlign.Center)
                : GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, CounterBaseFontSize, textSlot, GraphicsStuff.BuildingQuantityToText(SpoilViewModel.ActualSetValue, Context.RoundAssemblerCount) + " stacks", TextHorizontalAlign.Center, TextVerticalAlign.Center);

            int iconSize = TokenBridge.ScaledInt(32);
            SKBitmap icon = IconCache.SpoilageIcon ?? IconCache.UnknownIcon;
            int iconX = trans.X - Math.Min((Width / 2) - textRightInset, (textLength / 2) + iconSize);
            canvas.DrawBitmap(icon, SKRect.Create(iconX, trans.Y - TokenBridge.ScaledInt(16), iconSize, iconSize));
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) =>
            ExclusiveHelpTooltip($"Left click on this node to edit the throughput of {InputName} Spoilage.\nxN quantity lists number of slots required for throughput.\nRight click for options.", exclusive);
    }
}
