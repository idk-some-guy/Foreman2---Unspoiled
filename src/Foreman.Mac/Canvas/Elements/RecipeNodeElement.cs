using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/RecipeNodeElement.cs: Draw/DetailsDraw/UpdateState/UpdateValues/
    //tooltips (P3), plus the ~280-line paste-options right-click menu, AddRClickMenuOptions (P4, reference
    //§4c) - the only node subclass whose AddRClickMenuOptions isn't the base no-op.
    public sealed class RecipeNodeElement : BaseNodeElement {
        protected override SKPaint CleanBgPaint => RecipeBgPaint;
        private static readonly SKPaint RecipeBgPaint = Fill(new SKColor(190, 217, 212));

        private static readonly SKPaint ProductivityPaint = StrokePaint(new SKColor(139, 0, 0), 6);
        private static readonly SKPaint ProductivityPlusPaint = StrokePaint(new SKColor(139, 0, 0), 2);
        private static readonly SKPaint ExtraProductivityPaint = StrokePaint(new SKColor(220, 20, 60), 6);

        private static SKPaint StrokePaint(SKColor color, float width) => new() { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };

        //Exposed (rather than upstream's private fields) purely so tests can inspect the module density-tier
        //boundary (ModuleDisplayTier) directly instead of only through pixels; nothing outside
        //Foreman.Mac.UiTests reads these.
        internal AssemblerElement AssemblerElement { get; }
        internal BeaconElement BeaconElement { get; }

        internal IRecipeNodeViewModel RecipeViewModel => (IRecipeNodeViewModel)ViewModel;
        private string RecipeName => RecipeViewModel.BaseRecipe.FriendlyName ?? "";

        //Session-lifetime remembered checkbox defaults (reference §4c): process state, not per-node state -
        //whatever the user last confirmed in "Paste selected options" is what the next paste-options menu
        //(on any node) starts pre-checked with, for as long as the app keeps running.
        private static bool OptionsCopyAssemblerDefault = true;
        private static bool OptionsCopyExtraProductivityMinersDefault = true;
        private static bool OptionsCopyExtraProductivityNonMinersDefault = true;
        private static bool OptionsCopyFuelDefault = true;
        private static bool OptionsCopyModulesDefault = true;
        private static bool OptionsCopyBeaconDefault = true;
        private static bool OptionsCopyBeaconModulesDefault = true;

        public RecipeNodeElement(NodeElementContext context, IRecipeNodeViewModel viewModel) : base(context, viewModel) {
            AssemblerElement = new AssemblerElement(context, this);
            AssemblerElement.SetVisibility(context.LevelOfDetail != LevelOfDetail.Low);

            BeaconElement = new BeaconElement(context, this);
            BeaconElement.SetVisibility(context.LevelOfDetail != LevelOfDetail.Low);

            UpdateState();
        }

        protected override void UpdateState() {
            //recipe inputs/outputs can change after construction (fuel/quality/module selection) - reconcile
            //tabs against the live view model before laying them out, same as furnaces upstream.
            foreach (ItemTabElement oldTab in InputTabs.Where(tab => !RecipeViewModel.Inputs.Contains(tab.Item)).ToList()) {
                InputTabs.Remove(oldTab);
                SubElements.Remove(oldTab);
            }
            foreach (ItemTabElement oldTab in OutputTabs.Where(tab => !RecipeViewModel.Outputs.Contains(tab.Item)).ToList()) {
                OutputTabs.Remove(oldTab);
                SubElements.Remove(oldTab);
            }
            foreach (var item in RecipeViewModel.Inputs)
                if (!InputTabs.Any(tab => tab.Item == item))
                    InputTabs.Add(new ItemTabElement(item, LinkType.Input, Context, this));
            foreach (var item in RecipeViewModel.Outputs)
                if (!OutputTabs.Any(tab => tab.Item == item))
                    OutputTabs.Add(new ItemTabElement(item, LinkType.Output, Context, this));

            int yOffset = (RecipeViewModel.NodeDirection == NodeDirection.Up && InputTabs.Count == 0 && OutputTabs.Count != 0) || (RecipeViewModel.NodeDirection == NodeDirection.Down && OutputTabs.Count == 0 && InputTabs.Count != 0) ? 10 :
                          (RecipeViewModel.NodeDirection == NodeDirection.Down && InputTabs.Count == 0 && OutputTabs.Count != 0) || (RecipeViewModel.NodeDirection == NodeDirection.Up && OutputTabs.Count == 0 && InputTabs.Count != 0) ? -10 : 0;
            yOffset += RecipeViewModel.NodeDirection == NodeDirection.Up ? 4 : 0;

            AssemblerElement.Location = new Point(-26, -14 + yOffset);
            BeaconElement.Location = new Point(-30, 27 + yOffset);

            AssemblerElement.SetVisibility(Context.LevelOfDetail != LevelOfDetail.Low);
            BeaconElement.SetVisibility(Context.LevelOfDetail != LevelOfDetail.Low);

            Width = Math.Max(MinWidth, Math.Max(GetIconWidths(InputTabs), GetIconWidths(OutputTabs)) + 10);
            if (Width % WidthD != 0) {
                Width += WidthD;
                Width -= Width % WidthD;
            }
            Height = Context.LevelOfDetail == LevelOfDetail.Low ? BaseSimpleHeight : BaseRecipeHeight;

            base.UpdateState();
        }

        protected override SKBitmap? NodeIcon() => RecipeViewModel.BaseRecipe.Icon;

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            if (Context.LevelOfDetail == LevelOfDetail.Low) {
                bool overproducing = RecipeViewModel.IsOverproducing();
                var textSlot = new Rectangle(trans.X - (Width / 2) + 40, trans.Y - (Height / 2) + (overproducing ? 32 : 27), Width - 10 - 40, Height - (overproducing ? 64 : 54));
                int textLength = GraphicsStuff.DrawText(canvas, TextColor, RegularTypeface, BaseFontSize, textSlot, RecipeName, TextHorizontalAlign.Center, TextVerticalAlign.Center);

                SKBitmap assemblerIcon = RecipeViewModel.SelectedAssembler ? (RecipeViewModel.SelectedAssembler.Icon ?? IconCache.UnknownIcon) : IconCache.UnknownIcon;
                canvas.DrawBitmap(assemblerIcon, SKRect.Create(trans.X - Math.Min((Width / 2) - 10, (textLength / 2) + 32), trans.Y - 16, 32, 32));

                int pModules = RecipeViewModel.AssemblerModules.Count(m => m.Module.GetProductivityBonus() > 0);
                pModules += (int)(RecipeViewModel.BeaconModules.Count(m => m.Module.GetProductivityBonus() > 0) * RecipeViewModel.BeaconCount);

                bool extraProductivity = RecipeViewModel.ExtraProductivity > 0 && (RecipeViewModel.SelectedAssembler.Assembler.EntityType == EntityType.Miner || Context.EnableExtraProductivityForNonMiners);
                pModules += extraProductivity ? 1 : 0;

                for (int i = 0; i < pModules && i < 6; i++)
                    canvas.DrawOval(SKRect.Create(trans.X - (Width / 2) - 1, trans.Y - (Height / 2) + 10 + i * 12, 6, 6), (extraProductivity && i == 0) ? ExtraProductivityPaint : ProductivityPaint);
                if (pModules > 6) {
                    canvas.DrawLine(trans.X - (Width / 2) - 4, trans.Y - (Height / 2) + 84, trans.X - (Width / 2) + 8, trans.Y - (Height / 2) + 84, ProductivityPlusPaint);
                    canvas.DrawLine(trans.X - (Width / 2) + 2, trans.Y - (Height / 2) + 84 - 6, trans.X - (Width / 2) + 2, trans.Y - (Height / 2) + 84 + 6, ProductivityPlusPaint);
                }
            } else if (RecipeViewModel.ExtraProductivity > 0 && (RecipeViewModel.SelectedAssembler.Assembler.EntityType == EntityType.Miner || Context.EnableExtraProductivityForNonMiners)) {
                canvas.DrawOval(SKRect.Create(trans.X - (Width / 2) - 1, trans.Y - (Height / 2) + 10, 6, 6), ExtraProductivityPaint);
            }
        }

        //Ports AddRClickMenuOptions (reference §4c). `nodeInSelection` means "the target set is the current
        //multi-selection (if this node is part of it) or just this node alone" - already resolved by
        //BaseNodeElement.BuildRightClickMenu before calling in.
        protected override void AddRClickMenuOptions(List<MenuEntry> entries, bool nodeInSelection) {
            if (nodeInSelection) {
                List<IRecipeNodeViewModel> targets = [.. (Context.SelectedNodes ?? []).OfType<RecipeNodeElement>().Select(e => e.RecipeViewModel)];
                if (!targets.Contains(RecipeViewModel))
                    targets.Add(RecipeViewModel);

                entries.Add(MenuEntry.Divider);
                entries.Add(MenuEntry.Item("Apply default assembler(s)", () => ApplyToEachTarget(targets, c => c.AutoSetAssembler())));
                entries.Add(MenuEntry.Item("Apply default modules", () => ApplyToEachTarget(targets, c => c.AutoSetAssemblerModules())));
                if (targets.Any(t => t.AssemblerModules.Count > 0))
                    entries.Add(MenuEntry.Item("Remove modules", () => ApplyToEachTarget(targets, c => c.RemoveAssemblerModules())));
                if (targets.Any(t => t.SelectedBeacon))
                    entries.Add(MenuEntry.Item("Remove beacons", () => ApplyToEachTarget(targets, c => c.ClearBeacon())));

                entries.Add(MenuEntry.Divider);
                AddPasteOptionsBlock(entries, targets);
            } else
                entries.Add(MenuEntry.Divider);

            entries.Add(MenuEntry.Item("Copy this assembler's options", () =>
                Context.SetClipboardText?.Invoke(GraphSaveCodec.WriteNodeCopyOptionsToString(new NodeCopyOptions(RecipeViewModel).ToSaveDocument()))));
        }

        private void ApplyToEachTarget(List<IRecipeNodeViewModel> targets, Action<RecipeNodeController> apply) {
            foreach (IRecipeNodeViewModel target in targets)
                if (Context.Editor?.RequestNodeController(target.Id) is RecipeNodeController controller)
                    apply(controller);
        }

        //Ports step 5-6 of reference §4c: the paste-options checkbox block, built only if a DataCache and a
        //clipboard `NodeCopyOptions` payload with a resolvable assembler are both available. Each field's
        //"can paste" predicate is evaluated across every target node, deciding whether its checkbox appears
        //at all - "Paste selected options" itself re-checks the same predicate per node, per field, so a
        //checked-but-incompatible target is silently skipped rather than applied.
        private void AddPasteOptionsBlock(List<MenuEntry> entries, List<IRecipeNodeViewModel> targets) {
            if (Context.DCache is not DataCache cache)
                return;
            string? clipboardText = Context.GetClipboardText?.Invoke();
            if (clipboardText is null)
                return;
            NodeCopyOptions? pasteOptions = NodeCopyOptions.GetNodeCopyOptions(clipboardText, cache);
            if (pasteOptions is null || pasteOptions.Assembler.Assembler is not IAssembler pastedAssembler)
                return;

            bool canPasteAssembler = targets.Any(t => t.BaseRecipe.Recipe is IRecipe recipe && recipe.Assemblers.Contains(pastedAssembler));
            bool canPasteExtraProductivityMiners = targets.Any(t => t.SelectedAssembler.Assembler is IAssembler a && a.EntityType == EntityType.Miner);
            bool canPasteExtraProductivityNonMiners = Context.EnableExtraProductivityForNonMiners && targets.Any(t => t.SelectedAssembler.Assembler is IAssembler a && a.EntityType != EntityType.Miner);
            bool canPasteFuel = pasteOptions.Fuel is IItem pasteFuelOption && (canPasteAssembler || targets.Any(t => t.BaseRecipe.Recipe is IRecipe recipe && recipe.Assemblers.Any(a => a.Fuels.Contains(pasteFuelOption))));
            bool canPasteModules = pasteOptions.AssemblerModules.Count > 0 && (canPasteAssembler || targets.Any(t => t.BaseRecipe.Recipe is IRecipe recipe && recipe.AssemblerModules.Count > 0 && t.SelectedAssembler.Assembler is IAssembler a && a.Modules.Count > 0 && a.ModuleSlots > 0));
            bool canPasteBeacon = pasteOptions.Beacon && (canPasteAssembler || targets.Any(t => t.BaseRecipe.Recipe is IRecipe recipe && recipe.AssemblerModules.Count > 0 && t.SelectedAssembler.Assembler is IAssembler a && a.Modules.Count > 0));

            if (!(canPasteAssembler || canPasteFuel || canPasteModules || canPasteBeacon))
                return;

            var assemblerState = new MenuCheckboxState(OptionsCopyAssemblerDefault);
            var extraProdMinersState = new MenuCheckboxState(OptionsCopyExtraProductivityMinersDefault);
            var extraProdNonMinersState = new MenuCheckboxState(OptionsCopyExtraProductivityNonMinersDefault);
            var fuelState = new MenuCheckboxState(OptionsCopyFuelDefault);
            var modulesState = new MenuCheckboxState(OptionsCopyModulesDefault);
            var beaconState = new MenuCheckboxState(OptionsCopyBeaconDefault);
            var beaconModulesState = new MenuCheckboxState(OptionsCopyBeaconModulesDefault);

            if (canPasteAssembler)
                entries.Add(MenuEntry.Checkable(pastedAssembler.GetEntityTypeName(false), assemblerState));
            if (canPasteExtraProductivityMiners)
                entries.Add(MenuEntry.Checkable("Bonus Productivity (Miners)", extraProdMinersState));
            if (canPasteExtraProductivityNonMiners)
                entries.Add(MenuEntry.Checkable("Bonus Productivity (non-Miners)", extraProdNonMinersState));
            if (canPasteFuel)
                entries.Add(MenuEntry.Checkable("Fuel", fuelState));
            if (canPasteModules)
                entries.Add(MenuEntry.Checkable("Modules", modulesState));
            if (canPasteBeacon)
                entries.Add(MenuEntry.Checkable("Beacon", beaconState));
            if (canPasteBeacon)
                entries.Add(MenuEntry.Checkable("Beacon Modules", beaconModulesState));

            entries.Add(MenuEntry.Divider);
            entries.Add(MenuEntry.Item("Paste selected options", () => {
                if (canPasteAssembler) OptionsCopyAssemblerDefault = assemblerState.Checked;
                if (canPasteExtraProductivityMiners) OptionsCopyExtraProductivityMinersDefault = extraProdMinersState.Checked;
                if (canPasteExtraProductivityNonMiners) OptionsCopyExtraProductivityNonMinersDefault = extraProdNonMinersState.Checked;
                if (canPasteFuel) OptionsCopyFuelDefault = fuelState.Checked;
                if (canPasteModules) OptionsCopyModulesDefault = modulesState.Checked;
                if (canPasteBeacon) OptionsCopyBeaconDefault = beaconState.Checked;
                if (canPasteBeacon) OptionsCopyBeaconModulesDefault = beaconModulesState.Checked;

                foreach (IRecipeNodeViewModel target in targets) {
                    if (Context.Editor?.RequestNodeController(target.Id) is not RecipeNodeController controller)
                        continue;

                    if (assemblerState.Checked && target.BaseRecipe.Recipe is IRecipe targetRecipe && targetRecipe.Assemblers.Contains(pastedAssembler)) {
                        controller.SetAssembler(pasteOptions.Assembler);
                        if (target.SelectedAssembler.Assembler is IAssembler selectedAssembler && selectedAssembler.EntityType == EntityType.Reactor)
                            controller.SetNeighbourCount(pasteOptions.NeighbourCount);
                    }

                    if (extraProdMinersState.Checked && target.SelectedAssembler.Assembler is IAssembler minerAssembler && minerAssembler.EntityType == EntityType.Miner)
                        controller.SetExtraProductivityBonus(pasteOptions.ExtraProductivityBonus);
                    if (extraProdNonMinersState.Checked && target.SelectedAssembler.Assembler is IAssembler nonMinerAssembler && nonMinerAssembler.EntityType != EntityType.Miner)
                        controller.SetExtraProductivityBonus(pasteOptions.ExtraProductivityBonus);

                    if (fuelState.Checked && pasteOptions.Fuel is IItem pasteFuel && target.SelectedAssembler.Assembler is IAssembler fuelAssembler && fuelAssembler.Fuels.Contains(pasteFuel))
                        controller.SetFuel(pasteFuel);

                    if (modulesState.Checked && target.SelectedAssembler.Assembler is IAssembler moduleAssembler && target.BaseRecipe.Recipe is IRecipe moduleRecipe) {
                        var acceptableAssemblerModules = new HashSet<IModule>(moduleRecipe.AssemblerModules.Intersect(moduleAssembler.Modules));
                        if (!pasteOptions.AssemblerModules.Any(module => module.Module is IModule copiedModule && !acceptableAssemblerModules.Contains(copiedModule)))
                            controller.SetAssemblerModules(pasteOptions.AssemblerModules, true);
                    }

                    if (beaconState.Checked && target.SelectedAssembler.Assembler is IAssembler beaconHostAssembler && target.BaseRecipe.Recipe is IRecipe beaconRecipe && beaconRecipe.AssemblerModules.Intersect(beaconHostAssembler.Modules).Any() && pasteOptions.Beacon) {
                        controller.SetBeacon(pasteOptions.Beacon);
                        controller.SetBeaconCount(pasteOptions.BeaconCount);
                        controller.SetBeaconsCont(pasteOptions.BeaconsConst);
                        controller.SetBeaconsPerAssembler(pasteOptions.BeaconsPerAssembler);
                    }

                    if (beaconModulesState.Checked && target.SelectedBeacon && target.SelectedBeacon.Beacon is IBeacon selectedBeacon && target.SelectedAssembler.Assembler is IAssembler beaconModuleHostAssembler && target.BaseRecipe.Recipe is IRecipe beaconModuleRecipe) {
                        var acceptableBeaconModules = new HashSet<IModule>(beaconModuleRecipe.AssemblerModules.Intersect(beaconModuleHostAssembler.Modules).Intersect(selectedBeacon.Modules));
                        if (!pasteOptions.BeaconModules.Any(module => module.Module is IModule copiedBeaconModule && !acceptableBeaconModules.Contains(copiedBeaconModule)))
                            controller.SetBeaconModules(pasteOptions.BeaconModules, true);
                    }
                }

                Context.Editor?.Graph.UpdateNodeValues();
            }));
            entries.Add(MenuEntry.Divider);
        }

        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) {
            var tooltips = new List<TooltipInfo>();

            if (Context.ShowRecipeToolTip && RecipeViewModel.BaseRecipe.Recipe is IRecipe recipe) {
                IRecipe[] recipes = [recipe];
                Size size = RecipePainter.GetSize(recipes, Context.AbbreviateSciPacks);
                tooltips.Add(new TooltipInfo(
                    Context.Viewport.GraphToScreen(LocalToGraph(new Point(Width / 2, 0))),
                    Direction.Left,
                    null,
                    new Avalonia.Size(size.Width, size.Height),
                    (canvas, offset) => RecipePainter.Paint(recipes, canvas, offset, Context.AbbreviateSciPacks)));
            }

            string entityName = RecipeViewModel.SelectedAssembler.Assembler is IAssembler helpAssembler
                ? helpAssembler.GetEntityTypeName(false).ToLowerInvariant()
                : "assembler";
            tooltips.AddRange(ExclusiveHelpTooltip($"Left click on this node to edit its {entityName}, modules, beacon, etc.\nRight click for options.", exclusive));

            return tooltips;
        }
    }
}
