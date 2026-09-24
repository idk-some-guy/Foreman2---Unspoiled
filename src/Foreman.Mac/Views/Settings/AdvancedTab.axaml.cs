using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stepper = Foreman.Mac.Kit.Controls.Stepper;
using Switch = Foreman.Mac.Kit.Controls.Switch;

namespace Foreman.Mac.Views.Settings {
    public partial class AdvancedTab : UserControl {
        private readonly Switch showProductivityBonusOnAllSwitch;
        private readonly Switch showUnavailablesSwitch;
        private readonly Switch loadBarrelingSwitch;
        private readonly Stepper lowPriorityPowerStepper;
        private readonly Switch pullConsumerNodesSwitch;
        private readonly Stepper pullConsumerNodesPowerStepper;

        public AdvancedTab() {
            InitializeComponent();

            showProductivityBonusOnAllSwitch = this.FindControl<Switch>("ShowProductivityBonusOnAllSwitch")!;
            showUnavailablesSwitch = this.FindControl<Switch>("ShowUnavailablesSwitch")!;
            loadBarrelingSwitch = this.FindControl<Switch>("LoadBarrelingSwitch")!;
            lowPriorityPowerStepper = this.FindControl<Stepper>("LowPriorityPowerStepper")!;
            pullConsumerNodesSwitch = this.FindControl<Switch>("PullConsumerNodesSwitch")!;
            pullConsumerNodesPowerStepper = this.FindControl<Stepper>("PullConsumerNodesPowerStepper")!;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal Switch ShowProductivityBonusOnAllSwitchControl => showProductivityBonusOnAllSwitch;
        internal Switch ShowUnavailablesSwitchControl => showUnavailablesSwitch;
        internal Switch LoadBarrelingSwitchControl => loadBarrelingSwitch;
        internal Stepper LowPriorityPowerStepperControl => lowPriorityPowerStepper;
        internal Switch PullConsumerNodesSwitchControl => pullConsumerNodesSwitch;
        internal Stepper PullConsumerNodesPowerStepperControl => pullConsumerNodesPowerStepper;
    }
}
