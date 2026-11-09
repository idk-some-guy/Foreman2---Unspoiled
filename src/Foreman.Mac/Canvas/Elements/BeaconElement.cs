using Foreman;
using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    internal sealed class BeaconElement : GraphElement {
        private IRecipeNodeViewModel RecipeViewModel => (IRecipeNodeViewModel)parent.ViewModel;
        private readonly RecipeNodeElement parent;
        private readonly NodeElementContext context;

        public BeaconElement(NodeElementContext context, RecipeNodeElement parent) : base(parent) {
            this.context = context;
            this.parent = parent;
        }

        public void SetVisibility(bool visible) => Visible = visible;

        internal void Place(IEnumerable<StatusItem> items) {
            if (items.FirstOrDefault() is not { Kind: StatusItemKind.Beacon } badge) {
                Width = 0;
                Height = 0;
                return;
            }
            Location = new Point((int)Math.Round(badge.Rect.MidX), (int)Math.Round(badge.Rect.MidY));
            Width = (int)Math.Round(badge.Rect.Width);
            Height = (int)Math.Round(badge.Rect.Height);
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (Width == 0 || (style != NodeDrawingStyle.Regular && style != NodeDrawingStyle.PrintStyle))
                return;

            CanvasTokens tokens = TokenBridge.Current;
            SKRect badge = BadgeRect();
            NodeCardPainter.DrawRoundedBox(canvas, badge, tokens.NodeBeaconBadgeRadius, tokens.WellBackground, NodeCardPainter.CardStrokeWidth, tokens.BorderStrong);
            SKRect box = GlyphRect();
            NodeCardPainter.DrawGlyph(canvas, "Kit.Glyph.Beacon", box, tokens.TextSecondary);
            NodeCardPainter.FillGlyph(canvas, "Kit.Glyph.Beacon.Fill", box, tokens.TextSecondary);
        }

        private SKRect BadgeRect() {
            Point centre = LocalToGraph(new Point(0, 0));
            return SKRect.Create(centre.X - (Width / 2f), centre.Y - (Height / 2f), Width, Height);
        }

        internal SKRect GlyphRect() {
            SKRect badge = BadgeRect();
            float glyph = TokenBridge.Current.GlyphMicro;
            return SKRect.Create(badge.MidX - (glyph / 2), badge.MidY - (glyph / 2), glyph, glyph);
        }

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            if (!Visible || Width == 0 || !RecipeViewModel.SelectedBeacon)
                return [];

            string text = RecipeViewModel.SelectedBeacon.FriendlyName ?? "";
            if (RecipeViewModel.BeaconModules.Count > 0) {
                var moduleCounter = new Dictionary<ModuleQualityPair, int>();
                foreach (ModuleQualityPair m in RecipeViewModel.BeaconModules)
                    moduleCounter[m] = moduleCounter.TryGetValue(m, out int count) ? count + 1 : 1;
                text += "\nBeacon Modules:";
                foreach (ModuleQualityPair m in moduleCounter.Keys.OrderBy(m => m.Module.FriendlyName).ThenBy(m => m.Quality.Level).ThenBy(m => m.Quality.FriendlyName))
                    text += string.Create(DisplayCulture.Format, $"\n   {moduleCounter[m]} :{m.FriendlyName}");
            }

            Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point(0, Height / 2)));
            return [new TooltipInfo(screen, Direction.Up, text)];
        }
    }
}
