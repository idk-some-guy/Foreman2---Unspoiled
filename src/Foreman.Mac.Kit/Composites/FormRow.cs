using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Kit.Composites {
    public class FormRow : ContentControl {
        protected override Type StyleKeyOverride => typeof(FormRow);

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<FormRow, string?>(nameof(Label));

        public static readonly StyledProperty<string?> NoteProperty =
            AvaloniaProperty.Register<FormRow, string?>(nameof(Note));

        public static readonly StyledProperty<string?> ErrorProperty =
            AvaloniaProperty.Register<FormRow, string?>(nameof(Error));

        public static readonly StyledProperty<string?> UnitProperty =
            AvaloniaProperty.Register<FormRow, string?>(nameof(Unit));

        public static readonly StyledProperty<bool> IsInspectorProperty =
            AvaloniaProperty.Register<FormRow, bool>(nameof(IsInspector));

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string? Note {
            get => GetValue(NoteProperty);
            set => SetValue(NoteProperty, value);
        }

        public string? Error {
            get => GetValue(ErrorProperty);
            set => SetValue(ErrorProperty, value);
        }

        public string? Unit {
            get => GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public bool IsInspector {
            get => GetValue(IsInspectorProperty);
            set => SetValue(IsInspectorProperty, value);
        }

        private Grid? grid;
        private TextBlock? label;
        private TextBlock? noteText;
        private double windowLabelWidth = 132;
        private double inspectorLabelWidth = 108;
        private double gutterWidth = 12;
        private double rowMinHeight = 22;
        private double captionFontSize = 11;
        private EventHandler<PointerPressedEventArgs>? labelPressed;
        private CancellationTokenSource? reflowCancellation;
        private readonly List<IDisposable> subs = [];

        static FormRow() {
            NoteProperty.Changed.AddClassHandler<FormRow>((r, _) => { r.BeginHeightReflow(); r.ApplyRowState(); r.ApplyAccessibility(); });
            ErrorProperty.Changed.AddClassHandler<FormRow>((r, _) => { r.BeginHeightReflow(); r.ApplyRowState(); r.ApplyAccessibility(); });
            LabelProperty.Changed.AddClassHandler<FormRow>((r, _) => r.ApplyAccessibility());
            ContentProperty.Changed.AddClassHandler<FormRow>((r, _) => { r.BeginHeightReflow(); r.ApplyAccessibility(); });
            IsInspectorProperty.Changed.AddClassHandler<FormRow>((r, _) => r.ApplyLabelColumnWidth());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (label is not null && labelPressed is not null)
                label.PointerPressed -= labelPressed;

            grid = e.NameScope.Find<Grid>("PART_Grid");
            label = e.NameScope.Find<TextBlock>("PART_Label");
            noteText = e.NameScope.Find<TextBlock>("PART_Note");

            if (label is not null) {
                labelPressed = OnLabelPressed;
                label.PointerPressed += labelPressed;
            }

            ApplyLabelColumnWidth();
            ApplyGutterWidth();
            ApplyRowMinHeight();
            ApplyRowState();
            ApplyNoteLineHeight();
            ApplyAccessibility();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            reflowCancellation?.Cancel();
            reflowCancellation = null;
            ClearExplicitHeight();
            Unsubscribe();
        }

        protected override Size MeasureOverride(Size availableSize) {
            Size desired = base.MeasureOverride(availableSize);
            double overflow = PredictedLabelOverflow();
            return overflow > 0 ? new Size(desired.Width, desired.Height + overflow) : desired;
        }

        protected override Size ArrangeOverride(Size finalSize) {
            Size result = base.ArrangeOverride(finalSize);
            ApplyLabelBaseline();
            return result;
        }

        //Clicking the label moves focus to the row's control (addendum's keyboard row).
        private void OnLabelPressed(object? sender, PointerPressedEventArgs e) {
            if (label is null || !e.GetCurrentPoint(label).Properties.IsLeftButtonPressed)
                return;
            if (Content is Control contentControl)
                contentControl.Focus(NavigationMethod.Pointer);
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.FormLabelColumn").Subscribe(new ResourceSinks.DoubleSink(v => {
                windowLabelWidth = v;
                ApplyLabelColumnWidth();
            }, windowLabelWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Size.FormLabelColumnInspector").Subscribe(new ResourceSinks.DoubleSink(v => {
                inspectorLabelWidth = v;
                ApplyLabelColumnWidth();
            }, inspectorLabelWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Space.Space3").Subscribe(new ResourceSinks.DoubleSink(v => {
                gutterWidth = v;
                ApplyGutterWidth();
            }, gutterWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Size.ControlHeightRegular").Subscribe(new ResourceSinks.DoubleSink(v => {
                rowMinHeight = v;
                ApplyRowMinHeight();
            }, rowMinHeight)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Caption.FontSize").Subscribe(new ResourceSinks.DoubleSink(v => {
                captionFontSize = v;
                ApplyNoteLineHeight();
            }, captionFontSize)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void ApplyLabelColumnWidth() {
            if (grid is null)
                return;
            grid.ColumnDefinitions[0].Width = new GridLength(IsInspector ? inspectorLabelWidth : windowLabelWidth);
        }

        private void ApplyGutterWidth() {
            if (grid is null)
                return;
            grid.ColumnDefinitions[1].Width = new GridLength(gutterWidth);
        }

        private void ApplyRowMinHeight() {
            if (grid is null)
                return;
            grid.RowDefinitions[0].MinHeight = rowMinHeight;
        }

        private void ApplyNoteLineHeight() {
            if (noteText is null)
                return;
            noteText.LineHeight = captionFontSize * 1.35;
        }

        //Baseline offset lives in a RenderTransform, not Margin, so recomputing it never re-triggers layout.
        private void ApplyLabelBaseline() {
            if (label is null || grid is null)
                return;
            TextLine? labelLine = FirstLine(label);
            if (labelLine is null)
                return;

            Point gridOrigin = grid.TranslatePoint(new Point(0, 0), this) ?? default;
            double labelBaselineY = gridOrigin.Y + label.Bounds.Y + labelLine.Baseline;

            double? controlBaselineY = ControlFirstLineBaselineY(labelLine);
            if (controlBaselineY is not double cb)
                return;

            label.RenderTransform = new TranslateTransform(0, cb - labelBaselineY);
        }

        private double? ControlFirstLineBaselineY(TextLine labelLine) {
            if (Content is not Control content)
                return null;

            Control? textHost = content is TextBlock or TextPresenter && content.IsEffectivelyVisible
                ? content
                : content.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is TextBlock or TextPresenter && c.IsEffectivelyVisible);

            if (textHost is not null && FirstLine(textHost) is TextLine hostLine)
                return textHost.TranslatePoint(new Point(0, hostLine.Baseline), this)?.Y;

            //No text of its own: center an assumed line (the label's own metrics) on the control's own arranged height.
            double fallbackLocal = (content.Bounds.Height - labelLine.Height) / 2 + labelLine.Baseline;
            return content.TranslatePoint(new Point(0, fallbackLocal), this)?.Y;
        }

        //Mirrors ControlFirstLineBaselineY's fallback with DesiredSize standing in for Bounds pre-arrange.
        private double PredictedLabelOverflow() {
            if (label is null || Content is not Control content)
                return 0;
            TextLine? labelLine = FirstLine(label);
            if (labelLine is null)
                return 0;

            Control? textHost = content is TextBlock or TextPresenter && content.IsEffectivelyVisible
                ? content
                : content.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is TextBlock or TextPresenter && c.IsEffectivelyVisible);

            double controlBaselineLocal;
            if (textHost is not null && FirstLine(textHost) is TextLine hostLine) {
                controlBaselineLocal = hostLine.Baseline;
            } else {
                double contentHeight = content.Bounds.Height > 0 ? content.Bounds.Height : content.DesiredSize.Height;
                controlBaselineLocal = (contentHeight - labelLine.Height) / 2 + labelLine.Baseline;
            }

            return Math.Max(0, controlBaselineLocal - labelLine.Baseline);
        }

        private static TextLine? FirstLine(Control control) => control switch {
            TextBlock tb => tb.TextLayout is { TextLines.Count: > 0 } layout ? layout.TextLines[0] : null,
            TextPresenter tp => tp.TextLayout is { TextLines.Count: > 0 } layout ? layout.TextLines[0] : null,
            _ => null,
        };

        private void BeginHeightReflow() {
            if (Motion.ReduceMotion || grid is null || VisualRoot is null)
                return;
            double oldHeight = Bounds.Height;
            if (oldHeight <= 0)
                return;

            //Height starts at NaN (auto), which a transition can't animate into - suspend it for this one pin.
            Transitions? transitions = Transitions;
            Transitions = null;
            Height = oldHeight;
            Transitions = transitions;

            reflowCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            reflowCancellation = cancellation;
            Dispatcher.UIThread.Post(() => _ = RunHeightReflow(oldHeight, cancellation.Token), DispatcherPriority.Loaded);
        }

        private async Task RunHeightReflow(double oldHeight, CancellationToken token) {
            if (grid is null || token.IsCancellationRequested)
                return;
            grid.Measure(new Size(Bounds.Width, double.PositiveInfinity));
            double newHeight = grid.DesiredSize.Height;

            var reflow = new Animation {
                Duration = Motion.InspectorReflowDuration,
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(HeightProperty, oldHeight) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(HeightProperty, newHeight) } },
                },
            };

            try {
                await reflow.RunAsync(this, token);
            } catch (OperationCanceledException) {
                return;
            }

            if (!token.IsCancellationRequested)
                ClearExplicitHeight();
        }

        private void ClearExplicitHeight() {
            reflowCancellation = null;
            Transitions? transitions = Transitions;
            Transitions = null;
            Height = double.NaN;
            Transitions = transitions;
        }

        private void ApplyAccessibility() {
            if (Content is not Control contentControl)
                return;
            AutomationProperties.SetName(contentControl, Label);
            AutomationProperties.SetHelpText(contentControl, !string.IsNullOrEmpty(Error) ? Error : Note);
        }

        //Error replaces the note rather than stacking with it (addendum §01).
        private void ApplyRowState() {
            bool hasError = !string.IsNullOrEmpty(Error);
            PseudoClasses.Set(":error", hasError);
            PseudoClasses.Set(":note", !hasError && !string.IsNullOrEmpty(Note));
        }
    }
}
