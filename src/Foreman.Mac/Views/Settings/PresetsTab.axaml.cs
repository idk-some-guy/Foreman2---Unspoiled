using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Foreman.Mac.Views.Settings {
    //Presets tab content moved verbatim out of SettingsWindow.axaml (reference task-4-brief.md) - every
    //behavior (preset selection, import, compare, delete) stays orchestrated from SettingsWindow itself,
    //which needs window-level services (dialogs, SheetHost) this control has no business owning.
    public partial class PresetsTab : UserControl {
        private readonly TextBlock currentPresetLabel;
        private readonly ListBox modSelectionBox;
        private readonly ListBox presetListBox;
        private readonly MenuItem selectPresetMenuItem;
        private readonly MenuItem deletePresetMenuItem;
        private readonly Button importPresetButton;
        private readonly Button comparePresetsButton;

        public PresetsTab() {
            InitializeComponent();

            currentPresetLabel = this.FindControl<TextBlock>("CurrentPresetLabel")!;
            modSelectionBox = this.FindControl<ListBox>("ModSelectionBox")!;
            presetListBox = this.FindControl<ListBox>("PresetListBox")!;
            selectPresetMenuItem = this.FindControl<MenuItem>("SelectPresetMenuItem")!;
            deletePresetMenuItem = this.FindControl<MenuItem>("DeletePresetMenuItem")!;
            importPresetButton = this.FindControl<Button>("ImportPresetButton")!;
            comparePresetsButton = this.FindControl<Button>("ComparePresetsButton")!;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal TextBlock CurrentPresetLabelControl => currentPresetLabel;
        internal ListBox ModSelectionBoxControl => modSelectionBox;
        internal ListBox PresetListBoxControl => presetListBox;
        internal MenuItem SelectPresetMenuItemControl => selectPresetMenuItem;
        internal MenuItem DeletePresetMenuItemControl => deletePresetMenuItem;
        internal Button ImportPresetButtonControl => importPresetButton;
        internal Button ComparePresetsButtonControl => comparePresetsButton;
    }
}
