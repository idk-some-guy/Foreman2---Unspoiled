using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using SkiaSharp;
using System;

namespace Foreman.Mac.Views {
    //Ports Forms/TextPropertiesForm.cs as a real Avalonia window (reference §6/§10): every field applies live
    //(RebuildGdiObjects/FitBoxToTextAtCenter + RequestRedraw), Cancel reverts from a snapshot taken at
    //construction, OK calls TextAnnotationElement.SaveDefaults. Divergence: WinForms' FontDialog/ColorDialog
    //have no Avalonia equivalent in this port's dependency set, so font family/size/style and RGB color are
    //inline controls instead of modal pickers - alpha is fixed at fully opaque for both colors (matching
    //ColorDialog's own no-alpha default) except BackColor, whose alpha is the TransparentCheckBox's job.
    public partial class TextPropertiesWindow : Window {
        private static readonly string[] CuratedFontFamilies = ["Segoe UI", "Helvetica Neue", "Arial", "Times New Roman", "Courier New"];

        private readonly TextAnnotationElement element;
        private readonly Snapshot original;

        private readonly TextBox textInput;
        private readonly TextBlock fontPreviewLabel;
        private readonly ComboBox fontFamilyCombo;
        private readonly NumericUpDown fontSizeInput;
        private readonly CheckBox boldCheckBox;
        private readonly CheckBox italicCheckBox;
        private readonly RadioButton alignLeftRadio;
        private readonly RadioButton alignCenterRadio;
        private readonly RadioButton alignRightRadio;
        private readonly NumericUpDown textColorRedInput;
        private readonly NumericUpDown textColorGreenInput;
        private readonly NumericUpDown textColorBlueInput;
        private readonly Border textColorSwatch;
        private readonly CheckBox transparentCheckBox;
        private readonly NumericUpDown backColorRedInput;
        private readonly NumericUpDown backColorGreenInput;
        private readonly NumericUpDown backColorBlueInput;
        private readonly Border backColorSwatch;
        private readonly Button okButton;
        private readonly Button cancelButton;

        private bool suppressLiveUpdates = true;

        public Action? RequestRedraw { get; set; }
        public AppSettings? Settings { get; set; }

        private readonly record struct Snapshot(
            string Text, string FontFamily, float FontSize, int FontStyleFlags, SKColor TextColor, SKColor BackColor, int TextAlign) {
            public static Snapshot Capture(TextAnnotationElement e) =>
                new(e.Text, e.FontFamily, e.FontSize, e.FontStyleFlags, e.TextColor, e.BackColor, e.TextAlign);

            public void RevertOnto(TextAnnotationElement e) {
                e.Text = Text;
                e.FontFamily = FontFamily;
                e.FontSize = FontSize;
                e.FontStyleFlags = FontStyleFlags;
                e.TextColor = TextColor;
                e.BackColor = BackColor;
                e.TextAlign = TextAlign;
                e.RebuildGdiObjects();
                e.FitBoxToTextAtCenter();
            }
        }

        public TextPropertiesWindow() : this(new TextAnnotationElement(new System.Drawing.Point(0, 0))) {
        }

        public TextPropertiesWindow(TextAnnotationElement element) {
            InitializeComponent();
            this.element = element;
            original = Snapshot.Capture(element);

            textInput = this.FindControl<TextBox>("TextInput")!;
            fontPreviewLabel = this.FindControl<TextBlock>("FontPreviewLabel")!;
            fontFamilyCombo = this.FindControl<ComboBox>("FontFamilyCombo")!;
            fontSizeInput = this.FindControl<NumericUpDown>("FontSizeInput")!;
            boldCheckBox = this.FindControl<CheckBox>("BoldCheckBox")!;
            italicCheckBox = this.FindControl<CheckBox>("ItalicCheckBox")!;
            alignLeftRadio = this.FindControl<RadioButton>("AlignLeftRadio")!;
            alignCenterRadio = this.FindControl<RadioButton>("AlignCenterRadio")!;
            alignRightRadio = this.FindControl<RadioButton>("AlignRightRadio")!;
            textColorRedInput = this.FindControl<NumericUpDown>("TextColorRedInput")!;
            textColorGreenInput = this.FindControl<NumericUpDown>("TextColorGreenInput")!;
            textColorBlueInput = this.FindControl<NumericUpDown>("TextColorBlueInput")!;
            textColorSwatch = this.FindControl<Border>("TextColorSwatch")!;
            transparentCheckBox = this.FindControl<CheckBox>("TransparentCheckBox")!;
            backColorRedInput = this.FindControl<NumericUpDown>("BackColorRedInput")!;
            backColorGreenInput = this.FindControl<NumericUpDown>("BackColorGreenInput")!;
            backColorBlueInput = this.FindControl<NumericUpDown>("BackColorBlueInput")!;
            backColorSwatch = this.FindControl<Border>("BackColorSwatch")!;
            okButton = this.FindControl<Button>("OKButton")!;
            cancelButton = this.FindControl<Button>("CancelButton")!;

            PopulateFromElement();
            WireLiveUpdateHandlers();
            suppressLiveUpdates = false;

            okButton.Click += (_, _) => ApplyOk();
            cancelButton.Click += (_, _) => ApplyCancel();
            Opened += (_, _) => { textInput.Focus(); textInput.SelectAll(); };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void PopulateFromElement() {
            foreach (string family in CuratedFontFamilies)
                fontFamilyCombo.Items.Add(family);
            if (Array.IndexOf(CuratedFontFamilies, element.FontFamily) < 0)
                fontFamilyCombo.Items.Add(element.FontFamily);

            textInput.Text = element.Text;
            fontFamilyCombo.SelectedItem = element.FontFamily;
            fontSizeInput.Value = (decimal)element.FontSize;
            boldCheckBox.IsChecked = (element.FontStyleFlags & 1) != 0;
            italicCheckBox.IsChecked = (element.FontStyleFlags & 2) != 0;
            SetAlignRadio(element.TextAlign);
            SetColorInputs(textColorRedInput, textColorGreenInput, textColorBlueInput, element.TextColor);
            transparentCheckBox.IsChecked = element.BackColor.Alpha == 0;
            SetColorInputs(backColorRedInput, backColorGreenInput, backColorBlueInput, element.BackColor);
            backColorRedInput.IsEnabled = backColorGreenInput.IsEnabled = backColorBlueInput.IsEnabled = element.BackColor.Alpha != 0;
            UpdateSwatches();
            UpdateFontPreview();
        }

        private static void SetColorInputs(NumericUpDown r, NumericUpDown g, NumericUpDown b, SKColor color) {
            r.Value = color.Red;
            g.Value = color.Green;
            b.Value = color.Blue;
        }

        private void SetAlignRadio(int align) {
            alignLeftRadio.IsChecked = align == 0;
            alignCenterRadio.IsChecked = align == 1;
            alignRightRadio.IsChecked = align == 2;
        }

        private void WireLiveUpdateHandlers() {
            textInput.TextChanged += (_, _) => { element.Text = textInput.Text ?? ""; RebuildAndRefit(); };
            fontFamilyCombo.SelectionChanged += (_, _) => { element.FontFamily = fontFamilyCombo.SelectedItem as string ?? element.FontFamily; RebuildAndRefit(); UpdateFontPreview(); };
            fontSizeInput.ValueChanged += (_, _) => { element.FontSize = (float)(fontSizeInput.Value ?? 14m); RebuildAndRefit(); UpdateFontPreview(); };
            boldCheckBox.IsCheckedChanged += (_, _) => { ApplyFontStyleFlags(); RebuildAndRefit(); UpdateFontPreview(); };
            italicCheckBox.IsCheckedChanged += (_, _) => { ApplyFontStyleFlags(); RebuildAndRefit(); UpdateFontPreview(); };

            alignLeftRadio.IsCheckedChanged += (_, _) => ApplyAlign();
            alignCenterRadio.IsCheckedChanged += (_, _) => ApplyAlign();
            alignRightRadio.IsCheckedChanged += (_, _) => ApplyAlign();

            textColorRedInput.ValueChanged += (_, _) => ApplyTextColor();
            textColorGreenInput.ValueChanged += (_, _) => ApplyTextColor();
            textColorBlueInput.ValueChanged += (_, _) => ApplyTextColor();

            transparentCheckBox.IsCheckedChanged += (_, _) => ApplyBackColor();
            backColorRedInput.ValueChanged += (_, _) => ApplyBackColor();
            backColorGreenInput.ValueChanged += (_, _) => ApplyBackColor();
            backColorBlueInput.ValueChanged += (_, _) => ApplyBackColor();
        }

        private void ApplyFontStyleFlags() {
            if (suppressLiveUpdates)
                return;
            element.FontStyleFlags = (boldCheckBox.IsChecked == true ? 1 : 0) | (italicCheckBox.IsChecked == true ? 2 : 0);
        }

        private void ApplyAlign() {
            if (suppressLiveUpdates)
                return;
            element.TextAlign = alignLeftRadio.IsChecked == true ? 0 : alignRightRadio.IsChecked == true ? 2 : 1;
            RequestRedraw?.Invoke();
        }

        private void ApplyTextColor() {
            if (suppressLiveUpdates)
                return;
            element.TextColor = new SKColor((byte)(textColorRedInput.Value ?? 0), (byte)(textColorGreenInput.Value ?? 0), (byte)(textColorBlueInput.Value ?? 0), 255);
            UpdateSwatches();
            RequestRedraw?.Invoke();
        }

        private void ApplyBackColor() {
            if (suppressLiveUpdates)
                return;
            bool transparent = transparentCheckBox.IsChecked == true;
            backColorRedInput.IsEnabled = backColorGreenInput.IsEnabled = backColorBlueInput.IsEnabled = !transparent;
            byte alpha = (byte)(transparent ? 0 : 255);
            element.BackColor = new SKColor((byte)(backColorRedInput.Value ?? 0), (byte)(backColorGreenInput.Value ?? 0), (byte)(backColorBlueInput.Value ?? 0), alpha);
            UpdateSwatches();
            RequestRedraw?.Invoke();
        }

        private void RebuildAndRefit() {
            if (suppressLiveUpdates)
                return;
            element.RebuildGdiObjects();
            element.FitBoxToTextAtCenter();
            RequestRedraw?.Invoke();
        }

        private void UpdateSwatches() {
            textColorSwatch.Background = new Avalonia.Media.SolidColorBrush(new Avalonia.Media.Color(element.TextColor.Alpha, element.TextColor.Red, element.TextColor.Green, element.TextColor.Blue));
            backColorSwatch.Background = new Avalonia.Media.SolidColorBrush(new Avalonia.Media.Color(element.BackColor.Alpha, element.BackColor.Red, element.BackColor.Green, element.BackColor.Blue));
        }

        private void UpdateFontPreview() {
            fontPreviewLabel.FontFamily = new Avalonia.Media.FontFamily(element.FontFamily);
            fontPreviewLabel.FontSize = element.FontSize;
            fontPreviewLabel.FontWeight = (element.FontStyleFlags & 1) != 0 ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
            fontPreviewLabel.FontStyle = (element.FontStyleFlags & 2) != 0 ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal;
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these) - see ShapePropertiesWindow's
        //equivalent block for why these exist instead of external FindControl calls.
        internal TextBox TextInputControl => textInput;
        internal NumericUpDown FontSizeInputControl => fontSizeInput;
        internal RadioButton AlignRightRadioControl => alignRightRadio;
        internal NumericUpDown TextColorRedInputControl => textColorRedInput;
        internal CheckBox TransparentCheckBoxControl => transparentCheckBox;

        //Ports the OK path (reference §6): SaveDefaults persists the current values as the session/AppSettings
        //defaults, then closes with a true result.
        internal void ApplyOk() {
            TextAnnotationElement.SaveDefaults(element, Settings);
            Close(true);
        }

        //Ports the Cancel path (reference §6): reverts every field from the construction-time snapshot before
        //closing, so the live-previewed edits never stick.
        internal void ApplyCancel() {
            original.RevertOnto(element);
            RequestRedraw?.Invoke();
            Close(false);
        }
    }
}
