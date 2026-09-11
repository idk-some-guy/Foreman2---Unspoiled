using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;

namespace Foreman.Mac.Views.Settings {
    public partial class AppearanceTab : UserControl {
        private readonly ComboBox flagDarkModeDropDown;
        private readonly ComboBox uiScaleDropDown;
        private readonly RadioButton lowLodRadioButton;
        private readonly RadioButton mediumLodRadioButton;
        private readonly RadioButton highLodRadioButton;
        private readonly NumericUpDown nodeCountForSimpleViewInput;
        private readonly NumericUpDown iconsSizeInput;
        private readonly CheckBox arrowsOnLinksCheckBox;
        private readonly CheckBox dynamicLWCheckBox;
        private readonly CheckBox abbreviateSciPackCheckBox;

        public AppearanceTab() {
            InitializeComponent();

            flagDarkModeDropDown = this.FindControl<ComboBox>("FlagDarkModeDropDown")!;
            uiScaleDropDown = this.FindControl<ComboBox>("UiScaleDropDown")!;
            lowLodRadioButton = this.FindControl<RadioButton>("LowLodRadioButton")!;
            mediumLodRadioButton = this.FindControl<RadioButton>("MediumLodRadioButton")!;
            highLodRadioButton = this.FindControl<RadioButton>("HighLodRadioButton")!;
            nodeCountForSimpleViewInput = this.FindControl<NumericUpDown>("NodeCountForSimpleViewInput")!;
            iconsSizeInput = this.FindControl<NumericUpDown>("IconsSizeInput")!;
            arrowsOnLinksCheckBox = this.FindControl<CheckBox>("ArrowsOnLinksCheckBox")!;
            dynamicLWCheckBox = this.FindControl<CheckBox>("DynamicLWCheckBox")!;
            abbreviateSciPackCheckBox = this.FindControl<CheckBox>("AbbreviateSciPackCheckBox")!;

            flagDarkModeDropDown.ItemsSource = Enum.GetValues<ThemeMode>();
            uiScaleDropDown.ItemsSource = SettingsViewModel.UiScaleLabels;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal ComboBox FlagDarkModeDropDownControl => flagDarkModeDropDown;
        internal ComboBox UiScaleDropDownControl => uiScaleDropDown;
        internal RadioButton LowLodRadioButtonControl => lowLodRadioButton;
        internal RadioButton MediumLodRadioButtonControl => mediumLodRadioButton;
        internal RadioButton HighLodRadioButtonControl => highLodRadioButton;
        internal NumericUpDown NodeCountForSimpleViewInputControl => nodeCountForSimpleViewInput;
        internal NumericUpDown IconsSizeInputControl => iconsSizeInput;
        internal CheckBox ArrowsOnLinksCheckBoxControl => arrowsOnLinksCheckBox;
        internal CheckBox DynamicLWCheckBoxControl => dynamicLWCheckBox;
        internal CheckBox AbbreviateSciPackCheckBoxControl => abbreviateSciPackCheckBox;
    }
}
