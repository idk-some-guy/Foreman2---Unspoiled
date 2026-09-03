using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Models;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //New for this port: the module-count density tier a module display chose, exposed for test
    //introspection (canvas-reference.md §4 calls for behavior tests on the boundary, not only pixels).
    //Draw keeps its own literal count comparisons rather than consuming this, to preserve transcription
    //fidelity with upstream's inline branching.
    public enum ModuleDisplayTier { Icons, Dots, Tally }

    //Ports ProductionGraphView/Elements/AssemblerElement.cs in full: assembler icon + module icons/dots/
    //letter-tally at the 3 density tiers + the LOD-High stat readout.
    public sealed class AssemblerElement : GraphElement {
        private const int AssemblerIconSize = 54;
        private const int ModuleIconSize = 13;
        private const int ModuleSpacing = 12;

        //0,0 here is the top-left corner, matching upstream's local layout convention for this element.
        private static readonly Point[] moduleLocations = [new(ModuleSpacing, 0), new(ModuleSpacing, ModuleSpacing), new(ModuleSpacing, ModuleSpacing * 2), new(0, 0), new(0, ModuleSpacing), new(0, ModuleSpacing * 2)];
        private static readonly Point moduleOffset = new(0, 5);

        private static readonly SKPaint SpeedModulePaint = StrokePaint(new SKColor(0, 0, 139), 3);
        private static readonly SKPaint ProdModulePaint = StrokePaint(new SKColor(139, 0, 0), 3);
        private static readonly SKPaint EffModulePaint = StrokePaint(new SKColor(0, 100, 0), 3);
        private static readonly SKPaint QualityModulePaint = StrokePaint(new SKColor(255, 215, 0), 3);
        private static readonly SKPaint UnknownModulePaint = StrokePaint(SKColors.Black, 3);
        private static readonly SKTypeface ModuleTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        private const float ModuleFontSize = 6f;

        private static readonly SKTypeface InfoTypeface = SKTypeface.Default;
        private const float InfoFontSize = 5f;
        private static readonly SKTypeface CounterTypeface = SKTypeface.Default;
        private const float CounterFontSize = 14f;
        private static readonly SKColor TextColor = SKColors.Black;

        private static SKPaint StrokePaint(SKColor color, float width) => new() { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };

        private IRecipeNodeViewModel RecipeViewModel => (IRecipeNodeViewModel)parent.ViewModel;
        private readonly RecipeNodeElement parent;
        private readonly NodeElementContext context;

        internal ModuleDisplayTier ModuleTier => RecipeViewModel.AssemblerModules.Count <= moduleLocations.Length ? ModuleDisplayTier.Icons
            : RecipeViewModel.AssemblerModules.Count <= 4 * 7 ? ModuleDisplayTier.Dots
            : ModuleDisplayTier.Tally;

        public AssemblerElement(NodeElementContext context, RecipeNodeElement parent) : base(parent) {
            this.context = context;
            this.parent = parent;

            Width = AssemblerIconSize + (ModuleSpacing * 2) + 2;
            Height = AssemblerIconSize;
        }

        public void SetVisibility(bool visible) => Visible = visible;

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (style == NodeDrawingStyle.IconsOnly || style == NodeDrawingStyle.Simple)
                return;

            Point trans = LocalToGraph(new Point(-Width / 2, -Height / 2));

            if (RecipeViewModel.SelectedAssembler.Icon is SKBitmap assemblerIcon)
                canvas.DrawBitmap(assemblerIcon, SKRect.Create(trans.X + ModuleSpacing * 2 + 2, trans.Y, AssemblerIconSize, AssemblerIconSize));

            if (RecipeViewModel.AssemblerModules.Count <= 6) {
                for (int i = 0; i < moduleLocations.Length && i < RecipeViewModel.AssemblerModules.Count; i++)
                    if (RecipeViewModel.AssemblerModules[i].Icon is SKBitmap moduleIcon)
                        canvas.DrawBitmap(moduleIcon, SKRect.Create(trans.X + moduleLocations[i].X + moduleOffset.X, trans.Y + moduleLocations[i].Y + moduleOffset.Y, ModuleIconSize, ModuleIconSize));
            } else if (RecipeViewModel.AssemblerModules.Count <= 4 * 7) { //4x7 dot grid, 28 modules max
                for (int x = 0; x < 4; x++) {
                    for (int y = 0; y < 7; y++) {
                        if (RecipeViewModel.AssemblerModules.Count > (x * 7) + y) {
                            SKPaint marker = RecipeViewModel.AssemblerModules[(x * 7) + y].Module.GetProductivityBonus() > 0 ? ProdModulePaint :
                                RecipeViewModel.AssemblerModules[(x * 7) + y].Module.GetQualityBonus() > 0 ? QualityModulePaint :
                                RecipeViewModel.AssemblerModules[(x * 7) + y].Module.GetConsumptionBonus() < 0 ? EffModulePaint :
                                RecipeViewModel.AssemblerModules[(x * 7) + y].Module.GetSpeedBonus() > 0 ? SpeedModulePaint :
                                UnknownModulePaint;
                            canvas.DrawOval(SKRect.Create(trans.X + moduleOffset.X + ModuleSpacing + ModuleIconSize - 3 - (x * 7), trans.Y + moduleOffset.Y + (y * 7), 3, 3), marker);
                        }
                    }
                }
            } else {
                int prodModules = RecipeViewModel.AssemblerModules.Count(m => m.Module.GetProductivityBonus() > 0);
                int qualityModules = RecipeViewModel.AssemblerModules.Count(m => m.Module.GetQualityBonus() > 0 && m.Module.GetProductivityBonus() <= 0);
                int efficiencyModules = RecipeViewModel.AssemblerModules.Count(m => m.Module.GetConsumptionBonus() < 0 && m.Module.GetProductivityBonus() <= 0 && m.Module.GetQualityBonus() <= 0);
                int speedModules = RecipeViewModel.AssemblerModules.Count(m => m.Module.GetSpeedBonus() > 0 && m.Module.GetConsumptionBonus() >= 0 && m.Module.GetProductivityBonus() <= 0 && m.Module.GetQualityBonus() <= 0);
                int unknownModules = RecipeViewModel.AssemblerModules.Count - prodModules - efficiencyModules - speedModules - qualityModules;
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(0, 0, 139), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "S:{0}", speedModules), trans.X, trans.Y + 10);
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(0, 100, 0), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "E:{0}", efficiencyModules), trans.X, trans.Y + 20);
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(139, 0, 0), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "P:{0}", prodModules), trans.X, trans.Y + 30);
                GraphicsStuff.DrawStringAtPoint(canvas, new SKColor(255, 215, 0), ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "Q:{0}", qualityModules), trans.X, trans.Y + 40);
                GraphicsStuff.DrawStringAtPoint(canvas, SKColors.Black, ModuleTypeface, ModuleFontSize, string.Format(DisplayCulture.Format, "U:{0}", unknownModules), trans.X, trans.Y + 50);
            }

            int parentHalfWidth = Parent is RecipeNodeElement recipeParent ? recipeParent.Width : Width;
            var textbox = new Rectangle(trans.X + Width, trans.Y + 10, (parentHalfWidth / 2) - X - (Width / 2) - 6, 30);
            if (context.LevelOfDetail == LevelOfDetail.High && (RecipeViewModel.SelectedAssembler.Assembler.EntityType == EntityType.Assembler || RecipeViewModel.SelectedAssembler.Assembler.EntityType == EntityType.Miner || RecipeViewModel.SelectedAssembler.Assembler.EntityType == EntityType.OffshorePump)) {
                if (RecipeViewModel.GetQualityMultiplier() > 0) {
                    GraphicsStuff.DrawStringAtPoint(canvas, TextColor, InfoTypeface, InfoFontSize, "Speed:\nProd:\nPower:\nQuality:", trans.X + Width + 2, trans.Y);
                    GraphicsStuff.DrawStringAtPoint(canvas, TextColor, InfoTypeface, InfoFontSize,
                        string.Format(DisplayCulture.Format, "{0:+0%; -0%; 0%}\n{1:+0%; -0%; 0%}\n{2:+0%; -0%; 0%}\n{3:+0%; -0%; 0%}", RecipeViewModel.GetSpeedMultiplier() - 1, RecipeViewModel.GetProductivityMultiplier() - 1, RecipeViewModel.GetConsumptionMultiplier() - 1, RecipeViewModel.GetQualityMultiplier()),
                        trans.X + Width + 26, trans.Y);
                } else {
                    GraphicsStuff.DrawStringAtPoint(canvas, TextColor, InfoTypeface, InfoFontSize, "Speed:\nProd:\nPower:", trans.X + Width + 2, trans.Y);
                    GraphicsStuff.DrawStringAtPoint(canvas, TextColor, InfoTypeface, InfoFontSize,
                        string.Format(DisplayCulture.Format, "{0:+0%; -0%; 0%}\n{1:+0%; -0%; 0%}\n{2:+0%; -0%; 0%}", RecipeViewModel.GetSpeedMultiplier() - 1, RecipeViewModel.GetProductivityMultiplier() - 1, RecipeViewModel.GetConsumptionMultiplier() - 1),
                        trans.X + Width + 26, trans.Y);
                }

                textbox.Y = trans.Y + 28;
            } else if (context.LevelOfDetail == LevelOfDetail.High && RecipeViewModel.SelectedAssembler.Assembler.EntityType == EntityType.Generator) {
                GraphicsStuff.DrawStringAtPoint(canvas, TextColor, InfoTypeface, InfoFontSize, "Power:", trans.X + Width, trans.Y + 10);
                double generatorEffectivity = RecipeViewModel.GetGeneratorEffectivity();
                GraphicsStuff.DrawStringAtPoint(canvas, TextColor, InfoTypeface, InfoFontSize, string.Format(DisplayCulture.Format, "{0:P0}", generatorEffectivity), trans.X + Width + 26, trans.Y + 10);

                textbox.Y = trans.Y + 24;
            }

            string text = "x";
            if (RecipeViewModel.SelectedAssembler.Assembler.IsMissing)
                text += "---";
            else
                text += GraphicsStuff.BuildingQuantityToText(RecipeViewModel.ActualSetValue, context.RoundAssemblerCount);

            GraphicsStuff.DrawText(canvas, TextColor, CounterTypeface, CounterFontSize, textbox, text, TextHorizontalAlign.Near, TextVerticalAlign.Near, singleLine: true);
        }

        public override List<TooltipInfo> GetToolTips(Point graphPoint) {
            if (!Visible)
                return [];

            var tooltips = new List<TooltipInfo>();

            Point localPoint = Point.Add(GraphToLocal(graphPoint), new Size(Width / 2, Height / 2));
            if (localPoint.X < (ModuleSpacing * 2) + 2 && RecipeViewModel.AssemblerModules.Count > 0) { //over modules
                string text = "Assembler Modules:";
                var moduleCounter = new Dictionary<ModuleQualityPair, int>();
                foreach (ModuleQualityPair m in RecipeViewModel.AssemblerModules)
                    moduleCounter[m] = moduleCounter.TryGetValue(m, out int count) ? count + 1 : 1;
                foreach (ModuleQualityPair m in moduleCounter.Keys.OrderBy(m => m.Module.FriendlyName).ThenBy(m => m.Quality.Level).ThenBy(m => m.Quality.FriendlyName))
                    text += string.Format(DisplayCulture.Format, "\n   {0} :{1}", moduleCounter[m], m.FriendlyName);

                Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point(1 + (RecipeViewModel.AssemblerModules.Count > 3 ? RecipeViewModel.AssemblerModules.Count > 6 ? ModuleSpacing * 3 / 2 : ModuleSpacing : ModuleSpacing * 3 / 2) - (Width / 2), -Height / 2)));
                tooltips.Add(new TooltipInfo(screen, Direction.Down, text));
            } else { //over assembler
                Avalonia.Point screen = context.Viewport.GraphToScreen(LocalToGraph(new Point((ModuleSpacing * 2) + 2 + (AssemblerIconSize / 2) - (Width / 2), -Height / 2)));
                tooltips.Add(new TooltipInfo(screen, Direction.Down, RecipeViewModel.SelectedAssembler.FriendlyName ?? ""));
            }

            return tooltips;
        }
    }
}
