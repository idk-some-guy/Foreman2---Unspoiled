using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Kit.Composites;

namespace Foreman.Mac.Views.Settings {
    public partial class PresetsTab : UserControl {
        private readonly TextBlock currentPresetLabel;
        private readonly ListBox modSelectionBox;
        private readonly ListBox presetListBox;
        private readonly MenuItem selectPresetMenuItem;
        private readonly MenuItem deletePresetMenuItem;
        private readonly Button importPresetButton;
        private readonly Button comparePresetsButton;
        private readonly FormGroup presetsGroup;
        private readonly Border modsStateHost;
        private readonly StatePanel loadingPanel;
        private readonly Border loadWarning;
        private readonly TextBlock loadWarningText;
        private readonly Button retryButton;

        public PresetsTab() {
            InitializeComponent();

            currentPresetLabel = this.FindControl<TextBlock>("CurrentPresetLabel")!;
            modSelectionBox = this.FindControl<ListBox>("ModSelectionBox")!;
            presetListBox = this.FindControl<ListBox>("PresetListBox")!;
            selectPresetMenuItem = this.FindControl<MenuItem>("SelectPresetMenuItem")!;
            deletePresetMenuItem = this.FindControl<MenuItem>("DeletePresetMenuItem")!;
            importPresetButton = this.FindControl<Button>("ImportPresetButton")!;
            comparePresetsButton = this.FindControl<Button>("ComparePresetsButton")!;
            presetsGroup = this.FindControl<FormGroup>("PresetsGroup")!;
            modsStateHost = this.FindControl<Border>("ModsStateHost")!;
            loadingPanel = (StatePanel)Resources["PresetsLoadingPanel"]!;
            loadWarning = (Border)Resources["PresetLoadWarning"]!;
            Grid warningRow = (Grid)loadWarning.Child!;
            loadWarningText = (TextBlock)warningRow.Children[1];
            retryButton = (Button)warningRow.Children[2];

            ListRows.SetMinimum(presetListBox, 3);
            ListRows.SetMinimum(modSelectionBox, 3);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal TextBlock CurrentPresetLabelControl => currentPresetLabel;
        internal ListBox ModSelectionBoxControl => modSelectionBox;
        internal ListBox PresetListBoxControl => presetListBox;
        internal MenuItem SelectPresetMenuItemControl => selectPresetMenuItem;
        internal MenuItem DeletePresetMenuItemControl => deletePresetMenuItem;
        internal Button ImportPresetButtonControl => importPresetButton;
        internal Button ComparePresetsButtonControl => comparePresetsButton;
        internal FormGroup PresetsGroupControl => presetsGroup;
        internal Border ModsStateHostControl => modsStateHost;
        internal Button RetryPresetReadButtonControl => retryButton;

        internal void ShowModListReading() => ShowModState(loadingPanel);

        internal void ShowModListFailure(string presetName) {
            loadWarningText.Text = $"“{Dialogs.MiddleTruncate(presetName, 24)}” couldn’t be read — the preset file may be damaged.";
            ShowModState(loadWarning);
        }

        internal void ShowModList() => ShowModState(null);

        private void ShowModState(Control? state) {
            modsStateHost.Child = state;
            modsStateHost.IsVisible = state is not null;
            modSelectionBox.IsVisible = state is null;
        }
    }
}
