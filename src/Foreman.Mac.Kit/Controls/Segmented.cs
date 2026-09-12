using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;

namespace Foreman.Mac.Kit.Controls {
    public class Segmented : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(Segmented);

        public static readonly StyledProperty<int> SelectedIndexProperty =
            AvaloniaProperty.Register<Segmented, int>(nameof(SelectedIndex));

        public int SelectedIndex {
            get => GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public AvaloniaList<string> Items { get; } = [];

        private Grid? track;
        private Border? selection;
        private readonly List<Border> segments = [];
        private readonly List<TextBlock> labels = [];
        private bool hovering;
        private bool pressed;
        private Size lastTrackSize;

        private CornerRadius itemRadius = new(4);
        private Thickness itemPadding = new(10, 3);
        private double miniFontSize = 11.5;
        private FontWeight miniFontWeight = FontWeight.Medium;
        private double calloutFontSize = 12;
        private FontWeight calloutFontWeight = FontWeight.Normal;
        private IBrush text = Brushes.Transparent;
        private IBrush textSecondary = Brushes.Transparent;
        private IBrush controlBackground = Brushes.Transparent;
        private IBrush controlBackgroundPressed = Brushes.Transparent;
        private readonly List<IDisposable> subs = [];

        static Segmented() {
            FocusableProperty.OverrideDefaultValue<Segmented>(true);
            SelectedIndexProperty.Changed.AddClassHandler<Segmented>((s, _) => s.UpdateSelection());
        }

        public Segmented() {
            Items.CollectionChanged += (_, _) => Rebuild();
            Classes.CollectionChanged += (_, _) => ApplyStateColors();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            track = e.NameScope.Find<Grid>("PART_Track");
            selection = e.NameScope.Find<Border>("PART_Selection");
            if (track is not null)
                track.LayoutUpdated += (_, _) => {
                    if (track.Bounds.Size != lastTrackSize)
                        PositionSelection(animate: false);
                };
            Rebuild();
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
            subs.Add(this.GetResourceObservable("Foreman.Radius.SegmentItem").Subscribe(new Sink<CornerRadius>(v => { itemRadius = v; Rebuild(); }, itemRadius)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.SegmentItemPadding").Subscribe(new Sink<Thickness>(v => { itemPadding = v; Rebuild(); }, itemPadding)));
            subs.Add(this.GetResourceObservable("Foreman.Type.SegmentMini.FontSize").Subscribe(new Sink<double>(v => { miniFontSize = v; ApplyStateColors(); }, miniFontSize)));
            subs.Add(this.GetResourceObservable("Foreman.Type.SegmentMini.FontWeight").Subscribe(new Sink<FontWeight>(v => { miniFontWeight = v; ApplyStateColors(); }, miniFontWeight)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Callout.FontSize").Subscribe(new Sink<double>(v => { calloutFontSize = v; ApplyStateColors(); }, calloutFontSize)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Callout.FontWeight").Subscribe(new Sink<FontWeight>(v => { calloutFontWeight = v; ApplyStateColors(); }, calloutFontWeight)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Text").Subscribe(new BrushSink(v => { text = v; ApplyStateColors(); }, text)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TextSecondary").Subscribe(new BrushSink(v => { textSecondary = v; ApplyStateColors(); }, textSecondary)));
            subs.Add(this.GetResourceObservable("Foreman.Color.ControlBackground").Subscribe(new BrushSink(v => { controlBackground = v; ApplyStateColors(); }, controlBackground)));
            subs.Add(this.GetResourceObservable("Foreman.Color.ControlBackgroundPressed").Subscribe(new BrushSink(v => { controlBackgroundPressed = v; ApplyStateColors(); }, controlBackgroundPressed)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void Rebuild() {
            if (track is null)
                return;

            track.Children.Clear();
            track.ColumnDefinitions.Clear();
            segments.Clear();
            labels.Clear();

            for (int i = 0; i < Items.Count; i++) {
                track.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                var label = new TextBlock {
                    Text = Items[i],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                label.Classes.Add("segLabel");
                labels.Add(label);

                var segment = new Border {
                    Child = label,
                    CornerRadius = itemRadius,
                    Padding = itemPadding,
                };
                segment.Classes.Add("segment");

                int index = i;
                segment.PointerPressed += (_, _) => SelectedIndex = index;

                Grid.SetColumn(segment, i);
                track.Children.Add(segment);
                segments.Add(segment);
            }

            if (selection is not null)
                selection.CornerRadius = itemRadius;

            ApplyStateColors();
            PositionSelection(animate: false);
        }

        private void UpdateSelection() {
            ApplyStateColors();
            PositionSelection(animate: true);
        }

        private void ApplyStateColors() {
            bool mini = Classes.Contains("mini");
            double fontSize = mini ? miniFontSize : calloutFontSize;
            FontWeight fontWeight = mini ? miniFontWeight : calloutFontWeight;

            bool hover = hovering || Classes.Contains("gallery-hover");
            bool down = pressed || Classes.Contains("gallery-pressed");

            for (int i = 0; i < segments.Count; i++) {
                bool active = i == SelectedIndex;
                labels[i].FontSize = fontSize;
                labels[i].FontWeight = fontWeight;
                labels[i].Foreground = active || hover ? text : textSecondary;
            }

            if (selection is not null)
                selection.Background = down ? controlBackgroundPressed : controlBackground;
        }

        private void PositionSelection(bool animate) {
            if (track is null || selection is null || segments.Count == 0)
                return;

            lastTrackSize = track.Bounds.Size;
            double width = track.Bounds.Width / segments.Count;
            selection.Width = width;
            selection.Height = track.Bounds.Height;

            double x = SelectedIndex >= 0 && SelectedIndex < segments.Count ? SelectedIndex * width : 0;
            TransformOperations operations = TransformOperations.Parse($"translateX({x.ToString(CultureInfo.InvariantCulture)}px)");

            if (animate) {
                selection.RenderTransform = operations;
                return;
            }

            var transitions = selection.Transitions;
            selection.Transitions = null;
            selection.RenderTransform = operations;
            selection.Transitions = transitions;
        }

        protected override void OnPointerEntered(PointerEventArgs e) {
            base.OnPointerEntered(e);
            hovering = true;
            ApplyStateColors();
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            hovering = false;
            ApplyStateColors();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            pressed = true;
            ApplyStateColors();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            pressed = false;
            ApplyStateColors();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (Items.Count == 0)
                return;

            switch (e.Key) {
                case Key.Left:
                    SelectedIndex = (SelectedIndex - 1 + Items.Count) % Items.Count;
                    e.Handled = true;
                    break;
                case Key.Right:
                    SelectedIndex = (SelectedIndex + 1) % Items.Count;
                    e.Handled = true;
                    break;
                case Key.Space:
                    e.Handled = true;
                    break;
            }
        }

        private sealed class Sink<T> : IObserver<object?> {
            private readonly Action<T> onNext;
            private readonly T fallback;
            private T current;

            public Sink(Action<T> onNext, T fallback) {
                this.onNext = onNext;
                this.fallback = fallback;
                current = fallback;
            }

            public void OnNext(object? value) {
                T next = value is T t ? t : fallback;
                if (EqualityComparer<T>.Default.Equals(current, next))
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        private sealed class BrushSink(Action<IBrush> onNext, IBrush seed) : IObserver<object?> {
            private IBrush current = seed;

            public void OnNext(object? value) {
                IBrush next = value switch { IBrush b => b, Color c => new SolidColorBrush(c), _ => current };
                if (SameBrush(current, next))
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }

            private static bool SameBrush(IBrush a, IBrush b) =>
                ReferenceEquals(a, b) || (a is ISolidColorBrush sa && b is ISolidColorBrush sb && sa.Color == sb.Color);
        }
    }
}
