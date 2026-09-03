using Foreman;
using Foreman.Graph;
using Foreman.Models;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/BeaconElement.cs in full: beacon icon + module display (same 3
    //density tiers as AssemblerElement, smaller scale) + beacon count text.
    internal sealed class BeaconElement : GraphElement {
        private const int BeaconIconSize = 28;
        private const int ModuleIconSize = 12;
        private const int ModuleSpacing = 11;

        //0,0 here is the top-left corner, matching upstream's local layout convention for this element.
        private static readonly Point[] moduleLocations = [new(ModuleSpacing * 2, 0), new(ModuleSpacing * 2, ModuleSpacing), new(ModuleSpacing, 0), new(ModuleSpacing, ModuleSpacing), new(0, 0), new(0, ModuleSpacing)];
        private static readonly Point moduleOffset = new(10, 3);

        private static readonly SKPaint SpeedModulePaint = StrokePaint(new SKColor(0, 0, 139), 2);
        private static readonly SKPaint ProdModulePaint = StrokePaint(new SKColor(139, 0, 0), 2);
        private static readonly SKPaint EffModulePaint = StrokePaint(new SKColor(0, 100, 0), 2);
        private static readonly SKPaint QualityModulePaint = StrokePaint(new SKColor(255, 215, 0), 2);
        private static readonly SKPaint UnknownModulePaint = StrokePaint(SKColors.Black, 2);
        private static readonly SKTypeface ModuleTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        private const float ModuleFontSize = 5f;

        private static readonly SKTypeface CounterTypeface = SKTypeface.Default;
        private const float CounterFontSize = 8f;
        private static readonly SKColor TextColor = SKColors.Black;

        private static SKPaint StrokePaint(SKColor color, float width) => new() { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };

        private IRecipeNodeViewModel RecipeViewModel => (IRecipeNodeViewModel)parent.ViewModel;
        private readonly RecipeNodeElement parent;
        private readonly NodeElementContext context;

        internal ModuleDisplayTier ModuleTier => RecipeViewModel.BeaconModules.Count <= moduleLocations.Length ? ModuleDisplayTier.Icons
            : RecipeViewModel.BeaconModules.Count <= 8 * 4 ? ModuleDisplayTier.Dots
            : ModuleDisplayTier.Tally;

        public BeaconElement(NodeElementContext context, RecipeNodeElement parent) : base(parent) {
            this.context = context;
            this.parent = parent;

            Width = BeaconIconSize + (ModuleSpacing * 3) + 12;
            Height = BeaconIconSize;
        }

        public void SetVisibility(bool visible) => Visible = visible;

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (!RecipeViewModel.SelectedBeacon || style == NodeDrawingStyle.IconsOnly || style == NodeDrawingStyle.Simple)
                return;

            Point trans = LocalToGraph(new Point(-Width / 2, -Height / 2));

            if (RecipeViewModel.SelectedBeacon.Icon is SKBitmap beaconIcon)
                canvas.DrawBitmap(beaconIcon, SKRect.Create(trans.X + moduleOffset.X + ModuleSpacing * 3 + 2, trans.Y, BeaconIconSize, BeaconIconSize));

            if (RecipeViewModel.BeaconModules.Count <= 6) {
                for (int i = 0; i < moduleLocations.Length && i < RecipeViewModel.BeaconModules.Count; i++)
                    if (RecipeViewModel.BeaconModules[i].Icon is SKBitmap moduleIcon)
                        canvas.DrawBitmap(moduleIcon, SKRect.Create(trans.X + moduleLocations[i].X + moduleOffset.X, trans.Y + moduleLocations[i].Y + moduleOffset.Y, ModuleIconSize, ModuleIconSize));
            } else if (RecipeViewModel.BeaconModules.Count <= 8 * 4) { //8x4 dot grid, 32 modules max
                for (int x = 0; x < 8; x++) {
                    for (int y = 0; y < 4; y++) {
                        int moduleIndex = (x * 4) + y;
                        if (RecipeViewModel.BeaconModules.Count > moduleIndex) {
                            SKPaint marker = RecipeViewModel.BeaconModules[moduleIndex].Module.GetProductivityBonus() > 0 ? ProdModulePaint :
                                RecipeViewModel.BeaconModules[moduleIndex].Module.GetQualityBonus() > 0 ? QualityModulePaint :
                                RecipeViewModel.BeaconModules[moduleIndex].Module.GetConsumptionBonus() < 0 ? EffModulePaint :
                                RecipeViewModel.BeaconModules[moduleIndex].Module.GetSpeedBonus() > 0 ? SpeedModulePaint :
                                UnknownModulePaint;
                            canvas.DrawOval(SKRect.Create(trans.X + moduleOffset.X + (ModuleSpacing * 2) + ModuleIconSize - 5 - (x * 5), trans.Y + moduleOffset.Y + 2 + (y * 5), 2, 2), marker);
                        }
                    }
                }
            } else {
                int prodModules = RecipeViewModel.BeaconModules.Count(m => m.Module.GetProductivityBonus() > 0);
                int qualityModules = RecipeViewModel.BeaconModules.Count(m => m.Module.GetQualityBonus() > 0 && m.Module.GetProductivityBonus() <= 0);
                int efficiencyModules = RecipeViewModel.BeaconModules.Count(m => m.Module.GetConsumptionBonus() < 0 && m.Module.GetProductivityBonus() <= 0 && m.Module.GetQualityBonus() <= 0);
                int speedModules = RecipeViewModel.BeaconModules.Count(m => m.Module.GetSpeedBonus() > 0 && m.Module.GetConsumptionBonus() >= 0 && m.Module.GetProductivityBonus() <= 0 && m.Module.GetQualityBonus() <= 0);
                int unknownModules = RecipeViewModel.BeaconModules.Count - prodModules - efficiencyModules - speedModules - qualityModules;
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(0, 0, 139), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "S:{0}", speedModules), trans.X, trans.Y + 5);
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(0, 100, 0), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "E:{0}", efficiencyModules), trans.X, trans.Y + 15);
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(139, 0, 0), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "P:{0}", prodModules), trans.X + 22, trans.Y + 5);
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(255, 215, 0), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "Q:{0}", qualityModules), trans.X + 22, trans.Y + 15);
                GraphicsStuff.DrawStringAtPoint(canvas, SKColors.Black, ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "U:{0}", unknownModules), trans.X, trans.Y + 25);
            }

            if (RecipeViewModel.SelectedBeacon) {
                int parentHalfWidth = Parent is RecipeNodeElement recipeParent ? recipeParent.Width : Width;
                var textbox = new Rectangle(trans.X + Width, trans.Y + 5, (parentHalfWidth / 2) - X - (Width / 2) - 6, 18);

                double beaconCount = RecipeViewModel.GetTotalBeacons();
                string sbeaconCount = beaconCount >= 10000 ? beaconCount.ToString("0.##e0", DisplayCulture.Format) : beaconCount.ToString("0", DisplayCulture.Format);

                string text = context.LevelOfDetail == LevelOfDetail.Medium
                    ? string.Format(DisplayCulture.Format, "x {0}", RecipeViewModel.BeaconCount.ToString("0.##", DisplayCulture.Format))
                    : string.Format(DisplayCulture.Format, "x {0} Σ{1}", RecipeViewModel.BeaconCount.ToString("0.##", DisplayCulture.Format), sbeaconCount);
                GraphicsStuff.DrawText(canvas, TextColor, CounterTypeface, CounterFontSize, textbox, text, TextHorizontalAlign.Near, TextVerticalAlign.Near, singleLine: true);
            }
        }

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            if (!Visible)
                return [];
            if (!RecipeViewModel.SelectedBeacon)
                return [];

            var tooltips = new List<TooltipInfo>();

            Point localPoint = Point.Add(GraphToLocal(graphPoint), new Size(Width / 2, Height / 2));
            if (RecipeViewModel.BeaconModules.Count > 0 && localPoint.X < (ModuleSpacing * 3) + 2) { //over modules
                string text = "Beacon Modules:";
                var moduleCounter = new Dictionary<ModuleQualityPair, int>();
                foreach (ModuleQualityPair m in RecipeViewModel.BeaconModules)
                    moduleCounter[m] = moduleCounter.TryGetValue(m, out int count) ? count + 1 : 1;
                foreach (ModuleQualityPair m in moduleCounter.Keys.OrderBy(m => m.Module.FriendlyName).ThenBy(m => m.Quality.Level).ThenBy(m => m.Quality.FriendlyName))
                    text += string.Format(DisplayCulture.Format, "\n   {0} :{1}", moduleCounter[m], m.FriendlyName);

                Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point(1 + moduleOffset.X + (RecipeViewModel.BeaconModules.Count > 2 ? RecipeViewModel.BeaconModules.Count > 4 ? RecipeViewModel.BeaconModules.Count > 6 ? ModuleSpacing * 5 / 2 : ModuleSpacing * 3 / 2 : ModuleSpacing * 4 / 2 : ModuleSpacing * 5 / 2) - (Width / 2), Height / 2)));
                tooltips.Add(new TooltipInfo(screen, Direction.Up, text));
            } else { //over beacon
                Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point(moduleOffset.X + (ModuleSpacing * 3) + 2 + (BeaconIconSize / 2) - (Width / 2), Height / 2)));
                tooltips.Add(new TooltipInfo(screen, Direction.Up, RecipeViewModel.SelectedBeacon.FriendlyName ?? ""));
            }

            return tooltips;
        }
    }
}
