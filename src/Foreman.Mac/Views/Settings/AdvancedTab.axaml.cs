using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Foreman.Mac.Views.Settings {
    public partial class AdvancedTab : UserControl {
        private readonly CheckBox showProductivityBonusOnAllCheckBox;
        private readonly CheckBox showUnavailablesCheckBox;
        private readonly CheckBox loadBarrelingCheckBox;
        private readonly NumericUpDown lowPriorityPowerInput;
        private readonly CheckBox pullConsumerNodesCheckBox;
        private readonly NumericUpDown pullConsumerNodesPowerInput;

        public AdvancedTab() {
            InitializeComponent();

            showProductivityBonusOnAllCheckBox = this.FindControl<CheckBox>("ShowProductivityBonusOnAllCheckBox")!;
            showUnavailablesCheckBox = this.FindControl<CheckBox>("ShowUnavailablesCheckBox")!;
            loadBarrelingCheckBox = this.FindControl<CheckBox>("LoadBarrelingCheckBox")!;
            lowPriorityPowerInput = this.FindControl<NumericUpDown>("LowPriorityPowerInput")!;
            pullConsumerNodesCheckBox = this.FindControl<CheckBox>("PullConsumerNodesCheckBox")!;
            pullConsumerNodesPowerInput = this.FindControl<NumericUpDown>("PullConsumerNodesPowerInput")!;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal CheckBox ShowProductivityBonusOnAllCheckBoxControl => showProductivityBonusOnAllCheckBox;
        internal CheckBox ShowUnavailablesCheckBoxControl => showUnavailablesCheckBox;
        internal CheckBox LoadBarrelingCheckBoxControl => loadBarrelingCheckBox;
        internal NumericUpDown LowPriorityPowerInputControl => lowPriorityPowerInput;
        internal CheckBox PullConsumerNodesCheckBoxControl => pullConsumerNodesCheckBox;
        internal NumericUpDown PullConsumerNodesPowerInputControl => pullConsumerNodesPowerInput;
    }
}
