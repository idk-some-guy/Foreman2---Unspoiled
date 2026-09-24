using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Switch = Foreman.Mac.Kit.Controls.Switch;

namespace Foreman.Mac.Views.Settings {
    public partial class AnnotationsTab : UserControl {
        private readonly Switch errorArrowsSwitch;
        private readonly Switch warningArrowsSwitch;
        private readonly Switch disconnectedArrowsSwitch;
        private readonly Switch ouSuppliedArrowsSwitch;
        private readonly Switch flagOUSupplyNodesSwitch;

        public AnnotationsTab() {
            InitializeComponent();

            errorArrowsSwitch = this.FindControl<Switch>("ErrorArrowsSwitch")!;
            warningArrowsSwitch = this.FindControl<Switch>("WarningArrowsSwitch")!;
            disconnectedArrowsSwitch = this.FindControl<Switch>("DisconnectedArrowsSwitch")!;
            ouSuppliedArrowsSwitch = this.FindControl<Switch>("OUSuppliedArrowsSwitch")!;
            flagOUSupplyNodesSwitch = this.FindControl<Switch>("FlagOUSupplyNodesSwitch")!;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal Switch ErrorArrowsSwitchControl => errorArrowsSwitch;
        internal Switch WarningArrowsSwitchControl => warningArrowsSwitch;
        internal Switch DisconnectedArrowsSwitchControl => disconnectedArrowsSwitch;
        internal Switch OUSuppliedArrowsSwitchControl => ouSuppliedArrowsSwitch;
        internal Switch FlagOUSupplyNodesSwitchControl => flagOUSupplyNodesSwitch;
    }
}
