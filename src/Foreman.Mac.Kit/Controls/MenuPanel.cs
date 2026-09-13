using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Foreman.Mac.Kit.Controls {
    public class MenuPanel : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(MenuPanel);

        public static readonly StyledProperty<int> HighlightedIndexProperty =
            AvaloniaProperty.Register<MenuPanel, int>(nameof(HighlightedIndex), defaultValue: -1);

        public int HighlightedIndex {
            get => GetValue(HighlightedIndexProperty);
            set => SetValue(HighlightedIndexProperty, value);
        }

        public AvaloniaList<MenuRow> Items { get; } = [];

        public event EventHandler? RequestClose;

        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

        private StackPanel? itemsPanel;
        private readonly List<RowVisual> rows = [];
        private readonly List<IDisposable> subs = [];

        private CornerRadius itemRadius = new(4);
        private Thickness itemMargin = new(4, 0);
        private Thickness itemPadding = new(6, 0, 12, 0);
        private Thickness sepMargin = new(8, 5);
        private double itemHeight = 22;
        private double checkColumn = 16;
        private double glyphMicro = 12;
        private double fontSize = 12;
        private FontWeight fontWeight = FontWeight.Normal;
        private Geometry? checkGeometry;
        private IBrush border = Brushes.Transparent;
        private IBrush accent = Brushes.Transparent;
        private IBrush textOnAccent = Brushes.Transparent;
        private IBrush text = Brushes.Transparent;
        private IBrush textTertiary = Brushes.Transparent;

        private sealed record RowVisual(Border Row, Path? Check, TextBlock? Text, MenuRow Model);

        static MenuPanel() {
            FocusableProperty.OverrideDefaultValue<MenuPanel>(true);
            HighlightedIndexProperty.Changed.AddClassHandler<MenuPanel>((p, _) => p.UpdateHighlight());
        }

        public MenuPanel() => Items.CollectionChanged += OnItemsChanged;

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            itemsPanel = e.NameScope.Find<StackPanel>("PART_Items");
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
            subs.Add(this.GetResourceObservable("Kit.MenuPanel").Subscribe(new ResourceSinks.Sink<ControlTheme?>(v => Theme ??= v, null)));
            subs.Add(this.GetResourceObservable("Foreman.Radius.SegmentItem").Subscribe(new ResourceSinks.RadiusSink(v => { itemRadius = v; Rebuild(); }, itemRadius)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.MenuItemMargin").Subscribe(new ResourceSinks.ThicknessSink(v => { itemMargin = v; Rebuild(); }, itemMargin)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.MenuItemPadding").Subscribe(new ResourceSinks.ThicknessSink(v => { itemPadding = v; Rebuild(); }, itemPadding)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.MenuSeparatorMargin").Subscribe(new ResourceSinks.ThicknessSink(v => { sepMargin = v; Rebuild(); }, sepMargin)));
            subs.Add(this.GetResourceObservable("Foreman.Size.MenuItemHeight").Subscribe(new ResourceSinks.DoubleSink(v => { itemHeight = v; Rebuild(); }, itemHeight)));
            subs.Add(this.GetResourceObservable("Foreman.Size.MenuCheckColumn").Subscribe(new ResourceSinks.DoubleSink(v => { checkColumn = v; Rebuild(); }, checkColumn)));
            subs.Add(this.GetResourceObservable("Foreman.Size.GlyphMicro").Subscribe(new ResourceSinks.DoubleSink(v => { glyphMicro = v; Rebuild(); }, glyphMicro)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Callout.FontSize").Subscribe(new ResourceSinks.DoubleSink(v => { fontSize = v; Rebuild(); }, fontSize)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Callout.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { fontWeight = v; Rebuild(); }, fontWeight)));
            subs.Add(this.GetResourceObservable("Kit.Glyph.Checkmark.12").Subscribe(new ResourceSinks.Sink<Geometry?>(v => { checkGeometry = v; Rebuild(); }, checkGeometry)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Border").Subscribe(new ResourceSinks.BrushSink(v => { border = v; Rebuild(); }, border)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Accent").Subscribe(new ResourceSinks.BrushSink(v => { accent = v; UpdateHighlight(); }, accent)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TextOnAccent").Subscribe(new ResourceSinks.BrushSink(v => { textOnAccent = v; UpdateHighlight(); }, textOnAccent)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Text").Subscribe(new ResourceSinks.BrushSink(v => { text = v; UpdateHighlight(); }, text)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TextTertiary").Subscribe(new ResourceSinks.BrushSink(v => { textTertiary = v; UpdateHighlight(); }, textTertiary)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void Rebuild() {
            if (itemsPanel is null)
                return;

            itemsPanel.Children.Clear();
            rows.Clear();

            foreach (MenuRow item in Items) {
                if (item.IsSeparator) {
                    var separator = new Border { Height = 1, Margin = sepMargin, Background = border };
                    itemsPanel.Children.Add(separator);
                    rows.Add(new RowVisual(separator, null, null, item));
                    continue;
                }

                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

                Path? check = null;
                if (item.HasCheckSlot) {
                    check = new Path {
                        Data = checkGeometry,
                        Width = glyphMicro,
                        Height = glyphMicro,
                        StrokeThickness = 1.6,
                        StrokeLineCap = PenLineCap.Round,
                        StrokeJoin = PenLineJoin.Round,
                        IsVisible = item.IsChecked,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var slot = new Panel { Width = checkColumn, Children = { check } };
                    content.Children.Add(slot);
                }

                var text = new TextBlock {
                    Text = item.Text,
                    FontSize = fontSize,
                    FontWeight = fontWeight,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                content.Children.Add(text);

                var row = new Border {
                    Height = itemHeight,
                    Margin = itemMargin,
                    Padding = itemPadding,
                    CornerRadius = itemRadius,
                    Cursor = item.IsEnabled ? HandCursor : Cursor.Default,
                    Child = content,
                };

                MenuRow captured = item;
                row.PointerEntered += (_, _) => { if (captured.IsEnabled) HighlightedIndex = rows.FindIndex(r => r.Model == captured); };
                row.PointerPressed += (_, _) => {
                    if (!captured.IsEnabled)
                        return;
                    HighlightedIndex = rows.FindIndex(r => r.Model == captured);
                    ActivateHighlighted();
                };

                itemsPanel.Children.Add(row);
                rows.Add(new RowVisual(row, check, text, item));
            }

            UpdateHighlight();
        }

        private void UpdateHighlight() {
            for (int i = 0; i < rows.Count; i++) {
                RowVisual row = rows[i];
                if (row.Model.IsSeparator)
                    continue;

                bool highlighted = i == HighlightedIndex;
                row.Row.Background = highlighted ? accent : null;
                IBrush foreground = highlighted ? textOnAccent : row.Model.IsEnabled ? text : textTertiary;
                if (row.Text is not null)
                    row.Text.Foreground = foreground;
                if (row.Check is not null)
                    row.Check.Stroke = foreground;
            }
        }

        public void ActivateHighlighted() {
            if (HighlightedIndex < 0 || HighlightedIndex >= Items.Count)
                return;

            MenuRow item = Items[HighlightedIndex];
            if (item.IsSeparator || !item.IsEnabled)
                return;

            item.Activate?.Invoke();
            if (!item.KeepOpenOnActivate)
                RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public void MoveHighlight(int step) {
            if (Items.Count == 0)
                return;

            int index = HighlightedIndex;
            for (int attempts = 0; attempts < Items.Count; attempts++) {
                index = (index + step + Items.Count) % Items.Count;
                MenuRow candidate = Items[index];
                if (!candidate.IsSeparator && candidate.IsEnabled) {
                    HighlightedIndex = index;
                    return;
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            switch (e.Key) {
                case Key.Down:
                    MoveHighlight(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveHighlight(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                case Key.Space:
                    ActivateHighlighted();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    RequestClose?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

    }
}
