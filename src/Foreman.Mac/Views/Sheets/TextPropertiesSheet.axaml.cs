using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using SkiaSharp;
using System;

namespace Foreman.Mac.Views.Sheets {
    public partial class TextPropertiesSheet : SheetContent<bool> {
        private static readonly string[] CuratedFontFamilies = ["Segoe UI", "Helvetica Neue", "Arial", "Times New Roman", "Courier New"];
        private static readonly string[] AlignmentNames = ["Left", "Center", "Right"];

        private readonly TextAnnotationElement element;
        private readonly Snapshot original;

        private readonly TextBox textInput;
        private readonly PopUp fontPopUp;
        private readonly Stepper fontSizeStepper;
        private readonly CheckBox boldCheckBox;
        private readonly CheckBox italicCheckBox;
        private readonly Segmented alignmentSegments;
        private readonly ColorWell textColorWell;
        private readonly ColorWell backColorWell;
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

        public TextPropertiesSheet() : this(new TextAnnotationElement(new System.Drawing.Point(0, 0))) {
        }

        public TextPropertiesSheet(TextAnnotationElement element) {
            InitializeComponent();
            this.element = element;
            original = Snapshot.Capture(element);
            Title = "Text properties";

            textInput = this.FindControl<TextBox>("TextInput")!;
            fontPopUp = this.FindControl<PopUp>("FontPopUp")!;
            fontSizeStepper = this.FindControl<Stepper>("FontSizeStepper")!;
            boldCheckBox = this.FindControl<CheckBox>("BoldCheckBox")!;
            italicCheckBox = this.FindControl<CheckBox>("ItalicCheckBox")!;
            alignmentSegments = this.FindControl<Segmented>("AlignmentSegments")!;
            textColorWell = this.FindControl<ColorWell>("TextColorWell")!;
            backColorWell = this.FindControl<ColorWell>("BackColorWell")!;
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            okButton = SheetButtons.Make("OK", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(cancelButton, okButton);
            DefaultButton = okButton;

            PopulateFromElement();
            WireLiveUpdateHandlers();
            suppressLiveUpdates = false;

            okButton.Click += (_, _) => ApplyOk();
            cancelButton.Click += (_, _) => Cancel();
            Loaded += (_, _) => { textInput.Focus(); textInput.SelectAll(); };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private static Color ToAvalonia(SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        private static SKColor ToSkia(Color color) => new(color.R, color.G, color.B, color.A);

        private void PopulateFromElement() {
            foreach (string family in CuratedFontFamilies)
                fontPopUp.Items.Add(family);
            if (Array.IndexOf(CuratedFontFamilies, element.FontFamily) < 0)
                fontPopUp.Items.Add(element.FontFamily);
            foreach (string family in fontPopUp.Items)
                fontPopUp.ItemFontFamilies.Add(new FontFamily(family));
            foreach (string align in AlignmentNames)
                alignmentSegments.Items.Add(align);

            textInput.Text = element.Text;
            fontPopUp.SelectedItem = element.FontFamily;
            fontSizeStepper.Value = element.FontSize;
            boldCheckBox.IsChecked = (element.FontStyleFlags & 1) != 0;
            italicCheckBox.IsChecked = (element.FontStyleFlags & 2) != 0;
            alignmentSegments.SelectedIndex = Math.Clamp(element.TextAlign, 0, AlignmentNames.Length - 1);
            textColorWell.Color = ToAvalonia(element.TextColor);
            backColorWell.Color = ToAvalonia(element.BackColor);
        }

        private void WireLiveUpdateHandlers() {
            textInput.TextChanged += (_, _) => { element.Text = textInput.Text ?? ""; RebuildAndRefit(); };
            fontPopUp.PropertyChanged += (_, e) => {
                if (e.Property == PopUp.SelectedItemProperty && fontPopUp.SelectedItem is { } family) {
                    element.FontFamily = family;
                    RebuildAndRefit();
                }
            };
            fontSizeStepper.PropertyChanged += (_, e) => {
                if (e.Property == Stepper.ValueProperty) {
                    element.FontSize = (float)fontSizeStepper.Value;
                    RebuildAndRefit();
                }
            };
            boldCheckBox.IsCheckedChanged += (_, _) => { ApplyFontStyleFlags(); RebuildAndRefit(); };
            italicCheckBox.IsCheckedChanged += (_, _) => { ApplyFontStyleFlags(); RebuildAndRefit(); };
            alignmentSegments.PropertyChanged += (_, e) => {
                if (e.Property == Segmented.SelectedIndexProperty && !suppressLiveUpdates) {
                    element.TextAlign = alignmentSegments.SelectedIndex;
                    RequestRedraw?.Invoke();
                }
            };
            textColorWell.PropertyChanged += (_, e) => {
                if (e.Property == ColorWell.ColorProperty && !suppressLiveUpdates) {
                    element.TextColor = ToSkia(textColorWell.Color).WithAlpha(255);
                    RebuildAndRefit();
                }
            };
            backColorWell.PropertyChanged += (_, e) => {
                if (e.Property == ColorWell.ColorProperty && !suppressLiveUpdates) {
                    element.BackColor = ToSkia(backColorWell.Color);
                    RebuildAndRefit();
                }
            };
        }

        private void ApplyFontStyleFlags() {
            if (suppressLiveUpdates)
                return;
            element.FontStyleFlags = (boldCheckBox.IsChecked == true ? 1 : 0) | (italicCheckBox.IsChecked == true ? 2 : 0);
        }

        private void RebuildAndRefit() {
            if (suppressLiveUpdates)
                return;
            element.RebuildGdiObjects();
            element.FitBoxToTextAtCenter();
            RequestRedraw?.Invoke();
        }

        internal TextBox TextInputControl => textInput;
        internal PopUp FontPopUpControl => fontPopUp;
        internal Stepper FontSizeStepperControl => fontSizeStepper;
        internal CheckBox BoldCheckBoxControl => boldCheckBox;
        internal CheckBox ItalicCheckBoxControl => italicCheckBox;
        internal Segmented AlignmentSegmentsControl => alignmentSegments;
        internal ColorWell TextColorWellControl => textColorWell;
        internal ColorWell BackColorWellControl => backColorWell;
        internal Button OKButtonControl => okButton;
        internal Button CancelButtonControl => cancelButton;

        internal void ApplyOk() {
            TextAnnotationElement.SaveDefaults(element, Settings);
            Complete(true);
        }

        public override void Cancel() {
            original.RevertOnto(element);
            RequestRedraw?.Invoke();
            Complete(false);
        }
    }
}
