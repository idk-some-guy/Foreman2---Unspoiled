using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.ViewModels;
using Foreman.Models;

namespace Foreman.Mac.Views.Settings {
    public partial class GeneralTab : UserControl {
        private readonly NumericUpDown qualityStepsInput;
        private readonly CheckBox showNodeRecipeCheckBox;
        private readonly CheckBox roundAssemblerCountCheckBox;
        private readonly ComboBox assemblerSelectorStyleDropDown;
        private readonly ComboBox moduleSelectorStyleDropDown;
        private readonly ComboBox nodeDirectionDropDown;
        private readonly CheckBox smartNodeDirectionCheckBox;
        private readonly CheckBox simplePassthroughNodesCheckBox;

        public GeneralTab() {
            InitializeComponent();

            qualityStepsInput = this.FindControl<NumericUpDown>("QualityStepsInput")!;
            showNodeRecipeCheckBox = this.FindControl<CheckBox>("ShowNodeRecipeCheckBox")!;
            roundAssemblerCountCheckBox = this.FindControl<CheckBox>("RoundAssemblerCountCheckBox")!;
            assemblerSelectorStyleDropDown = this.FindControl<ComboBox>("AssemblerSelectorStyleDropDown")!;
            moduleSelectorStyleDropDown = this.FindControl<ComboBox>("ModuleSelectorStyleDropDown")!;
            nodeDirectionDropDown = this.FindControl<ComboBox>("NodeDirectionDropDown")!;
            smartNodeDirectionCheckBox = this.FindControl<CheckBox>("SmartNodeDirectionCheckBox")!;
            simplePassthroughNodesCheckBox = this.FindControl<CheckBox>("SimplePassthroughNodesCheckBox")!;

            assemblerSelectorStyleDropDown.ItemsSource = AssemblerSelector.StyleNames;
            moduleSelectorStyleDropDown.ItemsSource = ModuleSelector.StyleNames;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal NumericUpDown QualityStepsInputControl => qualityStepsInput;
        internal CheckBox ShowNodeRecipeCheckBoxControl => showNodeRecipeCheckBox;
        internal CheckBox RoundAssemblerCountCheckBoxControl => roundAssemblerCountCheckBox;
        internal ComboBox AssemblerSelectorStyleDropDownControl => assemblerSelectorStyleDropDown;
        internal ComboBox ModuleSelectorStyleDropDownControl => moduleSelectorStyleDropDown;
        internal ComboBox NodeDirectionDropDownControl => nodeDirectionDropDown;
        internal CheckBox SmartNodeDirectionCheckBoxControl => smartNodeDirectionCheckBox;
        internal CheckBox SimplePassthroughNodesCheckBoxControl => simplePassthroughNodesCheckBox;

        internal SettingsViewModel? ViewModel => DataContext as SettingsViewModel;
    }
}
