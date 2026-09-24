using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using SkiaSharp;
using System;

namespace Foreman.Mac.Views.Sheets {
    //cancel reverts from a construction-time snapshot; OK calls SaveDefaults
    public partial class ShapePropertiesSheet : SheetContent<bool> {
        private readonly ShapeAnnotationElement element;
        private readonly Snapshot original;

        private readonly ComboBox shapeTypeCombo;
        private readonly CheckBox noFillCheckBox;
        private readonly NumericUpDown fillColorRedInput;
        private readonly NumericUpDown fillColorGreenInput;
        private readonly NumericUpDown fillColorBlueInput;
        private readonly Border fillColorSwatch;
        private readonly Slider fillAlphaTrack;
        private readonly TextBlock fillAlphaLabel;
        private readonly NumericUpDown borderColorRedInput;
        private readonly NumericUpDown borderColorGreenInput;
        private readonly NumericUpDown borderColorBlueInput;
        private readonly Border borderColorSwatch;
        private readonly NumericUpDown borderWidthInput;
        private readonly Button okButton;
        private readonly Button cancelButton;

        public Action? RequestRedraw { get; set; }
        public AppSettings? Settings { get; set; }

        private readonly record struct Snapshot(
            ShapeAnnotationElement.ShapeType ShapeType, SKColor FillColor, SKColor BorderColor, int BorderWidth) {
            public static Snapshot Capture(ShapeAnnotationElement e) => new(e.CurrentShapeType, e.FillColor, e.BorderColor, e.BorderWidth);

            public void RevertOnto(ShapeAnnotationElement e) {
                e.CurrentShapeType = ShapeType;
                e.FillColor = FillColor;
                e.BorderColor = BorderColor;
                e.BorderWidth = BorderWidth;
            }
        }

        public ShapePropertiesSheet() : this(new ShapeAnnotationElement(new System.Drawing.Point(0, 0))) {
        }

        public ShapePropertiesSheet(ShapeAnnotationElement element) {
            InitializeComponent();
            this.element = element;
            original = Snapshot.Capture(element);
            Title = "Shape properties";

            shapeTypeCombo = this.FindControl<ComboBox>("ShapeTypeCombo")!;
            noFillCheckBox = this.FindControl<CheckBox>("NoFillCheckBox")!;
            fillColorRedInput = this.FindControl<NumericUpDown>("FillColorRedInput")!;
            fillColorGreenInput = this.FindControl<NumericUpDown>("FillColorGreenInput")!;
            fillColorBlueInput = this.FindControl<NumericUpDown>("FillColorBlueInput")!;
            fillColorSwatch = this.FindControl<Border>("FillColorSwatch")!;
            fillAlphaTrack = this.FindControl<Slider>("FillAlphaTrack")!;
            fillAlphaLabel = this.FindControl<TextBlock>("FillAlphaLabel")!;
            borderColorRedInput = this.FindControl<NumericUpDown>("BorderColorRedInput")!;
            borderColorGreenInput = this.FindControl<NumericUpDown>("BorderColorGreenInput")!;
            borderColorBlueInput = this.FindControl<NumericUpDown>("BorderColorBlueInput")!;
            borderColorSwatch = this.FindControl<Border>("BorderColorSwatch")!;
            borderWidthInput = this.FindControl<NumericUpDown>("BorderWidthInput")!;
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            okButton = SheetButtons.Make("OK", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(cancelButton, okButton);
            DefaultButton = okButton;

            PopulateFromElement();
            WireLiveUpdateHandlers();

            okButton.Click += (_, _) => ApplyOk();
            cancelButton.Click += (_, _) => Cancel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void PopulateFromElement() {
            shapeTypeCombo.Items.Add(ShapeAnnotationElement.ShapeType.Rectangle);
            shapeTypeCombo.Items.Add(ShapeAnnotationElement.ShapeType.Ellipse);
            shapeTypeCombo.SelectedItem = element.CurrentShapeType;

            noFillCheckBox.IsChecked = element.FillColor.Alpha == 0;
            fillColorRedInput.Value = element.FillColor.Red;
            fillColorGreenInput.Value = element.FillColor.Green;
            fillColorBlueInput.Value = element.FillColor.Blue;
            fillAlphaTrack.Value = element.FillColor.Alpha;
            fillAlphaLabel.Text = $"Fill alpha: {element.FillColor.Alpha}";

            borderColorRedInput.Value = element.BorderColor.Red;
            borderColorGreenInput.Value = element.BorderColor.Green;
            borderColorBlueInput.Value = element.BorderColor.Blue;
            borderWidthInput.Value = element.BorderWidth;

            UpdateSwatches();
        }

        private void WireLiveUpdateHandlers() {
            shapeTypeCombo.SelectionChanged += (_, _) => {
                if (shapeTypeCombo.SelectedItem is ShapeAnnotationElement.ShapeType type)
                    element.CurrentShapeType = type;
                RequestRedraw?.Invoke();
            };

            noFillCheckBox.IsCheckedChanged += (_, _) => {
                bool noFill = noFillCheckBox.IsChecked == true;
                fillAlphaTrack.IsEnabled = !noFill;
                fillAlphaTrack.Value = noFill ? 0 : 255;
                ApplyFillColor();
            };
            fillAlphaTrack.ValueChanged += (_, _) => {
                fillAlphaLabel.Text = $"Fill alpha: {(int)fillAlphaTrack.Value}";
                ApplyFillColor();
            };
            fillColorRedInput.ValueChanged += (_, _) => ApplyFillColor();
            fillColorGreenInput.ValueChanged += (_, _) => ApplyFillColor();
            fillColorBlueInput.ValueChanged += (_, _) => ApplyFillColor();

            borderColorRedInput.ValueChanged += (_, _) => ApplyBorderColor();
            borderColorGreenInput.ValueChanged += (_, _) => ApplyBorderColor();
            borderColorBlueInput.ValueChanged += (_, _) => ApplyBorderColor();
            borderWidthInput.ValueChanged += (_, _) => {
                element.BorderWidth = (int)(borderWidthInput.Value ?? 0);
                RequestRedraw?.Invoke();
            };
        }

        private void ApplyFillColor() {
            element.FillColor = new SKColor(
                (byte)(fillColorRedInput.Value ?? 0), (byte)(fillColorGreenInput.Value ?? 0), (byte)(fillColorBlueInput.Value ?? 0), (byte)fillAlphaTrack.Value);
            UpdateSwatches();
            RequestRedraw?.Invoke();
        }

        private void ApplyBorderColor() {
            element.BorderColor = new SKColor(
                (byte)(borderColorRedInput.Value ?? 0), (byte)(borderColorGreenInput.Value ?? 0), (byte)(borderColorBlueInput.Value ?? 0), 255);
            UpdateSwatches();
            RequestRedraw?.Invoke();
        }

        private void UpdateSwatches() {
            fillColorSwatch.Background = new Avalonia.Media.SolidColorBrush(new Avalonia.Media.Color(element.FillColor.Alpha, element.FillColor.Red, element.FillColor.Green, element.FillColor.Blue));
            borderColorSwatch.Background = new Avalonia.Media.SolidColorBrush(new Avalonia.Media.Color(element.BorderColor.Alpha, element.BorderColor.Red, element.BorderColor.Green, element.BorderColor.Blue));
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these): direct control access for
        //live-preview/Cancel-revert/OK-persists tests, since Window.FindControl isn't reliably reachable from
        //outside a window's own code-behind.
        internal ComboBox ShapeTypeComboControl => shapeTypeCombo;
        internal CheckBox NoFillCheckBoxControl => noFillCheckBox;
        internal NumericUpDown FillColorRedInputControl => fillColorRedInput;
        internal Slider FillAlphaTrackControl => fillAlphaTrack;
        internal NumericUpDown BorderColorRedInputControl => borderColorRedInput;
        internal NumericUpDown BorderWidthInputControl => borderWidthInput;
        internal Button OKButtonControl => okButton;
        internal Button CancelButtonControl => cancelButton;

        internal void ApplyOk() {
            ShapeAnnotationElement.SaveDefaults(element, Settings);
            Complete(true);
        }

        //Overrides SheetContent<T>.Cancel so the Cancel button and SheetHost's own Escape handling (which
        //calls this same method) both revert the live-previewed edits before completing false.
        public override void Cancel() {
            original.RevertOnto(element);
            RequestRedraw?.Invoke();
            Complete(false);
        }
    }
}
