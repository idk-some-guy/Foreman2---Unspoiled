using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using System;
using System.ComponentModel;
using KitPickGrid = Foreman.Mac.Kit.Composites.PickGrid;

namespace Foreman.Mac.Views.Inspector {
    //The inspector's Rate/Assembler/Fuel/Modules/Beacon groups for a recipe node. RateMode isn't bound in
    //XAML - Kit.Segmented has no data-bindable SelectedItem, so its SelectedIndex is synced by hand both
    //ways here, same pattern as FlowSegmentView. Assembler/Fuel/Beacon-pick stay on the app's own PickGrid
    //(a designer ruling on 2026-09-17 defers their move to a Kit.PickGrid choice mode that doesn't exist
    //yet); module and beacon-module slots are Kit.PickGrid (aliased KitPickGrid here to disambiguate from
    //the app's own PickGrid, which lives in this same namespace).
    public partial class RecipeSegmentView : UserControl {
        private readonly Segmented rateMode;
        private readonly PickGrid assemblerGrid;
        private readonly PickGrid fuelGrid;
        private readonly PickGrid beaconOptionsGrid;
        private readonly KitPickGrid equippedGrid;
        private readonly KitPickGrid moduleOptionsGrid;
        private readonly KitPickGrid equippedBeaconGrid;
        private readonly KitPickGrid beaconModuleOptionsGrid;
        private readonly Button clearBeaconButton;
        private readonly InspectorGroup beaconGroup;
        private readonly StatePanel recipeErrorPanel;
        private GraphViewer? boundViewer;
        private bool lastHasBeacon;

        public RecipeSegmentViewModel? ViewModel { get; private set; }

        //A25 .error's two buttons - the host (InspectorView) fills these in, same seam shape as EmptyState's AddRecipeCommand.
        public event EventHandler? OpenPresetsRequested;
        public event EventHandler<IRecipeNodeViewModel>? PickAnotherRecipeRequested;

        public RecipeSegmentView() {
            InitializeComponent();
            rateMode = this.FindControl<Segmented>("RateMode")!;
            rateMode.Items.Add("Auto");
            rateMode.Items.Add("Fixed");
            assemblerGrid = this.FindControl<PickGrid>("AssemblerGrid")!;
            fuelGrid = this.FindControl<PickGrid>("FuelGrid")!;
            beaconOptionsGrid = this.FindControl<PickGrid>("BeaconOptions")!;
            equippedGrid = this.FindControl<KitPickGrid>("EquippedGrid")!;
            moduleOptionsGrid = this.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            equippedBeaconGrid = this.FindControl<KitPickGrid>("EquippedBeaconGrid")!;
            beaconModuleOptionsGrid = this.FindControl<KitPickGrid>("BeaconModuleOptionsGrid")!;
            clearBeaconButton = this.FindControl<Button>("ClearBeaconButton")!;
            beaconGroup = this.FindControl<InspectorGroup>("BeaconGroup")!;
            recipeErrorPanel = this.FindControl<StatePanel>("RecipeErrorPanel")!;
            var openPresetsButton = new Button { Content = "Open Presets…", Theme = KitTheme("Kit.Button") };
            ButtonRow.SetRole(openPresetsButton, ButtonRowRole.Other);
            openPresetsButton.Click += (_, _) => OpenPresetsRequested?.Invoke(this, EventArgs.Empty);
            var pickAnotherRecipeButton = new Button { Content = "Pick Another Recipe…", Theme = KitTheme("Kit.Button") };
            ButtonRow.SetRole(pickAnotherRecipeButton, ButtonRowRole.Default);
            pickAnotherRecipeButton.Click += (_, _) => { if (ViewModel is not null) PickAnotherRecipeRequested?.Invoke(this, ViewModel.Node); };
            recipeErrorPanel.Buttons = new ButtonRow { Theme = KitTheme("Kit.ButtonRow"), Items = { openPresetsButton, pickAnotherRecipeButton } };

            rateMode.PropertyChanged += RateMode_PropertyChanged;
            assemblerGrid.ItemActivated += (_, item) => ViewModel?.PickAssembler(item);
            fuelGrid.ItemActivated += (_, item) => ViewModel?.PickFuel(item);
            beaconOptionsGrid.ItemActivated += (_, item) => ViewModel?.PickBeacon(item);

            equippedGrid.ChooseRequested += (_, _) => moduleOptionsGrid.Focus();
            equippedGrid.Cleared += EquippedGrid_Cleared;
            moduleOptionsGrid.ChooseRequested += ModuleOptionsGrid_ChooseRequested;
            moduleOptionsGrid.CellClicked += ModuleOptionsGrid_CellClicked;
            equippedBeaconGrid.ChooseRequested += (_, _) => beaconModuleOptionsGrid.Focus();
            equippedBeaconGrid.Cleared += EquippedBeaconGrid_Cleared;
            beaconModuleOptionsGrid.ChooseRequested += BeaconModuleOptionsGrid_ChooseRequested;
            beaconModuleOptionsGrid.CellClicked += BeaconModuleOptionsGrid_CellClicked;

            clearBeaconButton.Click += (_, _) => ViewModel?.ClearBeacon();
            DetachedFromVisualTree += (_, _) => Detach();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal void Bind(IRecipeNodeViewModel node, GraphViewer viewer, DataCache cache, Action? requestRedraw = null) {
            Detach();
            boundViewer = viewer;
            ViewModel = new RecipeSegmentViewModel(node, viewer, cache, requestRedraw);
            DataContext = ViewModel;
            rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
            lastHasBeacon = ViewModel.HasBeacon;
            beaconGroup.IsExpanded = lastHasBeacon;
            //Every grid is re-pointed at a different node below, so nothing it held for the old one may
            //survive: a stale SelectedIndex would aim the next option click at a slot never picked here.
            equippedGrid.SelectedIndex = -1;
            moduleOptionsGrid.SelectedIndex = -1;
            equippedBeaconGrid.SelectedIndex = -1;
            beaconModuleOptionsGrid.SelectedIndex = -1;
            SyncSlots(equippedGrid, ViewModel.EquippedModules, ViewModel.ModuleSlotCount);
            SyncSlots(moduleOptionsGrid, ViewModel.ModuleOptions, ViewModel.ModuleOptions.Count);
            SyncSlots(equippedBeaconGrid, ViewModel.EquippedBeaconModules, ViewModel.BeaconModuleSlotCount);
            SyncSlots(beaconModuleOptionsGrid, ViewModel.BeaconModuleOptions, ViewModel.BeaconModuleOptions.Count);
            ViewModel.PropertyChanged += OnRecipeViewModelPropertyChanged;
            viewer.Session.NodeValuesUpdated += OnNodeValuesUpdated;
        }

        internal void Refresh() {
            ViewModel?.Refresh();
            if (ViewModel is not null)
                rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
        }

        private void RateMode_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == Segmented.SelectedIndexProperty && ViewModel is not null)
                ViewModel.IsFixedRate = rateMode.SelectedIndex == 1;
        }

        private void EquippedGrid_Cleared(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.EquippedModules.Count)
                return;
            ViewModel.RemoveEquippedModule(ViewModel.EquippedModules[index]);
        }

        private void ModuleOptionsGrid_ChooseRequested(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.ModuleOptions.Count)
                return;
            ViewModel.AddModule(ViewModel.ModuleOptions[index]);
        }

        //A click on an option cell equips it into whatever's selected in the equipped grid - a filled
        //slot is replaced, an empty one behaves like a plain append.
        private void ModuleOptionsGrid_CellClicked(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.ModuleOptions.Count)
                return;
            ViewModel.EquipModuleAt(ViewModel.ModuleOptions[index], equippedGrid.SelectedIndex);
        }

        private void EquippedBeaconGrid_Cleared(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.EquippedBeaconModules.Count)
                return;
            ViewModel.RemoveEquippedBeaconModule(ViewModel.EquippedBeaconModules[index]);
        }

        private void BeaconModuleOptionsGrid_ChooseRequested(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.BeaconModuleOptions.Count)
                return;
            ViewModel.AddBeaconModule(ViewModel.BeaconModuleOptions[index]);
        }

        private void BeaconModuleOptionsGrid_CellClicked(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.BeaconModuleOptions.Count)
                return;
            ViewModel.EquipBeaconModuleAt(ViewModel.BeaconModuleOptions[index], equippedBeaconGrid.SelectedIndex);
        }

        //HasBeacon flipping is the only trigger for a code-driven IsExpanded change - a plain re-solve with
        //the beacon unchanged must not re-collapse or re-expand a group the user set by hand (fix round: a
        //live {Binding HasBeacon} used to re-push on every RefreshComputed, undoing a manual collapse).
        private void OnRecipeViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (ViewModel is not null) {
                SyncSlots(equippedGrid, ViewModel.EquippedModules, ViewModel.ModuleSlotCount);
                SyncSlots(moduleOptionsGrid, ViewModel.ModuleOptions, ViewModel.ModuleOptions.Count);
                SyncSlots(equippedBeaconGrid, ViewModel.EquippedBeaconModules, ViewModel.BeaconModuleSlotCount);
                SyncSlots(beaconModuleOptionsGrid, ViewModel.BeaconModuleOptions, ViewModel.BeaconModuleOptions.Count);
            }
            if (e.PropertyName != nameof(RecipeSegmentViewModel.HasBeacon) || ViewModel is null || ViewModel.HasBeacon == lastHasBeacon)
                return;
            lastHasBeacon = ViewModel.HasBeacon;
            beaconGroup.IsExpanded = lastHasBeacon;
        }

        //Diffs by index against whatever's already in the grid instead of Slots.Clear() + rebuild - a
        //solve that leaves every slot's content unchanged writes nothing at all, and one that changes a
        //single slot replaces only that entry (PickGrid updates the one PickCell in place for a Replace).
        private static void SyncSlots(KitPickGrid grid, System.Collections.Generic.IReadOnlyList<PickGridItem> items, int slotCount) {
            int total = Math.Max(slotCount, items.Count);
            for (int i = 0; i < total; i++) {
                PickSlot desired = i < items.Count ? BuildSlot(items[i]) : new PickSlot { IsEmpty = true };
                if (i < grid.Slots.Count) {
                    if (!SlotsEqual(grid.Slots[i], desired))
                        grid.Slots[i] = desired;
                } else {
                    grid.Slots.Add(desired);
                }
            }
            while (grid.Slots.Count > total)
                grid.Slots.RemoveAt(grid.Slots.Count - 1);
        }

        private static PickSlot BuildSlot(PickGridItem item) =>
            new() { Icon = item.Icon, Name = item.Name, IsMissing = !item.IsAvailable };

        private static bool SlotsEqual(PickSlot a, PickSlot b) =>
            a.Icon == b.Icon && a.Name == b.Name && a.IsEmpty == b.IsEmpty && a.IsMissing == b.IsMissing && a.IsEnabled == b.IsEnabled;

        private void OnNodeValuesUpdated(object? sender, EventArgs e) => Refresh();

        private void Detach() {
            if (boundViewer is not null)
                boundViewer.Session.NodeValuesUpdated -= OnNodeValuesUpdated;
            boundViewer = null;
            if (ViewModel is not null)
                ViewModel.PropertyChanged -= OnRecipeViewModelPropertyChanged;
        }

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"RecipeSegmentView: kit theme '{key}' not found.");
        }
    }
}
