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
    public sealed class AssemblerElement : GraphElement {
        private IRecipeNodeViewModel RecipeViewModel => (IRecipeNodeViewModel)parent.ViewModel;
        private readonly RecipeNodeElement parent;
        private readonly NodeElementContext context;
        private List<SKRect> pips = [];

        public AssemblerElement(NodeElementContext context, RecipeNodeElement parent) : base(parent) {
            this.context = context;
            this.parent = parent;
        }

        public void SetVisibility(bool visible) => Visible = visible;

        internal void Place(IEnumerable<StatusItem> items) {
            List<StatusItem> placed = [.. items];
            if (placed.Count == 0) {
                pips = [];
                Width = 0;
                Height = 0;
                return;
            }
            SKRect span = placed.Select(item => item.Rect).Aggregate(SKRect.Union);
            Location = new Point((int)Math.Round(span.MidX), (int)Math.Round(span.MidY));
            Width = (int)Math.Ceiling(span.Width);
            Height = (int)Math.Ceiling(span.Height);
            pips = [.. placed.Where(item => item.Kind == StatusItemKind.Pip).Select(item => item.Rect)];
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (style != NodeDrawingStyle.Regular && style != NodeDrawingStyle.PrintStyle)
                return;
            Point centre = parent.LocalToGraph(new Point(0, 0));
            SKColor color = TokenBridge.Current.TextSecondary;
            foreach (SKRect pip in pips) {
                SKRect drawn = pip;
                drawn.Offset(centre.X, centre.Y);
                NodeCardPainter.DrawPip(canvas, drawn, color);
            }
        }

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            if (!Visible || RecipeViewModel.AssemblerModules.Count == 0)
                return [];

            string text = "Assembler Modules:";
            var moduleCounter = new Dictionary<ModuleQualityPair, int>();
            foreach (ModuleQualityPair m in RecipeViewModel.AssemblerModules)
                moduleCounter[m] = moduleCounter.TryGetValue(m, out int count) ? count + 1 : 1;
            foreach (ModuleQualityPair m in moduleCounter.Keys.OrderBy(m => m.Module.FriendlyName).ThenBy(m => m.Quality.Level).ThenBy(m => m.Quality.FriendlyName))
                text += string.Create(DisplayCulture.Format, $"\n   {moduleCounter[m]} :{m.FriendlyName}");

            Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point(0, -Height / 2)));
            return [new TooltipInfo(screen, Direction.Down, text)];
        }
    }
}
