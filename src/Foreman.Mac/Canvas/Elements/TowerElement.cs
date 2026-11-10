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
    public sealed class TowerElement : GraphElement {
        private IPlantNodeViewModel PlantViewModel => (IPlantNodeViewModel)parent.ViewModel;
        private readonly PlantNodeElement parent;
        private readonly NodeElementContext context;
        private SKRect icon = SKRect.Empty;

        public TowerElement(NodeElementContext context, PlantNodeElement parent) : base(parent) {
            this.context = context;
            this.parent = parent;
            Visible = false;
        }

        public void SetVisibility(bool visible) => Visible = visible;

        internal bool IsMissing => PlantViewModel.Tower is AssemblerQualityPair tower && (tower.Assembler.IsMissing || tower.Quality.IsMissing);

        internal float IconOpacity => IsMissing ? TokenBridge.Current.NodeUnavailableOpacity : 1f;

        internal SKRect IconRect {
            get {
                if (icon.IsEmpty)
                    return SKRect.Empty;
                Point centre = parent.LocalToGraph(new Point(0, 0));
                SKRect graph = icon;
                graph.Offset(centre.X, centre.Y);
                return graph;
            }
        }

        internal void Place(SKRect iconRect, SKRect? bonusRect) {
            icon = iconRect;
            if (iconRect.IsEmpty) {
                Width = 0;
                Height = 0;
                return;
            }
            SKRect span = bonusRect is SKRect bonus ? SKRect.Union(iconRect, bonus) : iconRect;
            Location = new Point((int)Math.Round(span.MidX), (int)Math.Round(span.MidY));
            Width = (int)Math.Ceiling(span.Width);
            Height = (int)Math.Ceiling(span.Height);
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if ((style != NodeDrawingStyle.Regular && style != NodeDrawingStyle.PrintStyle) || icon.IsEmpty || PlantViewModel.Tower is not AssemblerQualityPair tower)
                return;
            NodeCardPainter.DrawIcon(canvas, tower.Icon, IconRect, IconOpacity);
        }

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            if (!Visible || PlantViewModel.Tower is not AssemblerQualityPair tower)
                return [];

            string text = tower.FriendlyName;
            double bonus = IsMissing ? 0 : PlantViewModel.GetProductivityBonus();
            if (bonus > 0)
                text += "\nProductivity " + bonus.ToString("+0%", DisplayCulture.Format);
            if (PlantViewModel.TowerModules.Count > 0) {
                text += "\nTower Modules:";
                var moduleCounter = new Dictionary<ModuleQualityPair, int>();
                foreach (ModuleQualityPair m in PlantViewModel.TowerModules)
                    moduleCounter[m] = moduleCounter.TryGetValue(m, out int count) ? count + 1 : 1;
                foreach (ModuleQualityPair m in moduleCounter.Keys.OrderBy(m => m.Module.FriendlyName).ThenBy(m => m.Quality.Level).ThenBy(m => m.Quality.FriendlyName))
                    text += string.Create(DisplayCulture.Format, $"\n   {moduleCounter[m]} :{m.FriendlyName}");
            }

            Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point(0, -Height / 2)));
            return [new TooltipInfo(screen, Direction.Down, text)];
        }
    }
}
