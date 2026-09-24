using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.ViewModels;
using Foreman.Models;
using PopUp = Foreman.Mac.Kit.Controls.PopUp;
using Segmented = Foreman.Mac.Kit.Controls.Segmented;
using Stepper = Foreman.Mac.Kit.Controls.Stepper;
using Switch = Foreman.Mac.Kit.Controls.Switch;

namespace Foreman.Mac.Views.Settings {
    public partial class GeneralTab : UserControl {
        private readonly Stepper qualityStepsStepper;
        private readonly Switch showNodeRecipeSwitch;
        private readonly Switch roundAssemblerCountSwitch;
        private readonly PopUp assemblerSelectorStylePopUp;
        private readonly PopUp moduleSelectorStylePopUp;
        private readonly Segmented nodeDirectionSegmented;
        private readonly Switch smartNodeDirectionSwitch;
        private readonly Switch simplePassthroughNodesSwitch;

        public GeneralTab() {
            InitializeComponent();

            qualityStepsStepper = this.FindControl<Stepper>("QualityStepsStepper")!;
            showNodeRecipeSwitch = this.FindControl<Switch>("ShowNodeRecipeSwitch")!;
            roundAssemblerCountSwitch = this.FindControl<Switch>("RoundAssemblerCountSwitch")!;
            assemblerSelectorStylePopUp = this.FindControl<PopUp>("AssemblerSelectorStylePopUp")!;
            moduleSelectorStylePopUp = this.FindControl<PopUp>("ModuleSelectorStylePopUp")!;
            nodeDirectionSegmented = this.FindControl<Segmented>("NodeDirectionSegmented")!;
            smartNodeDirectionSwitch = this.FindControl<Switch>("SmartNodeDirectionSwitch")!;
            simplePassthroughNodesSwitch = this.FindControl<Switch>("SimplePassthroughNodesSwitch")!;

            nodeDirectionSegmented.Items.AddRange(["Up", "Down"]);
            PopUpIndexBinding.Bind(this, assemblerSelectorStylePopUp, AssemblerSelector.StyleNames, nameof(SettingsViewModel.AssemblerSelectorStyleIndex),
                vm => vm.AssemblerSelectorStyleIndex, (vm, i) => vm.AssemblerSelectorStyleIndex = i);
            PopUpIndexBinding.Bind(this, moduleSelectorStylePopUp, ModuleSelector.StyleNames, nameof(SettingsViewModel.ModuleSelectorStyleIndex),
                vm => vm.ModuleSelectorStyleIndex, (vm, i) => vm.ModuleSelectorStyleIndex = i);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal Stepper QualityStepsStepperControl => qualityStepsStepper;
        internal Switch ShowNodeRecipeSwitchControl => showNodeRecipeSwitch;
        internal Switch RoundAssemblerCountSwitchControl => roundAssemblerCountSwitch;
        internal PopUp AssemblerSelectorStylePopUpControl => assemblerSelectorStylePopUp;
        internal PopUp ModuleSelectorStylePopUpControl => moduleSelectorStylePopUp;
        internal Segmented NodeDirectionSegmentedControl => nodeDirectionSegmented;
        internal Switch SmartNodeDirectionSwitchControl => smartNodeDirectionSwitch;
        internal Switch SimplePassthroughNodesSwitchControl => simplePassthroughNodesSwitch;
    }
}
