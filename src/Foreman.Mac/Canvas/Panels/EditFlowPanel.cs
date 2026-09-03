using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman;
using Foreman.Graph;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Globalization;

namespace Foreman.Mac.Canvas.Panels {
    //Ports Controls/EditFlowPanel.cs (docs/panels-reference.md §4/§9 step 4): the non-recipe node editor -
    //rate value, fixed/auto toggle, key-node title, simple-passthrough toggle. RateOptionsTable's
    //TableLayoutPanel becomes a plain StackPanel of horizontal rows; ActualSetValue/DesiredSetValue are
    //already SelectedRateUnit-scaled by Foreman.Core (BaseNode.ActualRate/DesiredRate), so this panel needs
    //no unit-conversion logic of its own, same as upstream. RequestRedraw/RequestReposition mirror
    //TextPropertiesWindow/ShapePropertiesWindow's settable-callback pattern, standing in for upstream's
    //myGraphViewer.Invalidate()/ApplyViewportBounds() calls since this port's canvas only repaints on demand.
    public sealed class EditFlowPanel : Border {
        //Human nit: 120px only clears the spinner buttons' ~68px overhead with ~52px left for digits -
        //too tight for six-digit flow values (same fix as EditRecipePanel.FixedAssemblerInputWidth).
        private const int FixedFlowInputWidth = 170;

        private readonly GraphViewer graphViewer;
        private readonly BaseNodeController nodeController;
        private readonly INodeViewModel nodeData;

        public Action? RequestRedraw { get; set; }
        public Action? RequestReposition { get; set; }

        public TextBlock RateLabel { get; }
        public RadioButton AutoOption { get; }
        public RadioButton FixedOption { get; }
        public NumericUpDown FixedFlowInput { get; }
        public CheckBox SimplePassthroughNodesCheckBox { get; }
        public CheckBox KeyNodeCheckBox { get; }
        public TextBlock KeyNodeTitleLabel { get; }
        public TextBox KeyNodeTitleInput { get; }

        public EditFlowPanel(INodeViewModel node, GraphViewer graphViewer) {
            nodeData = node;
            if (graphViewer.Session.Editor.RequestNodeController(node.Id) is not BaseNodeController controller)
                throw new InvalidOperationException("Node has no controller.");
            nodeController = controller;
            this.graphViewer = graphViewer;

            Background = Brushes.Black;
            Focusable = true;
            Padding = new Thickness(6);

            //Upstream: RateLabel.Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold) - Avalonia's
            //FontSize is device-independent px at 96 DPI, so the point size needs the 96/72 conversion rather
            //than a bare copy (review finding 1: a bare copy is what made this label render too small).
            RateLabel = new TextBlock { Text = node.SetValueDescription, Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 10.0 * 96.0 / 72.0 };

            AutoOption = new RadioButton { Content = "Auto", GroupName = "EditFlowRateType", Foreground = Brushes.White };
            FixedOption = new RadioButton { Content = "Fixed", GroupName = "EditFlowRateType", Foreground = Brushes.White };
            FixedFlowInput = new NumericUpDown {
                Minimum = 0,
                Maximum = (decimal)(node.MaxDesiredSetValue * graphViewer.Graph.GetRateMultipler()),
                Width = FixedFlowInputWidth,
                FormatString = "N4",
            };

            //Wired here, ahead of InitializeRates() below, matching upstream's InitializeComponent ordering
            //(Designer.cs wires both before the ctor body ever touches nodeData) - InitializeRates' own
            //FixedFlowInput.Value assignment must already reach SetFixedRate so an Auto node's stale
            //DesiredSetValue gets synced to the value the panel is about to display, same as upstream.
            FixedOption.IsCheckedChanged += FixedOption_CheckChanged;
            FixedFlowInput.ValueChanged += (_, _) => SetFixedRate();

            SimplePassthroughNodesCheckBox = new CheckBox { Content = "Simplify throughput node", Foreground = Brushes.White, IsVisible = false };
            KeyNodeCheckBox = new CheckBox { Content = "Key Node", Foreground = Brushes.White };
            KeyNodeTitleLabel = new TextBlock { Text = "Title:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            KeyNodeTitleInput = new TextBox { Background = Brushes.LightGray, Foreground = Brushes.Black, Width = 114, MaxLength = 200 };

            if (node is IPassthroughNodeViewModel pNode) {
                SimplePassthroughNodesCheckBox.IsChecked = pNode.SimpleDraw;
                SimplePassthroughNodesCheckBox.IsVisible = true;
            }

            KeyNodeCheckBox.IsChecked = nodeData.KeyNode;
            KeyNodeTitleLabel.IsVisible = nodeData.KeyNode;
            KeyNodeTitleInput.IsVisible = nodeData.KeyNode;
            KeyNodeTitleInput.Text = nodeData.KeyNodeTitle;

            InitializeRates();

            var rateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            rateRow.Children.Add(AutoOption);
            rateRow.Children.Add(FixedOption);
            rateRow.Children.Add(FixedFlowInput);

            var keyNodeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            keyNodeRow.Children.Add(KeyNodeCheckBox);
            keyNodeRow.Children.Add(KeyNodeTitleLabel);
            keyNodeRow.Children.Add(KeyNodeTitleInput);

            var root = new StackPanel { Spacing = 6 };
            root.Children.Add(RateLabel);
            root.Children.Add(rateRow);
            root.Children.Add(keyNodeRow);
            root.Children.Add(SimplePassthroughNodesCheckBox);
            Child = root;

            SimplePassthroughNodesCheckBox.IsCheckedChanged += SimplePassthroughNodesCheckBox_CheckedChanged;
            KeyNodeCheckBox.IsCheckedChanged += KeyNodeCheckBox_CheckedChanged;
            //TextChanging (not TextChanged) - TextChanged defers via Dispatcher.UIThread.Post, TextChanging
            //fires synchronously as each edit lands, matching upstream's per-keystroke WinForms TextChanged.
            KeyNodeTitleInput.TextChanging += (_, _) => nodeController.SetKeyNodeTitle(KeyNodeTitleInput.Text ?? "");
        }

        private void InitializeRates() {
            if (nodeData.RateType == RateType.Auto) {
                AutoOption.IsChecked = true;
                FixedFlowInput.IsEnabled = false;
                FixedFlowInput.Value = Math.Min(FixedFlowInput.Maximum, (decimal)nodeData.ActualSetValue);
            } else {
                FixedOption.IsChecked = true;
                FixedFlowInput.IsEnabled = true;
                FixedFlowInput.Value = Math.Min(FixedFlowInput.Maximum, (decimal)nodeData.DesiredSetValue);
            }
            UpdateFixedFlowInputDecimals(FixedFlowInput);
        }

        private void SetFixedRate() {
            if (FixedFlowInput.Value is decimal value && nodeData.DesiredSetValue != (double)value) {
                nodeController.SetDesiredSetValue((double)value);
                graphViewer.Graph.UpdateNodeValues();
                RequestRedraw?.Invoke();
            }
            UpdateFixedFlowInputDecimals(FixedFlowInput);
        }

        private static void UpdateFixedFlowInputDecimals(NumericUpDown nud) {
            if (nud.Value is not decimal value)
                return;
            int decimals = Math.Min(GetDecimals(value), 4);
            nud.FormatString = $"N{decimals}";
        }

        private static int GetDecimals(decimal d, int i = 0) {
            decimal multiplied = (decimal)((double)d * Math.Pow(10, i));
            return Math.Round(multiplied) == multiplied ? i : GetDecimals(d, i + 1);
        }

        private void FixedOption_CheckChanged(object? sender, RoutedEventArgs e) {
            FixedFlowInput.IsEnabled = FixedOption.IsChecked ?? false;
            RateType updatedRateType = FixedOption.IsChecked == true ? RateType.Manual : RateType.Auto;
            if (nodeData.RateType == updatedRateType)
                return;
            nodeController.SetRateType(updatedRateType);
            graphViewer.Graph.UpdateNodeValues();
            RequestRedraw?.Invoke();
        }

        private void SimplePassthroughNodesCheckBox_CheckedChanged(object? sender, RoutedEventArgs e) {
            (nodeController as PassthroughNodeController)?.SetSimpleDraw(SimplePassthroughNodesCheckBox.IsChecked ?? false);
            RequestRedraw?.Invoke();
        }

        private void KeyNodeCheckBox_CheckedChanged(object? sender, RoutedEventArgs e) {
            nodeController.SetKeyNode(KeyNodeCheckBox.IsChecked ?? false);
            KeyNodeTitleLabel.IsVisible = nodeData.KeyNode;
            KeyNodeTitleInput.IsVisible = nodeData.KeyNode;
            KeyNodeTitleInput.Text = nodeData.KeyNodeTitle;
            RequestRedraw?.Invoke();
            RequestReposition?.Invoke();
        }

        //Offscreen demo render (same "no compositor needed" technique as IRChooserPanel.RenderOffscreen):
        //walks the real, laid-out visual tree and paints a simplified stand-in for each control, since no
        //real Skia backend is attached to render their Avalonia templates offscreen.
        public void RenderOffscreen(SKCanvas canvas, int width, int height) {
            Measure(new Size(width, height));
            Arrange(new Rect(0, 0, width, height));

            using var backgroundPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, width, height, backgroundPaint);

            PaintVisual(canvas, this, 0, 0);
        }

        private static void PaintVisual(SKCanvas canvas, Control control, float parentAbsX, float parentAbsY) {
            if (!control.IsVisible)
                return;

            Rect bounds = control.Bounds;
            float x = parentAbsX + (float)bounds.X;
            float y = parentAbsY + (float)bounds.Y;
            float w = (float)bounds.Width;
            float h = (float)bounds.Height;

            switch (control) {
                case Border border:
                    FillRect(canvas, border.Background, x, y, w, h);
                    break;
                case TextBox textBox:
                    FillRect(canvas, textBox.Background, x, y, w, h);
                    DrawText(canvas, textBox.Text, textBox.Foreground, x + 2, y, h);
                    return;
                case CheckBox checkBox:
                    DrawCheckbox(canvas, checkBox.Content as string, checkBox.IsChecked == true, checkBox.Foreground, x, y, h);
                    return;
                case RadioButton radioButton:
                    DrawCheckbox(canvas, radioButton.Content as string, radioButton.IsChecked == true, radioButton.Foreground, x, y, h);
                    return;
                case NumericUpDown numericUpDown:
                    FillRect(canvas, Brushes.DimGray, x, y, w, h);
                    DrawText(canvas, numericUpDown.Value?.ToString(numericUpDown.FormatString, CultureInfo.InvariantCulture) ?? "", Brushes.White, x + 2, y, h);
                    return;
                case TextBlock textBlock:
                    DrawText(canvas, textBlock.Text, textBlock.Foreground, x, y, h);
                    return;
            }

            foreach (Visual child in control.GetVisualChildren())
                if (child is Control childControl)
                    PaintVisual(canvas, childControl, x, y);
        }

        private static void FillRect(SKCanvas canvas, IBrush? brush, float x, float y, float w, float h) {
            if (brush is not ISolidColorBrush solid || w <= 0 || h <= 0)
                return;
            using var paint = new SKPaint { Color = ToSkColor(solid.Color), Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, w, h, paint);
        }

        private static readonly SKFont ChromeFont = new() { Size = 11 };

        private static void DrawText(SKCanvas canvas, string? text, IBrush? brush, float x, float rowY, float rowHeight) {
            if (string.IsNullOrEmpty(text))
                return;
            SKColor color = brush is ISolidColorBrush solid ? ToSkColor(solid.Color) : SKColors.White;
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            SKFontMetrics metrics = ChromeFont.Metrics;
            float baseline = rowY + (rowHeight / 2) - ((metrics.Ascent + metrics.Descent) / 2);
            canvas.DrawText(text, x, baseline, ChromeFont, paint);
        }

        private static void DrawCheckbox(SKCanvas canvas, string? label, bool isChecked, IBrush? brush, float x, float y, float h) {
            const float boxSize = 12f;
            float boxY = y + (h - boxSize) / 2;
            using (var boxPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false })
                canvas.DrawRect(x, boxY, boxSize, boxSize, boxPaint);
            if (isChecked) {
                using var fillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x + 2, boxY + 2, boxSize - 4, boxSize - 4, fillPaint);
            }
            DrawText(canvas, label, brush, x + boxSize + 4, y, h);
        }

        private static SKColor ToSkColor(Color c) => new(c.R, c.G, c.B, c.A);
    }
}
