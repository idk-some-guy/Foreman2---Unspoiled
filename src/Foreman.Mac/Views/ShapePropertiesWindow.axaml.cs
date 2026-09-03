using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using SkiaSharp;
using System;

namespace Foreman.Mac.Views {
    //Ports Forms/ShapePropertiesForm.cs as a real Avalonia window (reference §6/§10): every field applies
    //live, Cancel reverts from a construction-time snapshot, OK calls ShapeAnnotationElement.SaveDefaults.
    //Divergence: WinForms' ColorDialog has no Avalonia equivalent in this port's dependency set, so fill/
    //border color are inline RGB controls instead of a modal picker; border color has no separate alpha field
    //(ColorDialog itself has none either) while fill alpha stays its own track, matching the reference's field
    //inventory.
    public partial class ShapePropertiesWindow : Window {
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

        public ShapePropertiesWindow() : this(new ShapeAnnotationElement(new System.Drawing.Point(0, 0))) {
        }

        public ShapePropertiesWindow(ShapeAnnotationElement element) {
            InitializeComponent();
            this.element = element;
            original = Snapshot.Capture(element);

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
            okButton = this.FindControl<Button>("OKButton")!;
            cancelButton = this.FindControl<Button>("CancelButton")!;

            PopulateFromElement();
            WireLiveUpdateHandlers();

            okButton.Click += (_, _) => ApplyOk();
            cancelButton.Click += (_, _) => ApplyCancel();
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

        internal void ApplyOk() {
            ShapeAnnotationElement.SaveDefaults(element, Settings);
            Close(true);
        }

        internal void ApplyCancel() {
            original.RevertOnto(element);
            RequestRedraw?.Invoke();
            Close(false);
        }
    }
}
