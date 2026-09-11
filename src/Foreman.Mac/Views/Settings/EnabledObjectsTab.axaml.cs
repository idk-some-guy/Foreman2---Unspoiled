using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Collections;
using System.Collections.Generic;

namespace Foreman.Mac.Views.Settings {
    //Enabled Objects tab content moved verbatim out of SettingsWindow.axaml (reference task-4-brief.md) -
    //membership toggling and the load-from-save/science-packs/enable-all orchestration stay on
    //SettingsWindow, which owns the live DataCache/Options this tab's rows write into.
    public partial class EnabledObjectsTab : UserControl {
        private readonly TextBox filterTextBox;
        private readonly CheckBox showUnavailablesFilterCheckBox;
        private readonly Button loadEnabledFromSaveButton;
        private readonly Button setEnabledFromSciencePacksButton;
        private readonly Button enableAllButton;
        private readonly ListBox assemblerListView;
        private readonly ListBox minerListView;
        private readonly ListBox powerListView;
        private readonly ListBox beaconListView;
        private readonly ListBox moduleListView;
        private readonly ListBox recipeListView;
        private readonly ListBox qualityListView;

        public EnabledObjectsTab() {
            InitializeComponent();

            filterTextBox = this.FindControl<TextBox>("FilterTextBox")!;
            showUnavailablesFilterCheckBox = this.FindControl<CheckBox>("ShowUnavailablesFilterCheckBox")!;
            loadEnabledFromSaveButton = this.FindControl<Button>("LoadEnabledFromSaveButton")!;
            setEnabledFromSciencePacksButton = this.FindControl<Button>("SetEnabledFromSciencePacksButton")!;
            enableAllButton = this.FindControl<Button>("EnableAllButton")!;
            assemblerListView = this.FindControl<ListBox>("AssemblerListView")!;
            minerListView = this.FindControl<ListBox>("MinerListView")!;
            powerListView = this.FindControl<ListBox>("PowerListView")!;
            beaconListView = this.FindControl<ListBox>("BeaconListView")!;
            moduleListView = this.FindControl<ListBox>("ModuleListView")!;
            recipeListView = this.FindControl<ListBox>("RecipeListView")!;
            qualityListView = this.FindControl<ListBox>("QualityListView")!;

            foreach (ListBox list in AllListViews)
                WireSelectAllShortcut(list);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal TextBox FilterTextBoxControl => filterTextBox;
        internal CheckBox ShowUnavailablesFilterCheckBoxControl => showUnavailablesFilterCheckBox;
        internal Button LoadEnabledFromSaveButtonControl => loadEnabledFromSaveButton;
        internal Button SetEnabledFromSciencePacksButtonControl => setEnabledFromSciencePacksButton;
        internal Button EnableAllButtonControl => enableAllButton;
        internal ListBox AssemblerListViewControl => assemblerListView;
        internal ListBox MinerListViewControl => minerListView;
        internal ListBox PowerListViewControl => powerListView;
        internal ListBox BeaconListViewControl => beaconListView;
        internal ListBox ModuleListViewControl => moduleListView;
        internal ListBox RecipeListViewControl => recipeListView;
        internal ListBox QualityListViewControl => qualityListView;

        internal IEnumerable<ListBox> AllListViews {
            get {
                yield return assemblerListView;
                yield return minerListView;
                yield return powerListView;
                yield return beaconListView;
                yield return moduleListView;
                yield return recipeListView;
                yield return qualityListView;
            }
        }

        //Ports ListView_KeyDown's Ctrl+A (reference SettingsForm.cs:344-347), Cmd on macOS/Ctrl on Linux
        //(PlatformModifiers.Primary): selects every row in this sub-tab, ahead of ListBox's own bubble-phase
        //key handling (arrow navigation, type-ahead), so the shortcut always wins here regardless of what a
        //focused row would otherwise do with it.
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
