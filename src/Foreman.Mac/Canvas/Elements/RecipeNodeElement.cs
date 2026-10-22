using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/RecipeNodeElement.cs: Draw/DetailsDraw/UpdateState/UpdateValues/
    //tooltips, plus the ~280-line paste-options right-click menu, AddRClickMenuOptions (reference
    //§4c) - the only node subclass whose AddRClickMenuOptions isn't the base no-op.
    public sealed class RecipeNodeElement : BaseNodeElement {
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

            Height = Context.LevelOfDetail == LevelOfDetail.Low ? BaseSimpleHeight : BaseRecipeHeight;
            base.UpdateState();
        }

        protected override int ComputeWidth() => WidthForTabs(Math.Max(TabSpan(InputTabs), TabSpan(OutputTabs)));

        protected override string DisplayName => RecipeName;
        protected override SKBitmap? TitleIcon => RecipeViewModel.BaseRecipe.Icon;
        protected override SKColor StripColor(CanvasTokens tokens) => tokens.NodeRecipe;
        protected override bool Unavailable => IsUnavailable(RecipeViewModel.BaseRecipe.Recipe);
        protected override SKBitmap? NodeIcon() => RecipeViewModel.BaseRecipe.Icon;

        private readonly LabelFade count = new();
        private SKRect buildingIconRect;
        private IReadOnlyList<StatusItem> statusRow = [];

        internal IReadOnlyList<StatusItem> StatusRow => statusRow;
        internal LabelState CountLabel => count.State(Context);

        internal int ProductivityPips =>
            RecipeViewModel.AssemblerModules.Count(m => m.Module.GetProductivityBonus() > 0)
            + (int)(RecipeViewModel.BeaconModules.Count(m => m.Module.GetProductivityBonus() > 0) * RecipeViewModel.BeaconCount);

        private bool ShowsExtraProductivity =>
            RecipeViewModel.ExtraProductivity > 0 && (RecipeViewModel.SelectedAssembler.Assembler.EntityType == EntityType.Miner || Context.EnableExtraProductivityForNonMiners);

        //Building row (§01): icon 20 · count 13/600, or a "Fixed ×{n}" chip · building name; then the status row at high detail.
        private protected override void LayoutDetail(CanvasTokens tokens, List<NodeText> detail) {
            float rowTop = Top + NodeCardPainter.DetailRowTop(tokens);
            float rowHeight = NodeCardPainter.BuildingRowHeight * (float)tokens.Scale;
            float gap = tokens.Space15;
            bool statusRowShown = Context.LevelOfDetail != LevelOfDetail.Low;
            float right = statusRowShown ? Right - tokens.NodeInsetSide : PlaceBadgeTrailing(tokens, rowTop, rowHeight);
            buildingIconRect = SKRect.Create(Left + tokens.NodeInsetSide, rowTop + ((rowHeight - tokens.NodeBuildingIcon) / 2), tokens.NodeBuildingIcon, tokens.NodeBuildingIcon);
            float x = buildingIconRect.Right + gap;

            if (RecipeViewModel.RateType == RateType.Manual) {
                x = LayoutChip(tokens, detail, "Fixed ×" + NodeCardPainter.Number(RecipeViewModel.DesiredSetValue), x, rowTop, rowHeight) + gap;
            } else {
                count.Set(SolveState == NodeSolveState.NeverSolved ? "—"
                    : "×" + (RecipeViewModel.SelectedAssembler.Assembler.IsMissing ? "---" : NodeCardPainter.Count(RecipeViewModel.ActualSetValue, Context.RoundAssemblerCount)), Context);
                int weight = tokens.CanvasEmphasizedWeight;
                float countWidth = NodeCardPainter.Measure(count.Text, tokens.EmphasizedBodyFontSize, weight);
                detail.Add(ValueText(NodeTextRole.Count, count.Text, SKRect.Create(x, rowTop, countWidth, rowHeight), tokens.EmphasizedBodyFontSize, weight, tokens.Text));
                x += countWidth + gap;
            }
            string building = NodeCardPainter.Fit(RecipeViewModel.SelectedAssembler.FriendlyName ?? "", right - x, tokens.CanvasFontSize, 400, out _);
            detail.Add(new NodeText(NodeTextRole.Detail, building, new SKRect(x, rowTop, right, rowTop + rowHeight), tokens.CanvasFontSize, 400, tokens.TextSecondary));

            statusRow = statusRowShown ? LayoutStatusRow(tokens, detail) : [];
            BeaconElement.Place(statusRow.Where(item => item.Kind == StatusItemKind.Beacon));
            AssemblerElement.Place(statusRow.Where(item => item.Kind != StatusItemKind.Beacon));
            BeaconElement.SetVisibility(Context.LevelOfDetail != LevelOfDetail.Low);
            AssemblerElement.SetVisibility(Context.LevelOfDetail != LevelOfDetail.Low);
        }

        //State badge · beacon badge · productivity pips (capped at 6) · extra productivity; missing items collapse leftward.
        private List<StatusItem> LayoutStatusRow(CanvasTokens tokens, List<NodeText> detail) {
            float rowTop = Top + NodeCardPainter.StatusRowTop(tokens);
            float badge = tokens.NodeBadgeHeight;
            float gap = tokens.Space15;
            float x = Left + tokens.NodeInsetSide;
            float afterBadge = PlaceBadge(tokens, x, rowTop, badge);
            if (afterBadge > x)
                x = afterBadge + gap;
            var items = new List<StatusItem>();

            if (RecipeViewModel.SelectedBeacon) {
                items.Add(new StatusItem(StatusItemKind.Beacon, SKRect.Create(x, rowTop, badge, badge)));
                x += badge + gap;
            }
            int pips = Math.Min(6, ProductivityPips);
            for (int i = 0; i < pips; i++) {
                items.Add(new StatusItem(StatusItemKind.Pip, SKRect.Create(x, rowTop + ((badge - tokens.NodePip) / 2), tokens.NodePip, tokens.NodePip)));
                x += tokens.NodePip + (i == pips - 1 ? gap : tokens.NodeGapTight);
            }
            if (ShowsExtraProductivity) {
                string text = RecipeViewModel.ExtraProductivity.ToString("+0%", DisplayCulture.Format);
                int weight = tokens.CanvasEmphasizedWeight;
                SKRect rect = SKRect.Create(x, rowTop, NodeCardPainter.Measure(text, tokens.CanvasFontSize, weight), badge);
                items.Add(new StatusItem(StatusItemKind.ExtraProductivity, rect));
                detail.Add(new NodeText(NodeTextRole.ExtraProductivity, text, rect, tokens.CanvasFontSize, weight, tokens.TextSecondary));
            }
            return items;
        }

        protected override void DetailsDraw(SKCanvas canvas, Point trans) {
            base.DetailsDraw(canvas, trans);
            SKRect icon = buildingIconRect;
            icon.Offset(trans.X, trans.Y);
            NodeCardPainter.DrawIcon(canvas, RecipeViewModel.SelectedAssembler.Icon, icon);
        }

        private protected override void DrawNodeText(SKCanvas canvas, NodeText text, Point trans) {
            if (text.Role == NodeTextRole.Count)
                NodeCardPainter.DrawLabel(canvas, text, trans.X, trans.Y, count.State(Context));
            else
                base.DrawNodeText(canvas, text, trans);
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

        //The recipe hover card is retired (contract §03) - it folds into the inspector's own
        //identity header (RecipeCardView) instead. Help tooltips stay.
        protected override List<TooltipInfo> GetMyToolTips(Point graphPoint, bool exclusive) {
            var tooltips = new List<TooltipInfo>();

            string entityName = RecipeViewModel.SelectedAssembler.Assembler is IAssembler helpAssembler
                ? helpAssembler.GetEntityTypeName(false).ToLowerInvariant()
                : "assembler";
            tooltips.AddRange(ExclusiveHelpTooltip($"Left click on this node to edit its {entityName}, modules, beacon, etc.\nRight click for options.", exclusive));

            return tooltips;
        }
    }
}
