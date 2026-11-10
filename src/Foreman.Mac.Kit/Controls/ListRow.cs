using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    public class ListRow : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ListRow);

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<ListRow, string?>(nameof(Label));

        public static readonly StyledProperty<string?> TrailingProperty =
            AvaloniaProperty.Register<ListRow, string?>(nameof(Trailing));

        public static readonly StyledProperty<IBrush?> IconProperty =
            AvaloniaProperty.Register<ListRow, IBrush?>(nameof(Icon));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<ListRow, bool>(nameof(IsSelected));

        public static readonly StyledProperty<bool> IsKeyProperty =
            AvaloniaProperty.Register<ListRow, bool>(nameof(IsKey), true);

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string? Trailing {
            get => GetValue(TrailingProperty);
            set => SetValue(TrailingProperty, value);
        }

        public IBrush? Icon {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public bool IsSelected {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public bool IsKey {
            get => GetValue(IsKeyProperty);
            set => SetValue(IsKeyProperty, value);
        }

        private Border? frame;
        private Border? fill;
        private Border? selectionFill;
        private StackPanel? iconRow;
        private TextBlock? labelText;
        private TextBlock? trailingText;
        private bool hovering;
        private bool hasKeyboardFocus;
        private double iconGap = 9;

        private IBrush accentSoft = Brushes.Transparent;
        private IBrush selectionInactive = Brushes.Transparent;
        private IBrush controlBackgroundHover = Brushes.Transparent;
        private IBrush text = Brushes.Transparent;
        private IBrush accentText = Brushes.Transparent;
        private IBrush textTertiary = Brushes.Transparent;
        private FontWeight normalWeight = FontWeight.Normal;
        private FontWeight selectedWeight = FontWeight.SemiBold;
        private readonly List<IDisposable> subs = [];

        static ListRow() {
            FocusableProperty.OverrideDefaultValue<ListRow>(true);
            IsSelectedProperty.Changed.AddClassHandler<ListRow>((r, _) => r.ApplyState());
            IsKeyProperty.Changed.AddClassHandler<ListRow>((r, e) => r.ApplyState());
            IsPointerOverProperty.Changed.AddClassHandler<ListRow>((r, e) => {
                r.hovering = e.NewValue is true;
                r.ApplyState();
            });
        }

        public ListRow() {
            GotFocus += (_, e) => {
                if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) {
                    hasKeyboardFocus = true;
                    UpdateRing();
                }
            };
            LostFocus += (_, _) => {
                hasKeyboardFocus = false;
                UpdateRing();
            };
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            frame = e.NameScope.Find<Border>("PART_Frame");
            fill = e.NameScope.Find<Border>("PART_Fill");
            selectionFill = e.NameScope.Find<Border>("PART_SelectionFill");
            iconRow = e.NameScope.Find<StackPanel>("PART_IconRow");
            labelText = e.NameScope.Find<TextBlock>("PART_Label");
            trailingText = e.NameScope.Find<TextBlock>("PART_Trailing");
            ApplyIconGap();
            ApplyState();
            UpdateRing();
        }

        private void ApplyIconGap() {
            if (iconRow is not null)
                iconRow.Spacing = iconGap;
        }

        private void UpdateRing() {
            if (frame is not null)
                FocusRing.SetShowAlways(frame, hasKeyboardFocus);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.ListRowIconGap").Subscribe(new ResourceSinks.DoubleSink(v => { iconGap = v; ApplyIconGap(); }, iconGap)));
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentSoft").Subscribe(new ResourceSinks.BrushSink(v => { accentSoft = v; ApplyState(); }, accentSoft)));
            subs.Add(this.GetResourceObservable("Foreman.Color.SelectionInactive").Subscribe(new ResourceSinks.BrushSink(v => { selectionInactive = v; ApplyState(); }, selectionInactive)));
            subs.Add(this.GetResourceObservable("Foreman.Color.ControlBackgroundHover").Subscribe(new ResourceSinks.BrushSink(v => { controlBackgroundHover = v; ApplyState(); }, controlBackgroundHover)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Text").Subscribe(new ResourceSinks.BrushSink(v => { text = v; ApplyState(); }, text)));
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentText").Subscribe(new ResourceSinks.BrushSink(v => { accentText = v; ApplyState(); }, accentText)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TextTertiary").Subscribe(new ResourceSinks.BrushSink(v => { textTertiary = v; ApplyState(); }, textTertiary)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Callout.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { normalWeight = v; ApplyState(); }, normalWeight)));
            subs.Add(this.GetResourceObservable("Foreman.Type.SectionHeader.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { selectedWeight = v; ApplyState(); }, selectedWeight)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void ApplyState() {
            if (fill is null || selectionFill is null)
                return;

            bool showSelectionFill = IsSelected && IsKey;
            fill.Background = IsSelected ? selectionInactive : controlBackgroundHover;
            fill.Opacity = showSelectionFill ? 0 : IsSelected || hovering ? 1 : 0;
            selectionFill.Background = accentSoft;
            selectionFill.Opacity = showSelectionFill ? 1 : 0;

            IBrush foreground = IsSelected && IsKey ? accentText : text;
            if (labelText is not null) {
                labelText.Foreground = foreground;
                labelText.FontWeight = IsSelected && IsKey ? selectedWeight : normalWeight;
            }

            if (trailingText is not null)
                trailingText.Foreground = IsSelected && IsKey ? accentText : textTertiary;
        }
    }
}
