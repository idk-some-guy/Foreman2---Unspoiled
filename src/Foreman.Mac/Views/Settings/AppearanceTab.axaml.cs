using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.ViewModels;
using PopUp = Foreman.Mac.Kit.Controls.PopUp;
using Segmented = Foreman.Mac.Kit.Controls.Segmented;
using Stepper = Foreman.Mac.Kit.Controls.Stepper;
using Switch = Foreman.Mac.Kit.Controls.Switch;

namespace Foreman.Mac.Views.Settings {
    public partial class AppearanceTab : UserControl {
        private readonly Segmented themeSegmented;
        private readonly PopUp uiScalePopUp;
        private readonly Segmented linkRoutingSegmented;
        private readonly Switch dynamicLineWidthSwitch;
        private readonly Switch arrowsOnLinksSwitch;
        private readonly Segmented levelOfDetailSegmented;
        private readonly Stepper nodeCountForSimpleViewStepper;
        private readonly Stepper iconsSizeStepper;
        private readonly Switch abbreviateSciPacksSwitch;

        public AppearanceTab() {
            InitializeComponent();

            themeSegmented = this.FindControl<Segmented>("ThemeSegmented")!;
            uiScalePopUp = this.FindControl<PopUp>("UiScalePopUp")!;
            linkRoutingSegmented = this.FindControl<Segmented>("LinkRoutingSegmented")!;
            dynamicLineWidthSwitch = this.FindControl<Switch>("DynamicLineWidthSwitch")!;
            arrowsOnLinksSwitch = this.FindControl<Switch>("ArrowsOnLinksSwitch")!;
            levelOfDetailSegmented = this.FindControl<Segmented>("LevelOfDetailSegmented")!;
            nodeCountForSimpleViewStepper = this.FindControl<Stepper>("NodeCountForSimpleViewStepper")!;
            iconsSizeStepper = this.FindControl<Stepper>("IconsSizeStepper")!;
            abbreviateSciPacksSwitch = this.FindControl<Switch>("AbbreviateSciPacksSwitch")!;

            themeSegmented.Items.AddRange(["System", "Light", "Dark"]);
            uiScalePopUp.Items.AddRange(SettingsViewModel.UiScaleLabels);
            linkRoutingSegmented.Items.AddRange(["Rounded", "Orthogonal"]);
            levelOfDetailSegmented.Items.AddRange(["Low", "Medium", "High"]);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal Segmented ThemeSegmentedControl => themeSegmented;
        internal PopUp UiScalePopUpControl => uiScalePopUp;
        internal Segmented LinkRoutingSegmentedControl => linkRoutingSegmented;
        internal Switch DynamicLineWidthSwitchControl => dynamicLineWidthSwitch;
        internal Switch ArrowsOnLinksSwitchControl => arrowsOnLinksSwitch;
        internal Segmented LevelOfDetailSegmentedControl => levelOfDetailSegmented;
        internal Stepper NodeCountForSimpleViewStepperControl => nodeCountForSimpleViewStepper;
        internal Stepper IconsSizeStepperControl => iconsSizeStepper;
        internal Switch AbbreviateSciPacksSwitchControl => abbreviateSciPacksSwitch;
    }
}
