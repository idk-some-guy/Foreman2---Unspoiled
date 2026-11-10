using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using Foreman.Mac;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SkiaSharp;
using KitPickGrid = Foreman.Mac.Kit.Composites.PickGrid;

namespace Foreman.Mac.Views.Inspector {
    public partial class RecipeSegmentView : UserControl {
        private readonly Segmented rateMode;
        private readonly PopUp rateUnitPopUp;
        private readonly KitPickGrid assemblerChoiceGrid;
        private readonly StackPanel assemblerValueLine;
        private readonly Image assemblerValueLineIcon;
        private readonly TextBlock assemblerValueLineText;
        private readonly PopUp assemblerFallbackPopUp;
        private readonly FormRow assemblerRow;
        private readonly KitPickGrid fuelChoiceGrid;
        private readonly StackPanel fuelValueLine;
        private readonly Image fuelValueLineIcon;
        private readonly TextBlock fuelValueLineText;
        private readonly PopUp fuelFallbackPopUp;
        private readonly FormRow fuelRow;
        private readonly KitPickGrid beaconChoiceGrid;
        private readonly StackPanel beaconValueLine;
        private readonly Image beaconValueLineIcon;
        private readonly TextBlock beaconValueLineText;
        private readonly PopUp beaconFallbackPopUp;
        private readonly FormRow beaconRow;
        private readonly KitPickGrid equippedGrid;
        private readonly KitPickGrid moduleOptionsGrid;
        private readonly KitPickGrid equippedBeaconGrid;
        private readonly KitPickGrid beaconModuleOptionsGrid;
        private readonly InspectorGroup beaconGroup;
        private readonly StatePanel recipeErrorPanel;
        private readonly Image recipeQualityIcon;
        private readonly Button switchQualityButton;
        private readonly Dictionary<SKBitmap, Bitmap> qualityIconCache = [];
        private GraphViewer? boundViewer;
        private bool lastHasBeacon;

        public RecipeSegmentViewModel? ViewModel { get; private set; }

        public event EventHandler? OpenPresetsRequested;
        public event EventHandler<IRecipeNodeViewModel>? PickAnotherRecipeRequested;

        public event EventHandler? QualitySwitched;

        public event EventHandler<ProductionGraph.RateUnit>? RateUnitChanged;

        public RecipeSegmentView() {
            InitializeComponent();
            rateMode = this.FindControl<Segmented>("RateMode")!;
            rateMode.Items.Add("Auto");
            rateMode.Items.Add("Fixed");
            rateUnitPopUp = this.FindControl<PopUp>("RateUnitPopUp")!;
            foreach (string option in MainWindow.RateUnitOptions)
                rateUnitPopUp.Items.Add(option);

            assemblerRow = this.FindControl<FormRow>("AssemblerRow")!;
            assemblerChoiceGrid = this.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            assemblerValueLine = this.FindControl<StackPanel>("AssemblerValueLine")!;
            assemblerValueLineIcon = this.FindControl<Image>("AssemblerValueLineIcon")!;
            assemblerValueLineText = this.FindControl<TextBlock>("AssemblerValueLineText")!;
            assemblerFallbackPopUp = this.FindControl<PopUp>("AssemblerFallbackPopUp")!;

            fuelRow = this.FindControl<FormRow>("FuelRow")!;
            fuelChoiceGrid = this.FindControl<KitPickGrid>("FuelChoiceGrid")!;
            fuelValueLine = this.FindControl<StackPanel>("FuelValueLine")!;
            fuelValueLineIcon = this.FindControl<Image>("FuelValueLineIcon")!;
            fuelValueLineText = this.FindControl<TextBlock>("FuelValueLineText")!;
            fuelFallbackPopUp = this.FindControl<PopUp>("FuelFallbackPopUp")!;

            beaconRow = this.FindControl<FormRow>("BeaconRow")!;
            beaconChoiceGrid = this.FindControl<KitPickGrid>("BeaconChoiceGrid")!;
            beaconValueLine = this.FindControl<StackPanel>("BeaconValueLine")!;
            beaconValueLineIcon = this.FindControl<Image>("BeaconValueLineIcon")!;
            beaconValueLineText = this.FindControl<TextBlock>("BeaconValueLineText")!;
            beaconFallbackPopUp = this.FindControl<PopUp>("BeaconFallbackPopUp")!;

            equippedGrid = this.FindControl<KitPickGrid>("EquippedGrid")!;
            moduleOptionsGrid = this.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            equippedBeaconGrid = this.FindControl<KitPickGrid>("EquippedBeaconGrid")!;
            beaconModuleOptionsGrid = this.FindControl<KitPickGrid>("BeaconModuleOptionsGrid")!;
            beaconGroup = this.FindControl<InspectorGroup>("BeaconGroup")!;
            recipeErrorPanel = this.FindControl<StatePanel>("RecipeErrorPanel")!;
            recipeQualityIcon = this.FindControl<Image>("RecipeQualityIcon")!;
            switchQualityButton = this.FindControl<Button>("SwitchQualityButton")!;
            switchQualityButton.Click += (_, _) => {
                ViewModel?.SwitchToLowestQuality();
                QualitySwitched?.Invoke(this, EventArgs.Empty);
            };
            var openPresetsButton = new Button { Content = "Open Presets…", Theme = KitTheme("Kit.Button") };
            ButtonRow.SetRole(openPresetsButton, ButtonRowRole.Other);
            openPresetsButton.Click += (_, _) => OpenPresetsRequested?.Invoke(this, EventArgs.Empty);
            var pickAnotherRecipeButton = new Button { Content = "Pick Another Recipe…", Theme = KitTheme("Kit.Button") };
            ButtonRow.SetRole(pickAnotherRecipeButton, ButtonRowRole.Default);
            pickAnotherRecipeButton.Click += (_, _) => { if (ViewModel is not null) PickAnotherRecipeRequested?.Invoke(this, ViewModel.Node); };
            recipeErrorPanel.Buttons = new ButtonRow { Theme = KitTheme("Kit.ButtonRow"), Items = { openPresetsButton, pickAnotherRecipeButton } };
            rateMode.PropertyChanged += RateMode_PropertyChanged;
            rateUnitPopUp.Command = new RelayCommand<string>(PickRateUnitByName);

            assemblerChoiceGrid.ChooseRequested += (_, index) => {
                if (ViewModel is null || index < 0 || index >= ViewModel.AssemblerOptions.Count)
                    return;
                ViewModel.PickAssembler(ViewModel.AssemblerOptions[index]);
            };
            assemblerFallbackPopUp.Command = new RelayCommand<string>(name => {
                PickGridItem? item = ViewModel?.AssemblerOptions.FirstOrDefault(i => i.Name == name);
                if (item is not null)
                    ViewModel?.PickAssembler(item);
            });

            fuelChoiceGrid.ChooseRequested += (_, index) => {
                if (ViewModel is null || index < 0 || index >= ViewModel.FuelOptions.Count)
                    return;
                ViewModel.PickFuel(ViewModel.FuelOptions[index]);
            };
            fuelFallbackPopUp.Command = new RelayCommand<string>(name => {
                PickGridItem? item = ViewModel?.FuelOptions.FirstOrDefault(i => i.Name == name);
                if (item is not null)
                    ViewModel?.PickFuel(item);
            });

            beaconChoiceGrid.ChooseRequested += (_, index) => {
                if (ViewModel is null)
                    return;
                if (index == 0) {
                    ViewModel.ClearBeacon();
                    return;
                }
                int optionIndex = index - 1;
                if (optionIndex >= 0 && optionIndex < ViewModel.BeaconOptions.Count)
                    ViewModel.PickBeacon(ViewModel.BeaconOptions[optionIndex]);
            };
            beaconFallbackPopUp.Command = new RelayCommand<string>(name => {
                if (ViewModel is null)
                    return;
                if (name == NoBeaconName) {
                    ViewModel.ClearBeacon();
                    return;
                }
                PickGridItem? item = ViewModel.BeaconOptions.FirstOrDefault(i => i.Name == name);
                if (item is not null)
                    ViewModel.PickBeacon(item);
            });

            equippedGrid.ChooseRequested += (_, _) => moduleOptionsGrid.Focus();
            equippedGrid.Cleared += EquippedGrid_Cleared;
            equippedGrid.PropertyChanged += (_, e) => {
                if (e.Property == KitPickGrid.SelectedIndexProperty)
                    UpdateOptionsAvailability(equippedGrid, moduleOptionsGrid);
            };
            moduleOptionsGrid.CellClicked += ModuleOptionsGrid_CellClicked;
            equippedBeaconGrid.ChooseRequested += (_, _) => beaconModuleOptionsGrid.Focus();
            equippedBeaconGrid.Cleared += EquippedBeaconGrid_Cleared;
            equippedBeaconGrid.PropertyChanged += (_, e) => {
                if (e.Property == KitPickGrid.SelectedIndexProperty)
                    UpdateOptionsAvailability(equippedBeaconGrid, beaconModuleOptionsGrid);
            };
            beaconModuleOptionsGrid.CellClicked += BeaconModuleOptionsGrid_CellClicked;

            DetachedFromVisualTree += (_, _) => {
                Detach();
                recipeQualityIcon.Source = null;
                foreach (Bitmap bitmap in qualityIconCache.Values)
                    bitmap.Dispose();
                qualityIconCache.Clear();
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private const string NoBeaconName = "No beacon";
        private const int QualityIconSize = 16;

        internal void Bind(IRecipeNodeViewModel node, GraphViewer viewer, DataCache cache, Action? requestRedraw = null) {
            Detach();
            boundViewer = viewer;
            ViewModel = new RecipeSegmentViewModel(node, viewer, cache, requestRedraw);
            DataContext = ViewModel;
            rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
            rateUnitPopUp.SelectedItem = MainWindow.RateUnitOptions[(int)viewer.Graph.SelectedRateUnit];
            lastHasBeacon = ViewModel.HasBeacon;
            beaconGroup.IsExpanded = lastHasBeacon;
            //A stale SelectedIndex aims the next option click at a slot never picked for this node.
            equippedGrid.SelectedIndex = -1;
            equippedGrid.FocusedIndex = -1;
            moduleOptionsGrid.SelectedIndex = -1;
            moduleOptionsGrid.FocusedIndex = -1;
            equippedBeaconGrid.SelectedIndex = -1;
            equippedBeaconGrid.FocusedIndex = -1;
            beaconModuleOptionsGrid.SelectedIndex = -1;
            beaconModuleOptionsGrid.FocusedIndex = -1;
            SyncSlots(equippedGrid, ViewModel.EquippedModules, ViewModel.ModuleSlotCount);
            SyncSlots(moduleOptionsGrid, ViewModel.ModuleOptions, ViewModel.ModuleOptions.Count);
            SyncSlots(equippedBeaconGrid, ViewModel.EquippedBeaconModules, ViewModel.BeaconModuleSlotCount);
            SyncSlots(beaconModuleOptionsGrid, ViewModel.BeaconModuleOptions, ViewModel.BeaconModuleOptions.Count);
            UpdateOptionsAvailability(equippedGrid, moduleOptionsGrid);
            UpdateOptionsAvailability(equippedBeaconGrid, beaconModuleOptionsGrid);
            UpdateChoiceRows();
            ViewModel.PropertyChanged += OnRecipeViewModelPropertyChanged;
            viewer.Session.NodeValuesUpdated += OnNodeValuesUpdated;
        }

        internal void Refresh() {
            ViewModel?.Refresh();
            if (ViewModel is null)
                return;
            rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
            if (boundViewer is not null)
                rateUnitPopUp.SelectedItem = MainWindow.RateUnitOptions[(int)boundViewer.Graph.SelectedRateUnit];
        }

        private void RateMode_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == Segmented.SelectedIndexProperty && ViewModel is not null)
                ViewModel.IsFixedRate = rateMode.SelectedIndex == 1;
        }

        private void PickRateUnitByName(string? name) {
            if (boundViewer is null || name is null)
                return;
            int index = Array.IndexOf(MainWindow.RateUnitOptions, name);
            if (index < 0)
                return;
            RateUnitChanged?.Invoke(this, (ProductionGraph.RateUnit)index);
        }

        private void EquippedGrid_Cleared(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.EquippedModules.Count)
                return;
            ViewModel.RemoveEquippedModule(ViewModel.EquippedModules[index]);
            RetargetArmAfterDelete(equippedGrid);
        }

        private void ModuleOptionsGrid_CellClicked(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.ModuleOptions.Count)
                return;
            ViewModel.EquipModuleAt(ViewModel.ModuleOptions[index], equippedGrid.SelectedIndex);
            equippedGrid.SelectedIndex = -1;
        }

        private void EquippedBeaconGrid_Cleared(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.EquippedBeaconModules.Count)
                return;
            ViewModel.RemoveEquippedBeaconModule(ViewModel.EquippedBeaconModules[index]);
            RetargetArmAfterDelete(equippedBeaconGrid);
        }

        private void BeaconModuleOptionsGrid_CellClicked(object? sender, int index) {
            if (ViewModel is null || index < 0 || index >= ViewModel.BeaconModuleOptions.Count)
                return;
            ViewModel.EquipBeaconModuleAt(ViewModel.BeaconModuleOptions[index], equippedBeaconGrid.SelectedIndex);
            equippedBeaconGrid.SelectedIndex = -1;
        }

        private static void UpdateOptionsAvailability(KitPickGrid equipped, KitPickGrid options) {
            int firstEmpty = FirstEmptySlotIndex(equipped);
            options.TargetIsReplace = equipped.SelectedIndex >= 0;
            options.TargetSlot = equipped.Slots.Count == 0 ? null
                : equipped.SelectedIndex >= 0 ? equipped.SelectedIndex + 1
                : firstEmpty >= 0 ? firstEmpty + 1
                : 0;
        }

        private static void RetargetArmAfterDelete(KitPickGrid grid) => grid.ArmAfterClear(FirstEmptySlotIndex(grid));

        private static int FirstEmptySlotIndex(KitPickGrid grid) {
            for (int i = 0; i < grid.Slots.Count; i++)
                if (grid.Slots[i].IsEmpty)
                    return i;
            return -1;
        }

        //A live {Binding HasBeacon} would re-push on every RefreshComputed and undo a manual collapse.
        private void OnRecipeViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (ViewModel is not null) {
                SyncSlots(equippedGrid, ViewModel.EquippedModules, ViewModel.ModuleSlotCount);
                SyncSlots(moduleOptionsGrid, ViewModel.ModuleOptions, ViewModel.ModuleOptions.Count);
                SyncSlots(equippedBeaconGrid, ViewModel.EquippedBeaconModules, ViewModel.BeaconModuleSlotCount);
                SyncSlots(beaconModuleOptionsGrid, ViewModel.BeaconModuleOptions, ViewModel.BeaconModuleOptions.Count);
                UpdateOptionsAvailability(equippedGrid, moduleOptionsGrid);
                UpdateOptionsAvailability(equippedBeaconGrid, beaconModuleOptionsGrid);
                UpdateChoiceRows();
            }
            if (e.PropertyName != nameof(RecipeSegmentViewModel.HasBeacon) || ViewModel is null || ViewModel.HasBeacon == lastHasBeacon)
                return;
            lastHasBeacon = ViewModel.HasBeacon;
            beaconGroup.IsExpanded = lastHasBeacon;
        }

        private void UpdateChoiceRows() {
            if (ViewModel is null)
                return;
            recipeQualityIcon.Source = ViewModel.RecipeQualityIcon is SKBitmap qualityIcon ? IconBaker.GetOrBake(qualityIconCache, qualityIcon, QualityIconSize) : null;
            UpdateChoiceRow(assemblerRow, assemblerChoiceGrid, assemblerValueLine, assemblerValueLineIcon, assemblerValueLineText,
                assemblerFallbackPopUp, ViewModel.AssemblerOptions, ViewModel.SelectedAssembler,
                "No enabled building crafts this recipe.", includeNone: false, statsSuffix: ViewModel.AssemblerStats,
                oneCandidateNote: "The only building that crafts this recipe.");
            UpdateChoiceRow(fuelRow, fuelChoiceGrid, fuelValueLine, fuelValueLineIcon, fuelValueLineText,
                fuelFallbackPopUp, ViewModel.FuelOptions, ViewModel.SelectedFuel,
                "No enabled fuel burns in this building.", includeNone: false);
            UpdateChoiceRow(beaconRow, beaconChoiceGrid, beaconValueLine, beaconValueLineIcon, beaconValueLineText,
                beaconFallbackPopUp, ViewModel.BeaconOptions, ViewModel.SelectedBeacon,
                "No beacon is enabled in this preset.", includeNone: true);
        }

        private void UpdateChoiceRow(FormRow row, KitPickGrid grid, StackPanel valueLine, Image valueLineIcon, TextBlock valueLineText,
                PopUp fallback, IReadOnlyList<PickGridItem> options, PickGridItem? current, string emptyWarning, bool includeNone,
                string? statsSuffix = null, string? oneCandidateNote = null) {
            bool currentOrphaned = current is not null && options.Count > 0 && !options.Any(o => o.IsSelected);
            int total = options.Count + (includeNone ? 1 : 0) + (currentOrphaned ? 1 : 0);
            row.Error = null;
            row.ErrorIsWarning = false;
            row.NoteIsWarning = false;
            row.NoteSuffix = null;
            row.NoteIsValue = true;

            if (total == 0) {
                grid.Slots.Clear();
                fallback.IsVisible = false;
                valueLine.IsVisible = true;
                valueLine.Opacity = UnavailableOpacity();
                valueLineIcon.Source = current?.Icon;
                valueLineText.Text = current?.Name ?? "";
                row.Note = null;
                row.Error = emptyWarning;
                row.ErrorIsWarning = true;
                return;
            }

            if (total == 1) {
                grid.Slots.Clear();
                fallback.IsVisible = false;
                valueLine.IsVisible = true;
                valueLine.Opacity = 1;
                bool onlyNone = includeNone && options.Count == 0;
                valueLineIcon.Source = onlyNone ? null : options[0].Icon;
                valueLineText.Text = onlyNone ? NoBeaconName : options[0].Name;
                row.Note = oneCandidateNote;
                row.NoteSuffix = oneCandidateNote is not null && !string.IsNullOrEmpty(statsSuffix) ? statsSuffix : null;
                return;
            }

            valueLine.IsVisible = false;

            if (total > MaxChoiceCells()) {
                grid.Slots.Clear();
                fallback.IsVisible = true;
                fallback.Icon = current?.Icon;
                fallback.Items.Clear();
                fallback.ItemIcons.Clear();
                if (includeNone) {
                    fallback.Items.Add(NoBeaconName);
                    fallback.ItemIcons.Add(null);
                }
                foreach (PickGridItem item in options) {
                    fallback.Items.Add(item.Name);
                    fallback.ItemIcons.Add(item.Icon);
                }
                fallback.SelectedItem = current?.Name ?? (includeNone ? NoBeaconName : null);
                row.NoteIsValue = false;
                row.Note = statsSuffix;
                return;
            }

            fallback.IsVisible = false;

            var desired = new List<PickSlot>(total);
            if (includeNone)
                desired.Add(new PickSlot { Name = NoBeaconName, IsNone = true });
            foreach (PickGridItem item in options)
                desired.Add(new PickSlot { Icon = item.Icon, Name = item.Name, IsMissing = item.IsMissing, IsUnavailable = !item.IsAvailable && !item.IsMissing });
            if (currentOrphaned)
                desired.Add(new PickSlot { Icon = current!.Icon, Name = current.Name, IsMissing = true });
            SyncChoiceSlots(grid, desired);

            int selected = includeNone && current is null ? 0 : -1;
            for (int i = 0; i < options.Count; i++)
                if (options[i].IsSelected)
                    selected = i + (includeNone ? 1 : 0);
            if (currentOrphaned)
                selected = desired.Count - 1;
            grid.SelectedIndex = selected;
            row.Note = selected >= 0 && selected < desired.Count ? desired[selected].Name : null;
            row.NoteIsWarning = currentOrphaned;
            row.NoteSuffix = !currentOrphaned && !string.IsNullOrEmpty(statsSuffix) ? statsSuffix : null;
        }

        private int MaxChoiceCells() {
            this.TryFindResource("Foreman.Metrics.PickChoiceMax", null, out object? value);
            return value is double d ? (int)d : 9;
        }

        private double UnavailableOpacity() {
            this.TryFindResource("Foreman.Opacity.Unavailable", ActualThemeVariant, out object? value);
            return value is double d ? d : 0.55;
        }

        private static void SyncSlots(KitPickGrid grid, IReadOnlyList<PickGridItem> items, int slotCount) {
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

        private static void SyncChoiceSlots(KitPickGrid grid, List<PickSlot> desired) {
            for (int i = 0; i < desired.Count; i++) {
                if (i < grid.Slots.Count) {
                    if (!SlotsEqual(grid.Slots[i], desired[i]))
                        grid.Slots[i] = desired[i];
                } else {
                    grid.Slots.Add(desired[i]);
                }
            }
            while (grid.Slots.Count > desired.Count)
                grid.Slots.RemoveAt(grid.Slots.Count - 1);
        }

        private static PickSlot BuildSlot(PickGridItem item) =>
            new() { Icon = item.Icon, Name = item.Name, IsMissing = !item.IsAvailable };

        private static bool SlotsEqual(PickSlot a, PickSlot b) =>
            a.Icon == b.Icon && a.Name == b.Name && a.IsEmpty == b.IsEmpty && a.IsMissing == b.IsMissing
            && a.IsEnabled == b.IsEnabled && a.IsUnavailable == b.IsUnavailable && a.IsNone == b.IsNone;

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
