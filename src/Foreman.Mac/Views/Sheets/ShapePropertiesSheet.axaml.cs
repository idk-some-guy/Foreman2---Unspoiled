using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using SkiaSharp;
using System;
using Slider = Foreman.Mac.Kit.Controls.Slider;

namespace Foreman.Mac.Views.Sheets {
    public partial class ShapePropertiesSheet : SheetContent<bool> {
        private readonly ShapeAnnotationElement element;
        private readonly Snapshot original;

        private readonly Segmented shapeSegments;
        private readonly ColorWell fillWell;
        private readonly Slider opacitySlider;
        private readonly ColorWell borderWell;
        private readonly Stepper borderWidthStepper;
        private readonly Button okButton;
        private readonly Button cancelButton;
        private bool syncingOpacity;

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

            shapeSegments = this.FindControl<Segmented>("ShapeSegments")!;
            fillWell = this.FindControl<ColorWell>("FillWell")!;
            opacitySlider = this.FindControl<Slider>("OpacitySlider")!;
            borderWell = this.FindControl<ColorWell>("BorderWell")!;
            borderWidthStepper = this.FindControl<Stepper>("BorderWidthStepper")!;
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

        private static Color ToAvalonia(SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        private static SKColor ToSkia(Color color) => new(color.R, color.G, color.B, color.A);
        private static double Percent(byte alpha) => Math.Round(alpha / 2.55);
        private static byte Alpha(double percent) => (byte)Math.Round(percent * 2.55);

        private void PopulateFromElement() {
            shapeSegments.Items.Add("Rectangle");
            shapeSegments.Items.Add("Ellipse");
            shapeSegments.SelectedIndex = element.CurrentShapeType == ShapeAnnotationElement.ShapeType.Ellipse ? 1 : 0;
            fillWell.Color = ToAvalonia(element.FillColor);
            opacitySlider.Value = Percent(element.FillColor.Alpha);
            borderWell.Color = ToAvalonia(element.BorderColor);
            borderWidthStepper.Value = element.BorderWidth;
        }

        private void WireLiveUpdateHandlers() {
            shapeSegments.PropertyChanged += (_, e) => {
                if (e.Property != Segmented.SelectedIndexProperty)
                    return;
                element.CurrentShapeType = shapeSegments.SelectedIndex == 1 ? ShapeAnnotationElement.ShapeType.Ellipse : ShapeAnnotationElement.ShapeType.Rectangle;
                RequestRedraw?.Invoke();
            };
            fillWell.PropertyChanged += (_, e) => {
                if (e.Property != ColorWell.ColorProperty)
                    return;
                element.FillColor = ToSkia(fillWell.Color);
                syncingOpacity = true;
                opacitySlider.Value = Percent(fillWell.Color.A);
                syncingOpacity = false;
                RequestRedraw?.Invoke();
            };
            opacitySlider.PropertyChanged += (_, e) => {
                if (e.Property != Slider.ValueProperty || syncingOpacity)
                    return;
                Color fill = fillWell.Color;
                fillWell.Color = Color.FromArgb(Alpha(opacitySlider.Value), fill.R, fill.G, fill.B);
            };
            borderWell.PropertyChanged += (_, e) => {
                if (e.Property != ColorWell.ColorProperty)
                    return;
                element.BorderColor = ToSkia(borderWell.Color).WithAlpha(255);
                RequestRedraw?.Invoke();
            };
            borderWidthStepper.PropertyChanged += (_, e) => {
                if (e.Property != Stepper.ValueProperty)
                    return;
                element.BorderWidth = (int)borderWidthStepper.Value;
                RequestRedraw?.Invoke();
            };
        }

        internal Segmented ShapeSegmentsControl => shapeSegments;
        internal ColorWell FillWellControl => fillWell;
        internal Slider OpacitySliderControl => opacitySlider;
        internal ColorWell BorderWellControl => borderWell;
        internal Stepper BorderWidthStepperControl => borderWidthStepper;
        internal Button OKButtonControl => okButton;
        internal Button CancelButtonControl => cancelButton;

        internal void ApplyOk() {
            ShapeAnnotationElement.SaveDefaults(element, Settings);
            Complete(true);
        }

        public override void Cancel() {
            original.RevertOnto(element);
            RequestRedraw?.Invoke();
            Complete(false);
        }
    }
}
