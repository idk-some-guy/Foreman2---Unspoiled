using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Views;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    public sealed partial class FlowSegmentViewModel {
        private const int TowerPickIconSize = 28;

        private IPlantNodeViewModel? plant;
        private PlantNodeController? plantController;
        private DataCache? cache;
        private readonly Dictionary<SKBitmap, Bitmap> towerIconCache = [];

        internal event EventHandler? TowerChanged;

        public bool ShowsTower { get; private set; }
        public bool ShowsTowerQuality { get; private set; }
        public bool ShowsTowerModules { get; private set; }
        public bool TowerIsMissing { get; private set; }
        public string TowerModulesTitle { get; private set; } = "";
        public int TowerModuleSlotCount { get; private set; }
        public bool HasTowerModuleOptions { get; private set; }
        public string? TowerModuleOptionsNote { get; private set; }
        public string TowerStats { get; private set; } = "";
        public PickGridItem? SelectedTower { get; private set; }
        public IReadOnlyList<PickGridItem> TowerOptions { get; private set; } = [];
        public IReadOnlyList<PickGridItem> EquippedTowerModules { get; private set; } = [];
        public IReadOnlyList<PickGridItem> TowerModuleOptions { get; private set; } = [];
        public IReadOnlyList<PickGridItem> TowerQualityOptions { get; private set; } = [];
        public string CurrentTowerQualityName { get; private set; } = "";
        public SKBitmap? CurrentTowerQualityIcon { get; private set; }

        private void InitTower() {
            plant = node as IPlantNodeViewModel;
            plantController = controller as PlantNodeController;
            cache = viewer.Context.DCache;
        }

        private void RefreshTower() {
            if (plant is null || plantController is null)
                return;

            AssemblerQualityPair? tower = plant.Tower;
            List<IQuality> qualities = cache is null ? [] : [.. cache.AvailableQualities.Where(q => q.Enabled)];
            IReadOnlyList<IAssembler> offered = cache is null ? [] : PlantTowerRules.TowerOptions(cache);
            IQuality? newQuality = tower?.Quality is { IsMissing: false } current ? current : cache is null ? null : NewTowerQuality();

            ShowsTower = offered.Count > 0 || tower is not null;
            TowerIsMissing = tower is AssemblerQualityPair missingCheck && (missingCheck.Assembler.IsMissing || missingCheck.Quality.IsMissing);
            TowerOptions = [.. offered.Select(t => BuildTowerItem(t, new AssemblerQualityPair(t, newQuality!).Icon,
                isSelected: tower?.Assembler == t, isAvailable: t.Available && !t.IsMissing))];
            SelectedTower = tower is AssemblerQualityPair selected
                ? BuildTowerItem(selected.Assembler, selected.Icon, isSelected: true,
                    isAvailable: selected.Assembler.Available && !selected.Assembler.IsMissing, isMissing: selected.Assembler.IsMissing)
                : null;
            int slots = tower?.Assembler.ModuleSlots ?? 0;
            TowerStats = slots == 1 ? "1 module slot" : $"{slots} module slots";

            ShowsTowerQuality = tower is not null && !TowerIsMissing && qualities.Count > 1;
            TowerQualityOptions = [.. qualities.Select(q => BuildTowerItem(q, q.Icon, isSelected: q == tower?.Quality, isAvailable: q.Available && !q.IsMissing))];
            CurrentTowerQualityName = tower?.Quality.FriendlyName ?? "";
            CurrentTowerQualityIcon = tower?.Quality.Icon;

            ShowsTowerModules = tower is not null && slots > 0;
            TowerModuleSlotCount = slots;
            TowerModulesTitle = $"Modules ({plant.TowerModules.Count}/{slots})";
            if (tower is AssemblerQualityPair equippedOn) {
                IReadOnlyList<IModule> options = TowerIsMissing ? [] : PlantTowerRules.ModuleOptions(equippedOn.Assembler, equippedOn.Quality);
                EquippedTowerModules = [.. plant.TowerModules.Select((pair, i) => BuildTowerItem(pair.Module, pair.Icon, isSelected: false,
                    isAvailable: !pair.Module.IsMissing && pair.Module.Available && pair.Module.Enabled && equippedOn.Assembler.Modules.Contains(pair.Module) && i < slots))];
                TowerModuleOptions = [.. options.Select(m => BuildTowerItem(m, new ModuleQualityPair(m, equippedOn.Quality).Icon, isSelected: false,
                    isAvailable: m.Available && !m.IsMissing))];
                TowerModuleOptionsNote = !TowerIsMissing && PlantTowerRules.HasHiddenModules(equippedOn.Assembler, equippedOn.Quality)
                    ? "Only productivity changes the harvest." : null;
            } else {
                EquippedTowerModules = [];
                TowerModuleOptions = [];
                TowerModuleOptionsNote = null;
            }
            HasTowerModuleOptions = TowerModuleOptions.Count > 0;

            foreach (string name in new[] { nameof(ShowsTower), nameof(ShowsTowerQuality), nameof(ShowsTowerModules), nameof(TowerIsMissing), nameof(TowerModulesTitle),
                nameof(TowerModuleSlotCount), nameof(HasTowerModuleOptions), nameof(TowerModuleOptionsNote), nameof(TowerStats) })
                OnPropertyChanged(name);
            TowerChanged?.Invoke(this, EventArgs.Empty);
        }

        private PickGridItem BuildTowerItem(IDataObjectBase dataObject, SKBitmap? icon, bool isSelected, bool isAvailable, bool isMissing = false) =>
            new(dataObject, dataObject.FriendlyName, "", icon is null ? null : IconBaker.GetOrBake(towerIconCache, icon, TowerPickIconSize), isSelected, isAvailable, isMissing);

        private IQuality NewTowerQuality() =>
            viewer.Graph.DefaultAssemblerQuality is { Enabled: true } preferred ? preferred : RecipeQualityLock.LowestEnabled(cache!);

        private void ApplyTower(AssemblerQualityPair? tower) {
            plantController?.SetTower(tower);
            viewer.Graph.UpdateNodeValues();
            RefreshTower();
            requestRedraw?.Invoke();
        }

        public void PickTower(PickGridItem item) {
            if (item.Key is not IAssembler assembler || plant is null)
                return;
            IQuality quality = plant.Tower?.Quality is { IsMissing: false } kept ? kept : NewTowerQuality();
            ApplyTower(new AssemblerQualityPair(assembler, quality));
        }

        public void ClearTower() => ApplyTower(null);

        public void PickTowerQuality(PickGridItem item) {
            if (item.Key is not IQuality quality || plant?.Tower is not AssemblerQualityPair tower || quality == tower.Quality)
                return;
            ApplyTower(new AssemblerQualityPair(tower.Assembler, quality));
        }

        public void EquipTowerModuleAt(PickGridItem item, int equippedSlotIndex) {
            if (item.Key is not IModule module || plant?.Tower is not AssemblerQualityPair tower || plantController is null || tower.Assembler.ModuleSlots == 0)
                return;
            var pair = new ModuleQualityPair(module, tower.Quality);
            if (equippedSlotIndex >= 0 && equippedSlotIndex < plant.TowerModules.Count)
                plantController.ReplaceTowerModule(equippedSlotIndex, pair);
            else if (plant.TowerModules.Count < tower.Assembler.ModuleSlots)
                plantController.AddTowerModule(pair);
            else
                return;
            viewer.Graph.UpdateNodeValues();
            RefreshTower();
            requestRedraw?.Invoke();
        }

        public void RemoveEquippedTowerModule(int index) {
            if (index < 0 || index >= EquippedTowerModules.Count || plantController is null)
                return;
            plantController.RemoveTowerModule(index);
            viewer.Graph.UpdateNodeValues();
            RefreshTower();
            requestRedraw?.Invoke();
        }
    }
}
