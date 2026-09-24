using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    //Kit.SheetFrame at SheetWidthAlert 300 (Standard for a long message) with the alert body baked in: app icon, headline, informative text.
    public class AlertFrame : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(AlertFrame);

        public static readonly StyledProperty<IImage?> IconProperty =
            AvaloniaProperty.Register<AlertFrame, IImage?>(nameof(Icon));

        public static readonly StyledProperty<string?> HeadlineProperty =
            AvaloniaProperty.Register<AlertFrame, string?>(nameof(Headline));

        public static readonly StyledProperty<string?> InformativeTextProperty =
            AvaloniaProperty.Register<AlertFrame, string?>(nameof(InformativeText));

        public static readonly StyledProperty<ButtonRow?> ButtonsProperty =
            AvaloniaProperty.Register<AlertFrame, ButtonRow?>(nameof(Buttons));

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<AlertFrame, bool>(nameof(IsOpen));

        public static readonly StyledProperty<double> TopInsetProperty =
            AvaloniaProperty.Register<AlertFrame, double>(nameof(TopInset));

        public static readonly StyledProperty<SheetWidthClass> WidthClassProperty =
            AvaloniaProperty.Register<AlertFrame, SheetWidthClass>(nameof(WidthClass), SheetWidthClass.Alert);

        public IImage? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string? Headline { get => GetValue(HeadlineProperty); set => SetValue(HeadlineProperty, value); }
        public string? InformativeText { get => GetValue(InformativeTextProperty); set => SetValue(InformativeTextProperty, value); }
        public ButtonRow? Buttons { get => GetValue(ButtonsProperty); set => SetValue(ButtonsProperty, value); }
        public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
        public double TopInset { get => GetValue(TopInsetProperty); set => SetValue(TopInsetProperty, value); }
        public SheetWidthClass WidthClass { get => GetValue(WidthClassProperty); set => SetValue(WidthClassProperty, value); }

        public event EventHandler? Closed;
        public event EventHandler? Cancelled;

        private SheetFrame? sheet;
        private TextBlock? informativeText;
        private Grid? body;
        private readonly List<IDisposable> tokenSubs = [];

        //Gap between the icon column and the text stack (addendum §05: Foreman.Space.Space3).
        private double bodyGapSpace3 = 12;
        private double iconSize = 32;

        static AlertFrame() {
            InformativeTextProperty.Changed.AddClassHandler<AlertFrame>((f, _) => f.ApplyInformative());
            HeadlineProperty.Changed.AddClassHandler<AlertFrame>((f, _) => f.ApplyAccessibility());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (sheet is not null) {
                sheet.Closed -= OnSheetClosed;
                sheet.Cancelled -= OnSheetCancelled;
            }
            sheet = e.NameScope.Find<SheetFrame>("PART_Sheet");
            informativeText = e.NameScope.Find<TextBlock>("PART_Informative");
            body = e.NameScope.Find<Grid>("PART_Body");
            if (sheet is not null) {
                sheet.Closed += OnSheetClosed;
                sheet.Cancelled += OnSheetCancelled;
            }
            ApplyInformative();
            ApplyAccessibility();
            ApplyBodyColumns();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Unsubscribe();
            tokenSubs.Add(this.GetResourceObservable("Foreman.Space.Space3").Subscribe(new ResourceSinks.DoubleSink(v => {
                bodyGapSpace3 = v;
                ApplyBodyColumns();
            }, bodyGapSpace3)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Size.AlertIcon").Subscribe(new ResourceSinks.DoubleSink(v => {
                iconSize = v;
                ApplyBodyColumns();
            }, iconSize)));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in tokenSubs)
                sub.Dispose();
            tokenSubs.Clear();
        }

        private void OnSheetClosed(object? sender, EventArgs e) => Closed?.Invoke(this, EventArgs.Empty);
        private void OnSheetCancelled(object? sender, EventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);

        //A headline-only alert never collapses the leading 32pt icon column + gap.
        private void ApplyInformative() {
            if (informativeText is not null)
                informativeText.IsVisible = !string.IsNullOrEmpty(InformativeText);
        }

        private void ApplyAccessibility() => AutomationProperties.SetName(this, Headline);

        private void ApplyBodyColumns() {
            if (body is null || body.ColumnDefinitions.Count <= 1)
                return;
            body.ColumnDefinitions[0].Width = new GridLength(iconSize);
            body.ColumnDefinitions[1].Width = new GridLength(bodyGapSpace3);
        }
    }
}
