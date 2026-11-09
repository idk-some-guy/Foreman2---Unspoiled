using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Foreman.Mac.Kit.Composites {
    public enum FormRowLabelColumn { Window, Inspector, Popover }

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

        public static readonly StyledProperty<bool> NoteIsValueProperty =
            AvaloniaProperty.Register<FormRow, bool>(nameof(NoteIsValue));

        public static readonly StyledProperty<string?> NoteSuffixProperty =
            AvaloniaProperty.Register<FormRow, string?>(nameof(NoteSuffix));

        public static readonly StyledProperty<bool> ErrorIsWarningProperty =
            AvaloniaProperty.Register<FormRow, bool>(nameof(ErrorIsWarning));

        public static readonly StyledProperty<bool> NoteIsWarningProperty =
            AvaloniaProperty.Register<FormRow, bool>(nameof(NoteIsWarning));

        public static readonly StyledProperty<FormRowLabelColumn> LabelColumnProperty =
            AvaloniaProperty.Register<FormRow, FormRowLabelColumn>(nameof(LabelColumn));

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

        public FormRowLabelColumn LabelColumn {
            get => GetValue(LabelColumnProperty);
            set => SetValue(LabelColumnProperty, value);
        }

        public bool NoteIsValue {
            get => GetValue(NoteIsValueProperty);
            set => SetValue(NoteIsValueProperty, value);
        }

        public string? NoteSuffix {
            get => GetValue(NoteSuffixProperty);
            set => SetValue(NoteSuffixProperty, value);
        }

        public bool ErrorIsWarning {
            get => GetValue(ErrorIsWarningProperty);
            set => SetValue(ErrorIsWarningProperty, value);
        }

        public bool NoteIsWarning {
            get => GetValue(NoteIsWarningProperty);
            set => SetValue(NoteIsWarningProperty, value);
        }

        private Grid? grid;
        private FormRowControlPanel? controlColumn;
        private Control? watchedContent;
        private TextBlock? label;
        private TextBlock? noteText;
        private TextBlock? noteSuffixText;
        private double windowLabelWidth = 132;
        private double inspectorLabelWidth = 108;
        private double popoverLabelWidth = 86;
        private double gutterWidth = 12;
        private double unitGap = 8;
        private double rowMinHeight = 22;
        private double captionFontSize = 11;
        private EventHandler<PointerPressedEventArgs>? labelPressed;
        private CancellationTokenSource? reflowCancellation;
        private readonly List<IDisposable> subs = [];

        static FormRow() {
            NoteProperty.Changed.AddClassHandler<FormRow>((r, _) => { r.BeginHeightReflow(); r.ApplyRowState(); r.ApplyAccessibility(); });
            ErrorProperty.Changed.AddClassHandler<FormRow>((r, _) => { r.BeginHeightReflow(); r.ApplyRowState(); r.ApplyAccessibility(); });
            LabelProperty.Changed.AddClassHandler<FormRow>((r, _) => r.ApplyAccessibility());
            ContentProperty.Changed.AddClassHandler<FormRow>((r, _) => { r.BeginHeightReflow(); r.ApplyAccessibility(); r.WatchContent(); });
            LabelColumnProperty.Changed.AddClassHandler<FormRow>((r, _) => r.ApplyLabelColumnWidth());
            NoteIsValueProperty.Changed.AddClassHandler<FormRow>((r, _) => r.ApplyRowState());
            ErrorIsWarningProperty.Changed.AddClassHandler<FormRow>((r, _) => r.ApplyRowState());
            NoteIsWarningProperty.Changed.AddClassHandler<FormRow>((r, _) => r.ApplyRowState());
            NoteSuffixProperty.Changed.AddClassHandler<FormRow>((r, _) => { r.BeginHeightReflow(); r.ApplyNoteSuffixText(); });
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (label is not null && labelPressed is not null)
                label.PointerPressed -= labelPressed;

            grid = e.NameScope.Find<Grid>("PART_Grid");
            controlColumn = e.NameScope.Find<FormRowControlPanel>("PART_ControlColumn");
            label = e.NameScope.Find<TextBlock>("PART_Label");
            noteText = e.NameScope.Find<TextBlock>("PART_Note");
            noteSuffixText = e.NameScope.Find<TextBlock>("PART_NoteSuffix");

            if (label is not null) {
                labelPressed = OnLabelPressed;
                label.PointerPressed += labelPressed;
            }

            ApplyLabelColumnWidth();
            ApplyGutterWidth();
            ApplyUnitGap();
            ApplyControlStretch();
            ApplyRowMinHeight();
            ApplyRowState();
            ApplyNoteLineHeight();
            ApplyNoteSuffixText();
            ApplyAccessibility();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
            WatchContent();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            CompositeMotion.ClearExplicitHeight(this, ref reflowCancellation);
            Unsubscribe();
            UnwatchContent();
        }

        private void WatchContent() {
            UnwatchContent();
            if (Content is Control content && this.IsAttachedToVisualTree()) {
                watchedContent = content;
                content.PropertyChanged += OnContentPropertyChanged;
            }
            ApplyControlStretch();
        }

        private void UnwatchContent() {
            if (watchedContent is not null)
                watchedContent.PropertyChanged -= OnContentPropertyChanged;
            watchedContent = null;
        }

        private void OnContentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == HorizontalAlignmentProperty || e.Property == WidthProperty)
                ApplyControlStretch();
        }

        private void ApplyControlStretch() {
            if (controlColumn is null)
                return;
            controlColumn.Fill = Content is Control content && content.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch && double.IsNaN(content.Width);
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
            subs.Add(this.GetResourceObservable("Foreman.Size.FormLabelColumnPopover").Subscribe(new ResourceSinks.DoubleSink(v => {
                popoverLabelWidth = v;
                ApplyLabelColumnWidth();
            }, popoverLabelWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Space.Space3").Subscribe(new ResourceSinks.DoubleSink(v => {
                gutterWidth = v;
                ApplyGutterWidth();
            }, gutterWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Space.Space2").Subscribe(new ResourceSinks.DoubleSink(v => {
                unitGap = v;
                ApplyUnitGap();
            }, unitGap)));
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
            grid.ColumnDefinitions[0].Width = new GridLength(LabelColumn switch {
                FormRowLabelColumn.Inspector => inspectorLabelWidth,
                FormRowLabelColumn.Popover => popoverLabelWidth,
                _ => windowLabelWidth,
            });
        }

        private void ApplyGutterWidth() {
            if (grid is null)
                return;
            grid.ColumnDefinitions[1].Width = new GridLength(gutterWidth);
        }

        private void ApplyUnitGap() {
            if (controlColumn is not null)
                controlColumn.Gap = unitGap;
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

            double fallbackLocal = (content.Bounds.Height - labelLine.Height) / 2 + labelLine.Baseline;
            return content.TranslatePoint(new Point(0, fallbackLocal), this)?.Y;
        }

        private double PredictedLabelOverflow() {
            if (label is null || Content is not Control content)
                return 0;
            TextLine? labelLine = FirstLine(label);
            if (labelLine is null)
                return 0;

            Control? textHost = content is TextBlock or TextPresenter && content.IsEffectivelyVisible
                ? content
                : content.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is TextBlock or TextPresenter && c.IsEffectivelyVisible);

            double contentHeight = content.Bounds.Height > 0 ? content.Bounds.Height : content.DesiredSize.Height;
            double controlBaselineLocal = textHost is not null && FirstLine(textHost) is TextLine hostLine
                ? hostLine.Baseline
                : (contentHeight - labelLine.Height) / 2 + labelLine.Baseline;

            double shift = controlBaselineLocal - labelLine.Baseline;
            double labelHeight = label.DesiredSize.Height;
            double rowHeight = Math.Max(Math.Max(labelHeight, contentHeight), rowMinHeight);
            return Math.Max(0, shift + labelHeight - rowHeight);
        }

        private static TextLine? FirstLine(Control control) => control switch {
            TextBlock tb => tb.TextLayout is { TextLines.Count: > 0 } layout ? layout.TextLines[0] : null,
            TextPresenter tp => tp.TextLayout is { TextLines.Count: > 0 } layout ? layout.TextLines[0] : null,
            _ => null,
        };

        private void BeginHeightReflow() {
            if (grid is null)
                return;
            CompositeMotion.ReflowHeight(this, () => {
                grid.Measure(new Size(Bounds.Width, double.PositiveInfinity));
                return grid.DesiredSize.Height;
            }, () => CompositeMotion.ClearExplicitHeight(this, ref reflowCancellation), ref reflowCancellation);
        }

        private void ApplyAccessibility() {
            if (Content is not Control contentControl)
                return;
            AutomationProperties.SetName(contentControl, Label);
            AutomationProperties.SetHelpText(contentControl, !string.IsNullOrEmpty(Error) ? Error : Note);
        }

        private void ApplyRowState() {
            bool hasError = !string.IsNullOrEmpty(Error);
            PseudoClasses.Set(":error", hasError);
            PseudoClasses.Set(":note", !hasError && !string.IsNullOrEmpty(Note));
            PseudoClasses.Set(":value", NoteIsValue);
            PseudoClasses.Set(":warning", ErrorIsWarning);
            PseudoClasses.Set(":note-warning", NoteIsWarning);
        }

        private void ApplyNoteSuffixText() {
            if (noteSuffixText is null)
                return;
            bool hasSuffix = !string.IsNullOrEmpty(NoteSuffix);
            noteSuffixText.IsVisible = hasSuffix;
            noteSuffixText.Text = hasSuffix ? $" · {NoteSuffix}" : "";
        }
    }
}
