using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Foreman.Mac.Views.Settings {
    public partial class AnnotationsTab : UserControl {
        private readonly CheckBox errorArrowsCheckBox;
        private readonly CheckBox warningArrowsCheckBox;
        private readonly CheckBox disconnectedArrowsCheckBox;
        private readonly CheckBox ouSuppliedArrowsCheckBox;
        private readonly CheckBox flagOUSupplyNodesCheckBox;

        public AnnotationsTab() {
            InitializeComponent();

            errorArrowsCheckBox = this.FindControl<CheckBox>("ErrorArrowsCheckBox")!;
            warningArrowsCheckBox = this.FindControl<CheckBox>("WarningArrowsCheckBox")!;
            disconnectedArrowsCheckBox = this.FindControl<CheckBox>("DisconnectedArrowsCheckBox")!;
            ouSuppliedArrowsCheckBox = this.FindControl<CheckBox>("OUSuppliedArrowsCheckBox")!;
            flagOUSupplyNodesCheckBox = this.FindControl<CheckBox>("FlagOUSupplyNodesCheckBox")!;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal CheckBox ErrorArrowsCheckBoxControl => errorArrowsCheckBox;
        internal CheckBox WarningArrowsCheckBoxControl => warningArrowsCheckBox;
        internal CheckBox DisconnectedArrowsCheckBoxControl => disconnectedArrowsCheckBox;
        internal CheckBox OUSuppliedArrowsCheckBoxControl => ouSuppliedArrowsCheckBox;
        internal CheckBox FlagOUSupplyNodesCheckBoxControl => flagOUSupplyNodesCheckBox;
    }
}
