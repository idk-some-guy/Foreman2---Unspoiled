using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    //Backs RecipeSegmentView (Task 5, part 1 - Rate/Assembler/Fuel; Modules/Beacon/quality land in Task 6):
    //the recipe node editor, ported from EditRecipePanel onto the docked inspector. Setters call the same
    //RecipeNodeController methods EditRecipePanel called directly - suppressWrites keeps Refresh() (reading
    //node state back into these properties) from bouncing those same writes back out at the node. Task 5
    //has no quality picker yet, so every AssemblerQualityPair built here carries the node's own current
    //quality forward rather than reading one back from a selector.
    public sealed partial class RecipeSegmentViewModel : ObservableObject {
        private const int PickIconSize = 28;

        private readonly IRecipeNodeViewModel node;
        private readonly GraphViewer viewer;
        private readonly RecipeNodeController controller;

        //Carried for Task 6 (quality/module/beacon groups on this same class); unread by Task 5.
        private DataCache Cache { get; }
        private AppSettings Settings { get; }
        private readonly Action? requestRedraw;
        private readonly Dictionary<SKBitmap, Bitmap> iconCache = [];
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
        public string AssemblerStats { get; private set; } = "";
        public IReadOnlyList<PickGridItem> AssemblerOptions { get; private set; } = [];
        public bool ShowsFuel { get; private set; }
        public IReadOnlyList<PickGridItem> FuelOptions { get; private set; } = [];

        //Same conditions as EditRecipePanel.cs's UpdateAssembler (~L646-654), minus its one-way latch:
        //that panel gets rebuilt on every open, so hiding a field for good the moment it's ever
        //disqualified is harmless there. This view model outlives many assembler picks on the same
        //node, so both properties stay pure functions of the current assembler/recipe instead -
        //picking a reactor after a plain assembler shows the neighbours stepper again, and so on.
        public bool ShowsNeighbours { get; private set; }
        public bool ShowsExtraProductivity { get; private set; }

        public RecipeSegmentViewModel(IRecipeNodeViewModel node, GraphViewer viewer, DataCache cache, AppSettings settings, Action? requestRedraw = null) {
            this.node = node;
            this.viewer = viewer;
            Cache = cache;
            Settings = settings;
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
            RefreshComputed();
            suppressWrites = false;
        }

        private void RefreshComputed() {
            SelectedAssembler = BuildItem(node.SelectedAssembler.Assembler, node.SelectedAssembler.Icon, isSelected: true,
                isAvailable: node.SelectedAssembler.Assembler.Available && !node.SelectedAssembler.Assembler.IsMissing);
            OnPropertyChanged(nameof(SelectedAssembler));
            AssemblerStats = BuildStats();
            OnPropertyChanged(nameof(AssemblerStats));
            AssemblerOptions = BuildAssemblerOptions();
            OnPropertyChanged(nameof(AssemblerOptions));
            ShowsFuel = node.SelectedAssembler.Assembler.IsBurner;
            OnPropertyChanged(nameof(ShowsFuel));
            FuelOptions = ShowsFuel ? BuildFuelOptions() : [];
            OnPropertyChanged(nameof(FuelOptions));

            //EditRecipePanel.cs L646-654's own conditions, negated (see the declaration comment above
            //for why this is a pure recompute rather than that panel's one-way latch).
            ShowsNeighbours = node.SelectedAssembler.Assembler.EntityType == EntityType.Reactor;
            OnPropertyChanged(nameof(ShowsNeighbours));

            ShowsExtraProductivity = node.BaseRecipe is not { Recipe: IRecipe productivityRecipe }
                || productivityRecipe.HasProductivityResearch
                || node.SelectedAssembler.Assembler.EntityType == EntityType.Miner
                || viewer.Graph.EnableExtraProductivityForNonMiners;
            OnPropertyChanged(nameof(ShowsExtraProductivity));
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
            if (item.Key is not IAssembler assembler)
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
            string speed = node.GetSpeedMultiplier().ToString("P0", DisplayCulture.Format);
            string productivity = node.GetProductivityMultiplier().ToString("P0", DisplayCulture.Format);
            string pollution = (node.GetAssemblerPollutionProduction() * 60).ToString("0.##", DisplayCulture.Format);
            return $"Speed {speed} · Prod {productivity} · Pollution {pollution}/m";
        }
    }
}
