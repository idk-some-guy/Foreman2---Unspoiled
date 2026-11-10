using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Foreman.Mac;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using KitPickGrid = Foreman.Mac.Kit.Composites.PickGrid;

namespace Foreman.Mac.Views.Inspector {
    public partial class FlowSegmentView : UserControl {
        private readonly Segmented rateMode;
        private readonly Stepper rateValue;
        private readonly PopUp rateUnitPopUp;
        private readonly FormRow towerRow;
        private readonly FormRow towerModulesEquippedRow;
        private readonly KitPickGrid towerChoiceGrid;
        private readonly StackPanel towerValueLine;
        private readonly Image towerValueLineIcon;
        private readonly TextBlock towerValueLineText;
        private readonly PopUp towerFallbackPopUp;
        private readonly PopUp towerQualityPopUp;
        private readonly KitPickGrid equippedTowerGrid;
        private readonly KitPickGrid towerModuleOptionsGrid;
        private readonly Dictionary<SKBitmap, Bitmap> qualityIconCache = [];
        private IDisposable? equippedNoteBinding;
        private GraphViewer? boundViewer;

        private const string NoTowerName = "No tower";
        private const string NoTowerModulesNote = "No enabled productivity module fits this tower.";
        private const int QualityIconSize = 16;

        public FlowSegmentViewModel? ViewModel { get; private set; }

        internal event EventHandler? TowerChanged;

        public event EventHandler<ProductionGraph.RateUnit>? RateUnitChanged;

        public FlowSegmentView() {
            InitializeComponent();
            rateMode = this.FindControl<Segmented>("RateMode")!;
            rateMode.Items.Add("Auto");
            rateMode.Items.Add("Fixed");
            rateValue = this.FindControl<Stepper>("RateValue")!;
            rateUnitPopUp = this.FindControl<PopUp>("RateUnitPopUp")!;
            foreach (string option in MainWindow.RateUnitOptions)
                rateUnitPopUp.Items.Add(option);
            rateMode.PropertyChanged += RateMode_PropertyChanged;
            rateUnitPopUp.Command = new RelayCommand<string>(PickRateUnitByName);

            towerRow = this.FindControl<FormRow>("TowerRow")!;
            towerModulesEquippedRow = this.FindControl<FormRow>("TowerModulesEquippedRow")!;
            towerChoiceGrid = this.FindControl<KitPickGrid>("TowerChoiceGrid")!;
            towerValueLine = this.FindControl<StackPanel>("TowerValueLine")!;
            towerValueLineIcon = this.FindControl<Image>("TowerValueLineIcon")!;
            towerValueLineText = this.FindControl<TextBlock>("TowerValueLineText")!;
            towerFallbackPopUp = this.FindControl<PopUp>("TowerFallbackPopUp")!;
            towerQualityPopUp = this.FindControl<PopUp>("TowerQualityPopUp")!;
            equippedTowerGrid = this.FindControl<KitPickGrid>("EquippedTowerGrid")!;
            towerModuleOptionsGrid = this.FindControl<KitPickGrid>("TowerModuleOptionsGrid")!;

            towerChoiceGrid.ChooseRequested += (_, index) => {
                if (ViewModel is null)
                    return;
                if (index == 0) {
                    ViewModel.ClearTower();
                    return;
                }
                int optionIndex = index - 1;
                if (optionIndex >= 0 && optionIndex < ViewModel.TowerOptions.Count)
                    ViewModel.PickTower(ViewModel.TowerOptions[optionIndex]);
            };
            towerFallbackPopUp.Command = new RelayCommand<string>(name => {
                if (ViewModel is null)
                    return;
                if (name == NoTowerName) {
                    ViewModel.ClearTower();
                    return;
                }
                PickGridItem? item = ViewModel.TowerOptions.FirstOrDefault(i => i.Name == name);
                if (item is not null)
                    ViewModel.PickTower(item);
            });
            towerQualityPopUp.Command = new RelayCommand<string>(name => {
                PickGridItem? item = ViewModel?.TowerQualityOptions.FirstOrDefault(i => i.Name == name);
                if (item is not null)
                    ViewModel!.PickTowerQuality(item);
            });
            equippedTowerGrid.ChooseRequested += (_, _) => towerModuleOptionsGrid.Focus();
            equippedTowerGrid.Cleared += (_, index) => {
                ViewModel?.RemoveEquippedTowerModule(index);
                InspectorChoiceRows.RetargetArmAfterDelete(equippedTowerGrid);
            };
            equippedTowerGrid.PropertyChanged += (_, e) => {
                if (e.Property == KitPickGrid.SelectedIndexProperty)
                    InspectorChoiceRows.UpdateOptionsAvailability(equippedTowerGrid, towerModuleOptionsGrid);
            };
            towerModuleOptionsGrid.CellClicked += (_, index) => {
                if (ViewModel is null || index < 0 || index >= ViewModel.TowerModuleOptions.Count)
                    return;
                ViewModel.EquipTowerModuleAt(ViewModel.TowerModuleOptions[index], equippedTowerGrid.SelectedIndex);
                equippedTowerGrid.SelectedIndex = -1;
            };

            DetachedFromVisualTree += (_, _) => {
                Detach();
                towerQualityPopUp.Icon = null;
                foreach (Bitmap bitmap in qualityIconCache.Values)
                    bitmap.Dispose();
                qualityIconCache.Clear();
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal void Bind(INodeViewModel node, GraphViewer viewer, Action? requestRedraw = null) {
            Detach();
            boundViewer = viewer;
            ViewModel = new FlowSegmentViewModel(node, viewer, requestRedraw);
            DataContext = ViewModel;
            foreach (KitPickGrid grid in new[] { equippedTowerGrid, towerModuleOptionsGrid }) {
                grid.SelectedIndex = -1;
                grid.FocusedIndex = -1;
            }
            ViewModel.TowerChanged += OnTowerChanged;
            SyncTower();
            rateValue.Maximum = node.MaxDesiredSetValue * viewer.Graph.GetRateMultipler();
            rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
            rateUnitPopUp.SelectedItem = MainWindow.RateUnitOptions[(int)viewer.Graph.SelectedRateUnit];
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

        private void OnTowerChanged(object? sender, EventArgs e) {
            SyncTower();
            TowerChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SyncTower() {
            if (ViewModel is null || !ViewModel.ShowsTower)
                return;
            InspectorChoiceRows.SyncSlots(equippedTowerGrid, ViewModel.EquippedTowerModules, ViewModel.TowerModuleSlotCount);
            InspectorChoiceRows.SyncSlots(towerModuleOptionsGrid, ViewModel.TowerModuleOptions, ViewModel.TowerModuleOptions.Count);
            InspectorChoiceRows.UpdateOptionsAvailability(equippedTowerGrid, towerModuleOptionsGrid);
            InspectorChoiceRows.UpdateChoiceRow(this, towerRow, towerChoiceGrid, towerValueLine, towerValueLineIcon, towerValueLineText, towerFallbackPopUp,
                ViewModel.TowerOptions, ViewModel.SelectedTower, "No tower is enabled in this preset.", includeNone: true,
                statsSuffix: ViewModel.SelectedTower is null ? null : ViewModel.TowerStats, noneName: NoTowerName, orphanWithoutOptions: true);
            SyncTowerQuality();
            SyncEquippedNote();
        }

        private void SyncTowerQuality() {
            string[] names = [.. ViewModel!.TowerQualityOptions.Select(o => o.Name)];
            if (!names.SequenceEqual(towerQualityPopUp.Items.OfType<string>())) {
                towerQualityPopUp.Items.Clear();
                towerQualityPopUp.ItemIcons.Clear();
                foreach (PickGridItem option in ViewModel.TowerQualityOptions) {
                    towerQualityPopUp.Items.Add(option.Name);
                    towerQualityPopUp.ItemIcons.Add(option.Icon);
                }
            }
            towerQualityPopUp.SelectedItem = ViewModel.CurrentTowerQualityName;
            SKBitmap? icon = ViewModel.CurrentTowerQualityIcon;
            towerQualityPopUp.Icon = icon is null ? null : IconBaker.GetOrBake(qualityIconCache, icon, QualityIconSize);
        }

        private void SyncEquippedNote() {
            if (ViewModel!.HasTowerModuleOptions) {
                equippedNoteBinding ??= towerModulesEquippedRow.Bind(FormRow.NoteProperty, equippedTowerGrid.GetObservable(KitPickGrid.ArmingNoteProperty));
                return;
            }
            equippedNoteBinding?.Dispose();
            equippedNoteBinding = null;
            towerModulesEquippedRow.Note = ViewModel.TowerIsMissing ? null : NoTowerModulesNote;
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

        private void OnNodeValuesUpdated(object? sender, EventArgs e) => Refresh();

        private void Detach() {
            if (boundViewer is not null)
                boundViewer.Session.NodeValuesUpdated -= OnNodeValuesUpdated;
            boundViewer = null;
            if (ViewModel is not null)
                ViewModel.TowerChanged -= OnTowerChanged;
        }
    }
}
