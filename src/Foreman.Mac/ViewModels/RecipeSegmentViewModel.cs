using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    //Backs RecipeSegmentView - suppressWrites stops Refresh() from bouncing its own reads back out through the controller.
    public sealed partial class RecipeSegmentViewModel : ObservableObject {
        private const int PickIconSize = 28;

        private readonly IRecipeNodeViewModel node;
        public IRecipeNodeViewModel Node => node;
        private readonly GraphViewer viewer;
        private readonly RecipeNodeController controller;

        private DataCache Cache { get; }
        private readonly Action? requestRedraw;
        private readonly Dictionary<SKBitmap, Bitmap> iconCache = [];
        private readonly Dictionary<SKBitmap, Bitmap> cardIconCache = [];
        private bool suppressWrites;

        [ObservableProperty]
        private bool isFixedRate;

        [ObservableProperty]
        private double assemblerCount;

        [ObservableProperty]
        private bool lowPriority;

        [ObservableProperty]
        private bool isKeyNode;

        [ObservableProperty]
        private string keyNodeTitle = "";

        [ObservableProperty]
        private double avgNeighbours;

        [ObservableProperty]
        private double extraProductivityPercent;

        public PickGridItem? SelectedAssembler { get; private set; }
        public IImage? SelectedAssemblerIcon { get; private set; }
        public string SelectedAssemblerName { get; private set; } = "";
        public string AssemblerStats { get; private set; } = "";
        public IReadOnlyList<PickGridItem> AssemblerOptions { get; private set; } = [];
        public bool ShowsFuel { get; private set; }
        public IReadOnlyList<PickGridItem> FuelOptions { get; private set; } = [];

        public string ModulesTitle { get; private set; } = "";
        public IReadOnlyList<PickGridItem> EquippedModules { get; private set; } = [];
        public IReadOnlyList<PickGridItem> ModuleOptions { get; private set; } = [];

        public bool HasBeacon { get; private set; }
        public string BeaconName { get; private set; } = "-none-";
        public PickGridItem? SelectedBeacon { get; private set; }
        public IImage? SelectedBeaconIcon { get; private set; }
        public IReadOnlyList<PickGridItem> BeaconOptions { get; private set; } = [];
        public IReadOnlyList<PickGridItem> EquippedBeaconModules { get; private set; } = [];
        public IReadOnlyList<PickGridItem> BeaconModuleOptions { get; private set; } = [];

        public bool ShowsQuality { get; private set; }
        public IReadOnlyList<PickGridItem> QualityOptions { get; private set; } = [];
        public string CurrentQualityName { get; private set; } = "";
        public SKBitmap? CurrentQualityIcon { get; private set; }

        //Recipe card fold (Task 9): the identity header's own card, built once per assembler/recipe change
        //from the same recipe data RecipePainter used - null for a recipe with no base recipe resolved.
        public RecipeCardViewModel? Card { get; private set; }
        public bool HasCard => Card is not null;

        //A25 .error: the Recipe group swaps the card for Kit.StatePanel.error when the recipe is missing from the preset - the other groups stay live.
        public bool HasRecipeError { get; private set; }

        [ObservableProperty]
        private double beaconCount;

        [ObservableProperty]
        private double beaconsPerAssembler;

        [ObservableProperty]
        private double beaconsTotal;

        //Pure functions of the current assembler/recipe, not a one-way latch: this view model outlives
        //many assembler picks on the same node, so picking a reactor after a plain assembler shows the
        //neighbours stepper again, and so on.
        public bool ShowsNeighbours { get; private set; }
        public bool ShowsExtraProductivity { get; private set; }

        public RecipeSegmentViewModel(IRecipeNodeViewModel node, GraphViewer viewer, DataCache cache, Action? requestRedraw = null) {
            this.node = node;
            this.viewer = viewer;
            Cache = cache;
            this.requestRedraw = requestRedraw;
            if (viewer.Session.Editor.RequestNodeController(node.Id) is not RecipeNodeController requestedController)
                throw new InvalidOperationException("Recipe node has no controller.");
            controller = requestedController;
            Refresh();
        }

        //Re-reads every field from the node - called on construction, on NodeValuesUpdated, and after every
        //controller call this view model makes itself (assembler/fuel pick, rate toggle). suppressWrites
        //stops these assignments from round-tripping back through the controller.
        public void Refresh() {
            suppressWrites = true;
            IsFixedRate = node.RateType == RateType.Manual;
            AssemblerCount = IsFixedRate ? node.DesiredSetValue : node.ActualSetValue;
            LowPriority = node.LowPriority;
            IsKeyNode = node.KeyNode;
            KeyNodeTitle = node.KeyNodeTitle;
            AvgNeighbours = node.NeighbourCount;
            ExtraProductivityPercent = node.ExtraProductivity * 100;
            BeaconCount = node.BeaconCount;
            BeaconsPerAssembler = node.BeaconsPerAssembler;
            BeaconsTotal = node.BeaconsConst;
            RefreshComputed();
            suppressWrites = false;
        }

        private void RefreshComputed() {
            SelectedAssembler = BuildItem(node.SelectedAssembler.Assembler, node.SelectedAssembler.Icon, isSelected: true,
                isAvailable: node.SelectedAssembler.Assembler.Available && !node.SelectedAssembler.Assembler.IsMissing);
            OnPropertyChanged(nameof(SelectedAssembler));
            //Flattened onto their own properties: {Binding SelectedAssembler.Icon}/.Name log a [Binding]
            //warning on the nullable two-hop path (same fix as SelectedBeaconIcon below).
            SelectedAssemblerIcon = SelectedAssembler?.Icon;
            OnPropertyChanged(nameof(SelectedAssemblerIcon));
            SelectedAssemblerName = SelectedAssembler?.Name ?? "";
            OnPropertyChanged(nameof(SelectedAssemblerName));
            AssemblerStats = BuildStats();
            OnPropertyChanged(nameof(AssemblerStats));
            AssemblerOptions = BuildAssemblerOptions();
            OnPropertyChanged(nameof(AssemblerOptions));
            ShowsFuel = node.SelectedAssembler.Assembler.IsBurner;
            OnPropertyChanged(nameof(ShowsFuel));
            FuelOptions = ShowsFuel ? BuildFuelOptions() : [];
            OnPropertyChanged(nameof(FuelOptions));

            //See the declaration comment above for why this is a pure recompute, not a one-way latch.
            ShowsNeighbours = node.SelectedAssembler.Assembler.EntityType == EntityType.Reactor;
            OnPropertyChanged(nameof(ShowsNeighbours));

            ShowsExtraProductivity = node.BaseRecipe is not { Recipe: IRecipe productivityRecipe }
                || productivityRecipe.HasProductivityResearch
                || node.SelectedAssembler.Assembler.EntityType == EntityType.Miner
                || viewer.Graph.EnableExtraProductivityForNonMiners;
            OnPropertyChanged(nameof(ShowsExtraProductivity));

            HasRecipeError = node.BaseRecipe.Recipe?.IsMissing == true;
            OnPropertyChanged(nameof(HasRecipeError));

            Card = !HasRecipeError && node.BaseRecipe is { Recipe: IRecipe cardRecipe } ? RecipeCardViewModel.Build(cardRecipe, cardIconCache) : null;
            OnPropertyChanged(nameof(Card));
            OnPropertyChanged(nameof(HasCard));

            RefreshModules();
            RefreshBeacon();
            RefreshQuality();
        }

        //------------------------------------------------------------------------------------------------------Modules

        private void RefreshModules() {
            IAssembler assembler = node.SelectedAssembler.Assembler;
            ModulesTitle = $"Modules ({node.AssemblerModules.Count}/{assembler.ModuleSlots})";
            OnPropertyChanged(nameof(ModulesTitle));

            List<IModule> eligible = GetAssemblerModuleOptions();
            EquippedModules = [.. node.AssemblerModules.Select((pair, i) => BuildItem(pair.Module, pair.Icon, isSelected: false,
                isAvailable: !pair.Module.IsMissing && pair.Module.Available && pair.Module.Enabled && eligible.Contains(pair.Module) && i < assembler.ModuleSlots))];
            OnPropertyChanged(nameof(EquippedModules));

            ModuleOptions = [.. eligible.Select(module => BuildItem(module,
                new ModuleQualityPair(module, node.SelectedAssembler.Quality).Icon, isSelected: false,
                isAvailable: module.Available && !module.IsMissing))];
            OnPropertyChanged(nameof(ModuleOptions));
        }

        //Recipe's own assembler-module list intersected with what the current assembler actually accepts.
        private List<IModule> GetAssemblerModuleOptions() =>
            node.SelectedAssembler.Assembler.AllowModules && node.BaseRecipe is { Recipe: IRecipe recipe }
                ? [.. recipe.AssemblerModules.Intersect(node.SelectedAssembler.Assembler.Modules).Where(m => m.Enabled).OrderBy(m => m.LFriendlyName)]
                : [];

        public void AddModule(PickGridItem item) {
            if (item.Key is not IModule module || node.AssemblerModules.Count >= node.SelectedAssembler.Assembler.ModuleSlots)
                return;
            controller.AddAssemblerModule(new ModuleQualityPair(module, node.SelectedAssembler.Quality));
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        public void RemoveEquippedModule(PickGridItem item) {
            int index = IndexOf(EquippedModules, item);
            if (index < 0)
                return;
            controller.RemoveAssemblerModule(index);
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        //------------------------------------------------------------------------------------------------------Beacon

        private void RefreshBeacon() {
            BeaconQualityPair selected = node.SelectedBeacon;
            HasBeacon = selected;
            OnPropertyChanged(nameof(HasBeacon));
            BeaconName = selected.Beacon?.FriendlyName ?? "-none-";
            OnPropertyChanged(nameof(BeaconName));
            SelectedBeacon = selected.Beacon is IBeacon beacon
                ? BuildItem(beacon, selected.Icon, isSelected: true, isAvailable: beacon.Available && !beacon.IsMissing)
                : null;
            OnPropertyChanged(nameof(SelectedBeacon));
            //Flattened onto its own property: {Binding SelectedBeacon.Icon} logs a [Binding] warning every
            //time SelectedBeacon is null, which is the common no-beacon-selected state.
            SelectedBeaconIcon = SelectedBeacon?.Icon;
            OnPropertyChanged(nameof(SelectedBeaconIcon));

            BeaconOptions = BuildBeaconOptions();
            OnPropertyChanged(nameof(BeaconOptions));

            List<IModule> eligible = GetBeaconModuleOptions();
            int slots = selected.Beacon is IBeacon beaconForSlots ? beaconForSlots.ModuleSlots : 0;
            EquippedBeaconModules = [.. node.BeaconModules.Select((pair, i) => BuildItem(pair.Module, pair.Icon, isSelected: false,
                isAvailable: !pair.Module.IsMissing && pair.Module.Available && pair.Module.Enabled && eligible.Contains(pair.Module) && i < slots))];
            OnPropertyChanged(nameof(EquippedBeaconModules));

            BeaconModuleOptions = [.. eligible.Select(module => BuildItem(module,
                new ModuleQualityPair(module, node.SelectedAssembler.Quality).Icon, isSelected: false,
                isAvailable: module.Available && !module.IsMissing))];
            OnPropertyChanged(nameof(BeaconModuleOptions));
        }

        private IReadOnlyList<PickGridItem> BuildBeaconOptions() {
            if (node.BaseRecipe is not { Recipe: IRecipe recipe } || !node.SelectedAssembler.Assembler.AllowBeacons)
                return [];
            List<IModule> compatibleModules = [.. recipe.BeaconModules];
            return [.. Cache.Beacons.Values.Where(b => b.Enabled && compatibleModules.Any(m => b.Modules.Contains(m)))
                .Select(beacon => {
                    var pair = new BeaconQualityPair(beacon, node.SelectedAssembler.Quality);
                    bool isSelected = node.SelectedBeacon is { Beacon: IBeacon selectedBeacon }
                        && selectedBeacon == beacon && node.SelectedBeacon.Quality == node.SelectedAssembler.Quality;
                    return BuildItem(beacon, pair.Icon, isSelected, isAvailable: beacon.Available && !beacon.IsMissing);
                })];
        }

        //Recipe's own beacon-module list intersected with what the selected beacon actually accepts.
        private List<IModule> GetBeaconModuleOptions() =>
            node.SelectedAssembler.Assembler.AllowBeacons && node.SelectedBeacon.Beacon is IBeacon beacon && node.BaseRecipe is { Recipe: IRecipe recipe }
                ? [.. recipe.BeaconModules.Intersect(beacon.Modules).Where(m => m.Enabled).OrderBy(m => m.LFriendlyName)]
                : [];

        public void PickBeacon(PickGridItem item) {
            if (item.Key is not IBeacon beacon)
                return;
            controller.SetBeacon(new BeaconQualityPair(beacon, node.SelectedAssembler.Quality));
            viewer.Graph.UpdateNodeValues();
            Refresh();
            requestRedraw?.Invoke();
        }

        public void ClearBeacon() {
            controller.ClearBeacon();
            viewer.Graph.UpdateNodeValues();
            Refresh();
            requestRedraw?.Invoke();
        }

        public void AddBeaconModule(PickGridItem item) {
            if (item.Key is not IModule module || node.SelectedBeacon.Beacon is not IBeacon beacon || node.BeaconModules.Count >= beacon.ModuleSlots)
                return;
            controller.AddBeaconModule(new ModuleQualityPair(module, node.SelectedAssembler.Quality));
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        public void RemoveEquippedBeaconModule(PickGridItem item) {
            int index = IndexOf(EquippedBeaconModules, item);
            if (index < 0)
                return;
            controller.RemoveBeaconModule(index);
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        partial void OnBeaconCountChanged(double value) {
            if (suppressWrites || node.BeaconCount == value)
                return;
            controller.SetBeaconCount(value);
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        //Unlike OnBeaconCountChanged above, only the beacon count itself re-solves the graph.
        partial void OnBeaconsPerAssemblerChanged(double value) {
            if (suppressWrites || node.BeaconsPerAssembler == value)
                return;
            controller.SetBeaconsPerAssembler(value);
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        partial void OnBeaconsTotalChanged(double value) {
            if (suppressWrites || node.BeaconsConst == value)
                return;
            controller.SetBeaconsCont(value);
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        //------------------------------------------------------------------------------------------------------Quality

        private void RefreshQuality() {
            List<IQuality> qualities = [.. Cache.AvailableQualities.Where(q => q.Enabled)];
            ShowsQuality = qualities.Count > 0;
            OnPropertyChanged(nameof(ShowsQuality));
            QualityOptions = [.. qualities.Select(quality => BuildItem(quality, quality.Icon,
                isSelected: quality == node.SelectedAssembler.Quality, isAvailable: quality.Available && !quality.IsMissing))];
            OnPropertyChanged(nameof(QualityOptions));
            CurrentQualityName = node.SelectedAssembler.Quality.FriendlyName ?? "";
            OnPropertyChanged(nameof(CurrentQualityName));
            CurrentQualityIcon = node.SelectedAssembler.Quality.Icon;
            OnPropertyChanged(nameof(CurrentQualityIcon));
        }

        //Re-qualities the node's current assembler in place and writes straight through, rather than only
        //setting a default for the next pick.
        public void PickQuality(PickGridItem item) {
            if (item.Key is not IQuality quality || quality == node.SelectedAssembler.Quality)
                return;
            controller.SetAssembler(new AssemblerQualityPair(node.SelectedAssembler.Assembler, quality));
            viewer.Graph.DefaultAssemblerQuality = quality;
            viewer.Graph.UpdateNodeValues();
            Refresh();
            requestRedraw?.Invoke();
        }

        private static int IndexOf(IReadOnlyList<PickGridItem> items, PickGridItem item) {
            for (int i = 0; i < items.Count; i++)
                if (items[i] == item)
                    return i;
            return -1;
        }

        //Switching to Fixed pushes whatever's currently displayed (the solver's own ActualSetValue while the
        //node was Auto) into DesiredSetValue, so the node doesn't jump to a stale value nobody sees on screen.
        partial void OnIsFixedRateChanged(bool value) {
            if (suppressWrites)
                return;
            if (value && node.DesiredSetValue != AssemblerCount)
                controller.SetDesiredSetValue(AssemblerCount);
            RateType updated = value ? RateType.Manual : RateType.Auto;
            if (node.RateType != updated)
                controller.SetRateType(updated);
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        partial void OnAssemblerCountChanged(double value) {
            if (suppressWrites || node.DesiredSetValue == value)
                return;
            controller.SetDesiredSetValue(value);
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        partial void OnLowPriorityChanged(bool value) {
            if (suppressWrites)
                return;
            controller.SetPriority(value);
            viewer.Graph.UpdateNodeValues();
            requestRedraw?.Invoke();
        }

        partial void OnIsKeyNodeChanged(bool value) {
            if (suppressWrites)
                return;
            controller.SetKeyNode(value);
            suppressWrites = true;
            KeyNodeTitle = node.KeyNodeTitle;
            suppressWrites = false;
            requestRedraw?.Invoke();
        }

        partial void OnKeyNodeTitleChanged(string value) {
            if (suppressWrites)
                return;
            controller.SetKeyNodeTitle(value ?? "");
            requestRedraw?.Invoke();
        }

        partial void OnAvgNeighboursChanged(double value) {
            if (suppressWrites || node.NeighbourCount == value)
                return;
            controller.SetNeighbourCount(value);
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        partial void OnExtraProductivityPercentChanged(double value) {
            double bonus = value / 100;
            if (suppressWrites || node.ExtraProductivity == bonus)
                return;
            controller.SetExtraProductivityBonus(bonus);
            viewer.Graph.UpdateNodeValues();
            RefreshComputed();
            requestRedraw?.Invoke();
        }

        public void PickAssembler(PickGridItem item) {
            if (item.Key is not IAssembler assembler || assembler == node.SelectedAssembler.Assembler)
                return;
            controller.SetAssembler(new AssemblerQualityPair(assembler, node.SelectedAssembler.Quality));
            viewer.Graph.UpdateNodeValues();
            Refresh();
            requestRedraw?.Invoke();
        }

        public void PickFuel(PickGridItem item) {
            if (item.Key is not IItem fuel)
                return;
            controller.SetFuel(fuel);
            viewer.Graph.UpdateNodeValues();
            Refresh();
            requestRedraw?.Invoke();
        }

        private IReadOnlyList<PickGridItem> BuildAssemblerOptions() {
            if (node.BaseRecipe is not { Recipe: IRecipe recipe })
                return [];
            IQuality quality = node.SelectedAssembler.Quality;
            IAssembler selected = node.SelectedAssembler.Assembler;
            return [.. recipe.Assemblers.Where(a => a.Enabled).Select(assembler => BuildItem(
                assembler, new AssemblerQualityPair(assembler, quality).Icon,
                isSelected: assembler == selected,
                isAvailable: assembler.Available && !assembler.IsMissing))];
        }

        private IReadOnlyList<PickGridItem> BuildFuelOptions() {
            IAssembler assembler = node.SelectedAssembler.Assembler;
            IItem? selected = node.Fuel;
            return [.. assembler.Fuels
                .Where(f => f.ProductionRecipes.Any(r => r.Enabled && r.Assemblers.Any(a => a.Enabled)))
                .Select(fuel => BuildItem(fuel, fuel.Icon, isSelected: fuel == selected,
                    isAvailable: fuel.Available && !fuel.IsMissing && fuel.ProductionRecipes.Any(r => r.Available && r.Assemblers.Any(a => a.Available))))];
        }

        private PickGridItem BuildItem(IDataObjectBase dataObject, SKBitmap? icon, bool isSelected, bool isAvailable) =>
            new(dataObject, dataObject.FriendlyName, "", icon is null ? null : IconBaker.GetOrBake(iconCache, icon, PickIconSize), isSelected, isAvailable);

        private string BuildStats() {
            EntityType entityType = node.SelectedAssembler.Assembler.EntityType;
            if (entityType == EntityType.Generator)
                return BuildGeneratorStats();
            if (entityType is not (EntityType.Assembler or EntityType.Miner or EntityType.OffshorePump))
                return ""; //Reactor and anything else outside the assembler stat set: no Speed/Prod/Pollution readout

            string speed = node.GetSpeedMultiplier().ToString("P0", DisplayCulture.Format);
            string productivity = node.GetProductivityMultiplier().ToString("P0", DisplayCulture.Format);
            string pollution = (node.GetAssemblerPollutionProduction() * 60).ToString("0.##", DisplayCulture.Format);
            return $"Speed {speed} · Prod {productivity} · Pollution {pollution}/m";
        }

        //Temperature range, effectivity, and electrical production for a Generator-type assembler
        //(RecipeNode.Display.cs's GetGeneratorMinimumTemperature/GetGeneratorMaximumTemperature/
        //GetGeneratorEffectivity/GetGeneratorElectricalProduction).
        private string BuildGeneratorStats() {
            double minTemp = node.GetGeneratorMinimumTemperature();
            double maxTemp = node.GetGeneratorMaximumTemperature();
            double operationalTemp = node.SelectedAssembler.Assembler.OperationTemperature;
            double effectivity = node.GetGeneratorEffectivity();

            string range = double.IsInfinity(maxTemp)
                ? $"min {Math.Round(minTemp, 1).ToString("0.#", DisplayCulture.Format)}°c  (optimal: {Math.Round(operationalTemp, 1).ToString("0.#", DisplayCulture.Format)}°c)"
                : $"{Math.Round(minTemp, 1).ToString("0.#", DisplayCulture.Format)}-{Math.Round(maxTemp, 1).ToString("0.#", DisplayCulture.Format)}°c  (optimal: {Math.Round(operationalTemp, 1).ToString("0.#", DisplayCulture.Format)}°c)";
            string production = GraphicsStuff.DoubleToEnergy(node.GetGeneratorElectricalProduction(), "W");
            return $"{range} · Effectivity {effectivity.ToString("P0", DisplayCulture.Format)} · {production}";
        }
    }
}
