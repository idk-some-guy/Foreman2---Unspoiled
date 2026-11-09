using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.ViewModels;
using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using PopUp = Foreman.Mac.Kit.Controls.PopUp;
using SearchField = Foreman.Mac.Kit.Controls.SearchField;
using Segmented = Foreman.Mac.Kit.Controls.Segmented;

namespace Foreman.Mac.Views.Settings {
    public partial class EnabledObjectsTab : UserControl {
        private readonly PopUp categoryPopUp;
        private readonly SearchField filterField;
        private readonly Segmented showSegmented;
        private readonly CheckBox showUnavailablesFilterCheckBox;
        private readonly ListBox objectList;
        private readonly StatePanel noResultsPanel;
        private readonly Button loadEnabledFromSaveButton;
        private readonly Button setEnabledFromSciencePacksButton;
        private readonly Button enableAllButton;
        private SettingsViewModel? viewModel;

        public EnabledObjectsTab() {
            InitializeComponent();

            categoryPopUp = this.FindControl<PopUp>("CategoryPopUp")!;
            filterField = this.FindControl<SearchField>("FilterField")!;
            showSegmented = this.FindControl<Segmented>("ShowSegmented")!;
            showUnavailablesFilterCheckBox = this.FindControl<CheckBox>("ShowUnavailablesFilterCheckBox")!;
            objectList = this.FindControl<ListBox>("ObjectList")!;
            noResultsPanel = this.FindControl<StatePanel>("NoResultsPanel")!;
            loadEnabledFromSaveButton = this.FindControl<Button>("LoadEnabledFromSaveButton")!;
            setEnabledFromSciencePacksButton = this.FindControl<Button>("SetEnabledFromSciencePacksButton")!;
            enableAllButton = this.FindControl<Button>("EnableAllButton")!;

            showSegmented.Items.AddRange(["All", "Enabled", "Disabled"]);
            categoryPopUp.PropertyChanged += (_, e) => {
                if (e.Property == PopUp.SelectedItemProperty && viewModel is not null && categoryPopUp.SelectedItem is string label)
                    viewModel.EnabledObjectsCategoryIndex = categoryPopUp.Items.IndexOf(label);
            };
            this.FindControl<Button>("ShowAllObjectsButton")!.Click += (_, _) => ShowAllObjects();
            WireSelectAllShortcut(objectList);
            ListRows.SetMinimum(objectList, 4);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);
            viewModel?.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel = DataContext as SettingsViewModel;
            viewModel?.PropertyChanged += OnViewModelPropertyChanged;
            SyncCategories();
            UpdateNoResults();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName is nameof(SettingsViewModel.EnabledObjectsCategoryLabels) or nameof(SettingsViewModel.EnabledObjectsCategoryIndex))
                SyncCategories();
            if (e.PropertyName is nameof(SettingsViewModel.VisibleObjects) or nameof(SettingsViewModel.FilterText))
                UpdateNoResults();
        }

        private void SyncCategories() {
            if (viewModel is null)
                return;
            if (!categoryPopUp.Items.SequenceEqual(viewModel.EnabledObjectsCategoryLabels)) {
                categoryPopUp.Items.Clear();
                categoryPopUp.Items.AddRange(viewModel.EnabledObjectsCategoryLabels);
            }
            categoryPopUp.SelectedItem = categoryPopUp.Items[viewModel.EnabledObjectsCategoryIndex];
        }

        private void UpdateNoResults() {
            string filter = viewModel?.FilterText ?? "";
            bool empty = viewModel is not null && filter.Length > 0 && viewModel.VisibleObjects.Count == 0;
            if (empty)
                noResultsPanel.Headline = $"No objects match “{Dialogs.MiddleTruncate(filter, 24)}”";
            noResultsPanel.IsVisible = empty;
            objectList.IsVisible = !empty;
        }

        private void ShowAllObjects() {
            if (viewModel is null)
                return;
            viewModel.FilterText = "";
            viewModel.EnabledObjectsShowIndex = 0;
        }

        internal PopUp CategoryPopUpControl => categoryPopUp;
        internal SearchField FilterFieldControl => filterField;
        internal Segmented ShowSegmentedControl => showSegmented;
        internal CheckBox ShowUnavailablesFilterCheckBoxControl => showUnavailablesFilterCheckBox;
        internal ListBox ObjectListControl => objectList;
        internal StatePanel NoResultsPanelControl => noResultsPanel;
        internal Button LoadEnabledFromSaveButtonControl => loadEnabledFromSaveButton;
        internal Button SetEnabledFromSciencePacksButtonControl => setEnabledFromSciencePacksButton;
        internal Button EnableAllButtonControl => enableAllButton;

        private static void WireSelectAllShortcut(ListBox list) =>
            list.AddHandler(InputElement.KeyDownEvent, (_, e) => {
                if (e.Key == Key.A && e.KeyModifiers.HasFlag(PlatformModifiers.Primary)) {
                    SelectAllRows(list);
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);

        private static void SelectAllRows(ListBox list) {
            if (list.ItemsSource is not IEnumerable items || list.SelectedItems is not { } selected)
                return;
            selected.Clear();
            foreach (object? item in items)
                selected.Add(item);
        }
    }
}
