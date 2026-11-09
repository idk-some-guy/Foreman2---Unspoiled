using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;

namespace Foreman.Mac.Kit.Controls {
    public class TabStrip : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(TabStrip);

        public static readonly StyledProperty<int> SelectedIndexProperty =
            AvaloniaProperty.Register<TabStrip, int>(nameof(SelectedIndex), coerce: CoerceSelectedIndex);

        public int SelectedIndex {
            get => GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public AvaloniaList<TabStripItem> Items { get; } = [];

        public event EventHandler? SelectionChanged;

        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
        private const double GlyphMasterSize = 18;

        private Border? chrome;
        private StackPanel? track;
        private Border? baseline;
        private Border? underline;
        private readonly List<ItemVisual> items = [];
        private bool hasKeyboardFocus;
        private bool isUnderlineMode;
        private int hoverIndex = -1;
        private int pressIndex = -1;
        private Size lastTrackSize;
        private EventHandler? trackLayoutUpdated;

        private double tabItemGap = 2, underlineTabGap = 18, tabItemGlyphGap = 3;
        private double tabItemHeight = 46, tabItemMinWidth = 64, glyph18 = 18;
        private double tabLabelFontSize = 10.5, calloutFontSize = 12;
        private FontWeight tabLabelFontWeight = FontWeight.Medium, calloutFontWeight = FontWeight.Normal, selectedUnderlineWeight = FontWeight.SemiBold;
        private CornerRadius itemRadius = new(6), underlineFocusRadius = new(4);
        private Thickness tabItemPadding = new(12, 6), underlineTabPadding = new(2, 0, 2, 8), tabStripPadding = new(6);
        private double disabledOpacity = 0.38;
        private IBrush textSecondary = Brushes.Transparent, text = Brushes.Transparent, accentSoft = Brushes.Transparent, accentText = Brushes.Transparent;
        private IBrush controlBackgroundHover = Brushes.Transparent, controlBackgroundPressed = Brushes.Transparent;
        private IBrush border = Brushes.Transparent, accent = Brushes.Transparent, titlebarBackground = Brushes.Transparent;
        private CornerRadius chromeRadius = new(9);
        private readonly List<IDisposable> subs = [];

        private sealed record ItemVisual(Border Item, Border? Fill, Border? SelectionFill, Path? Glyph, Path? GlyphFill, TextBlock Label, TabStripItem Model);

        static TabStrip() {
            FocusableProperty.OverrideDefaultValue<TabStrip>(true);
            SelectedIndexProperty.Changed.AddClassHandler<TabStrip>((s, e) => s.OnSelectionChanged((int)e.OldValue!));
        }

        private static int CoerceSelectedIndex(AvaloniaObject sender, int value) {
            var strip = (TabStrip)sender;
            return strip.Items.Count == 0 ? -1 : Math.Clamp(value, 0, strip.Items.Count - 1);
        }

        public TabStrip() {
            Items.CollectionChanged += (_, _) => { CoerceValue(SelectedIndexProperty); Rebuild(); };
            Classes.CollectionChanged += (_, _) => {
                if (IsUnderline != isUnderlineMode)
                    Rebuild();
            };
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
            if (track is not null && trackLayoutUpdated is not null)
                track.LayoutUpdated -= trackLayoutUpdated;

            chrome = e.NameScope.Find<Border>("PART_Chrome");
            track = e.NameScope.Find<StackPanel>("PART_Track");
            baseline = e.NameScope.Find<Border>("PART_Baseline");
            underline = e.NameScope.Find<Border>("PART_Underline");

            if (track is not null) {
                trackLayoutUpdated = (_, _) => {
                    if (track.Bounds.Size != lastTrackSize)
                        PositionUnderline(animate: false);
                };
                track.LayoutUpdated += trackLayoutUpdated;
            }

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
            subs.Add(this.GetResourceObservable("Foreman.Size.TabItemGap").Subscribe(new ResourceSinks.DoubleSink(v => { tabItemGap = v; Rebuild(); }, tabItemGap)));
            subs.Add(this.GetResourceObservable("Foreman.Size.UnderlineTabGap").Subscribe(new ResourceSinks.DoubleSink(v => { underlineTabGap = v; Rebuild(); }, underlineTabGap)));
            subs.Add(this.GetResourceObservable("Foreman.Size.TabItemGlyphGap").Subscribe(new ResourceSinks.DoubleSink(v => { tabItemGlyphGap = v; Rebuild(); }, tabItemGlyphGap)));
            subs.Add(this.GetResourceObservable("Foreman.Size.TabItemHeight").Subscribe(new ResourceSinks.DoubleSink(v => { tabItemHeight = v; Rebuild(); }, tabItemHeight)));
            subs.Add(this.GetResourceObservable("Foreman.Metrics.TabItemMinWidth").Subscribe(new ResourceSinks.DoubleSink(v => { tabItemMinWidth = v; Rebuild(); }, tabItemMinWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Size.Glyph18").Subscribe(new ResourceSinks.DoubleSink(v => { glyph18 = v; Rebuild(); }, glyph18)));
            subs.Add(this.GetResourceObservable("Foreman.Type.TabLabel.FontSize").Subscribe(new ResourceSinks.DoubleSink(v => { tabLabelFontSize = v; Rebuild(); }, tabLabelFontSize)));
            subs.Add(this.GetResourceObservable("Foreman.Type.TabLabel.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { tabLabelFontWeight = v; Rebuild(); }, tabLabelFontWeight)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Callout.FontSize").Subscribe(new ResourceSinks.DoubleSink(v => { calloutFontSize = v; Rebuild(); }, calloutFontSize)));
            subs.Add(this.GetResourceObservable("Foreman.Type.Callout.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { calloutFontWeight = v; Rebuild(); }, calloutFontWeight)));
            subs.Add(this.GetResourceObservable("Foreman.Type.SectionHeader.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { selectedUnderlineWeight = v; Rebuild(); }, selectedUnderlineWeight)));
            subs.Add(this.GetResourceObservable("Foreman.Radius.Control").Subscribe(new ResourceSinks.RadiusSink(v => { itemRadius = v; Rebuild(); }, itemRadius)));
            subs.Add(this.GetResourceObservable("Foreman.Radius.UnderlineTabFocus").Subscribe(new ResourceSinks.RadiusSink(v => { underlineFocusRadius = v; Rebuild(); }, underlineFocusRadius)));
            subs.Add(this.GetResourceObservable("Foreman.Radius.Card").Subscribe(new ResourceSinks.RadiusSink(v => { chromeRadius = v; Rebuild(); }, chromeRadius)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.TabItemPadding").Subscribe(new ResourceSinks.ThicknessSink(v => { tabItemPadding = v; Rebuild(); }, tabItemPadding)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.UnderlineTabPadding").Subscribe(new ResourceSinks.ThicknessSink(v => { underlineTabPadding = v; Rebuild(); }, underlineTabPadding)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.TabStripPadding").Subscribe(new ResourceSinks.ThicknessSink(v => { tabStripPadding = v; Rebuild(); }, tabStripPadding)));
            subs.Add(this.GetResourceObservable("Foreman.Opacity.Disabled").Subscribe(new ResourceSinks.DoubleSink(v => { disabledOpacity = v; ApplyStateColors(); }, disabledOpacity)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TextSecondary").Subscribe(new ResourceSinks.BrushSink(v => { textSecondary = v; ApplyStateColors(); }, textSecondary)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Text").Subscribe(new ResourceSinks.BrushSink(v => { text = v; ApplyStateColors(); }, text)));
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentSoft").Subscribe(new ResourceSinks.BrushSink(v => { accentSoft = v; ApplyStateColors(); }, accentSoft)));
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentText").Subscribe(new ResourceSinks.BrushSink(v => { accentText = v; ApplyStateColors(); }, accentText)));
            subs.Add(this.GetResourceObservable("Foreman.Color.ControlBackgroundHover").Subscribe(new ResourceSinks.BrushSink(v => { controlBackgroundHover = v; ApplyStateColors(); }, controlBackgroundHover)));
            subs.Add(this.GetResourceObservable("Foreman.Color.ControlBackgroundPressed").Subscribe(new ResourceSinks.BrushSink(v => { controlBackgroundPressed = v; ApplyStateColors(); }, controlBackgroundPressed)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Border").Subscribe(new ResourceSinks.BrushSink(v => { border = v; ApplyStateColors(); }, border)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Accent").Subscribe(new ResourceSinks.BrushSink(v => { accent = v; ApplyStateColors(); }, accent)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TitlebarBackground").Subscribe(new ResourceSinks.BrushSink(v => { titlebarBackground = v; ApplyStateColors(); }, titlebarBackground)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private bool IsUnderline => Classes.Contains("underline");

        private void Rebuild() {
            if (track is null || chrome is null)
                return;

            track.Children.Clear();
            items.Clear();

            bool underlineMode = IsUnderline;
            isUnderlineMode = underlineMode;
            track.Spacing = underlineMode ? underlineTabGap : tabItemGap;

            if (baseline is not null)
                baseline.IsVisible = underlineMode;
            if (underline is not null)
                underline.IsVisible = underlineMode;

            chrome.Background = underlineMode ? null : titlebarBackground;
            chrome.Padding = underlineMode ? default : tabStripPadding;
            chrome.CornerRadius = underlineMode ? default : chromeRadius;

            for (int i = 0; i < Items.Count; i++) {
                TabStripItem model = Items[i];
                ItemVisual visual = underlineMode ? BuildUnderlineItem(model) : BuildToolbarItem(model);

                int index = i;
                visual.Item.PointerEntered += (_, _) => { hoverIndex = index; ApplyStateColors(); };
                visual.Item.PointerExited += (_, _) => { if (hoverIndex == index) hoverIndex = -1; ApplyStateColors(); };
                visual.Item.PointerPressed += (_, _) => {
                    if (!model.IsEnabled)
                        return;
                    pressIndex = index;
                    SelectedIndex = index;
                    ApplyStateColors();
                };
                visual.Item.PointerReleased += (_, _) => {
                    if (pressIndex == index)
                        pressIndex = -1;
                    ApplyStateColors();
                };

                FocusRing.SetCornerRadius(visual.Item, underlineMode ? underlineFocusRadius : itemRadius);
                AutomationProperties.SetName(visual.Item, model.Text);
                AutomationProperties.SetHelpText(visual.Item, model.ToolTip);
                AutomationProperties.SetPositionInSet(visual.Item, i + 1);
                AutomationProperties.SetSizeOfSet(visual.Item, Items.Count);

                track.Children.Add(visual.Item);
                items.Add(visual);
            }

            ApplyStateColors();
            PositionUnderline(animate: false);
            UpdateRing();
            (ControlAutomationPeer.FromElement(this) as TabStripAutomationPeer)?.ItemsRebuilt();
        }

        private ItemVisual BuildToolbarItem(TabStripItem model) {
            var content = new StackPanel {
                Orientation = Orientation.Vertical,
                Spacing = tabItemGlyphGap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = tabItemPadding,
            };

            Path? glyph = null;
            Path? glyphFill = null;
            if (model.Glyph is not null) {
                glyph = new Path {
                    Data = model.Glyph,
                    Width = GlyphMasterSize,
                    Height = GlyphMasterSize,
                    Stretch = Stretch.None,
                    StrokeThickness = 1.5,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                };
                Control master = glyph;
                if (model.GlyphFill is not null) {
                    glyphFill = new Path { Data = model.GlyphFill, Width = GlyphMasterSize, Height = GlyphMasterSize, Stretch = Stretch.None };
                    master = new Panel { Width = GlyphMasterSize, Height = GlyphMasterSize, Children = { glyphFill, glyph } };
                }
                content.Children.Add(new Viewbox { Width = glyph18, Height = glyph18, Stretch = Stretch.Uniform, Child = master });
            }

            var label = new TextBlock {
                Text = model.Text,
                FontSize = tabLabelFontSize,
                FontWeight = tabLabelFontWeight,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            content.Children.Add(label);

            var fill = new Border {
                Name = "PART_Fill",
                CornerRadius = itemRadius,
                Opacity = 0,
                IsHitTestVisible = false,
                Transitions = Motion.TabFillTransitions,
            };

            var selectionFill = new Border {
                Name = "PART_SelectionFill",
                CornerRadius = itemRadius,
                Opacity = 0,
                IsHitTestVisible = false,
                Transitions = Motion.TabFillTransitions,
            };

            var item = new TabItemHost(this, model) {
                Height = tabItemHeight,
                MinWidth = tabItemMinWidth,
                CornerRadius = itemRadius,
                Background = Brushes.Transparent,
                Cursor = model.IsEnabled ? HandCursor : Cursor.Default,
                Focusable = false,
                Child = new Panel { Children = { fill, selectionFill, content } },
            };
            ToolTip.SetTip(item, model.ToolTip);

            return new ItemVisual(item, fill, selectionFill, glyph, glyphFill, label, model);
        }

        private ItemVisual BuildUnderlineItem(TabStripItem model) {
            var label = new TextBlock {
                Text = model.Text,
                FontSize = calloutFontSize,
                FontWeight = calloutFontWeight,
            };

            var item = new TabItemHost(this, model) {
                Padding = underlineTabPadding,
                Background = Brushes.Transparent,
                Cursor = model.IsEnabled ? HandCursor : Cursor.Default,
                Focusable = false,
                Child = label,
            };

            return new ItemVisual(item, null, null, null, null, label, model);
        }

        private void ApplyStateColors() {
            for (int i = 0; i < items.Count; i++) {
                ItemVisual v = items[i];
                bool selected = i == SelectedIndex;
                bool hover = (i == hoverIndex || v.Item.Classes.Contains("gallery-hover")) && !selected && v.Model.IsEnabled;
                bool pressed = (i == pressIndex || v.Item.Classes.Contains("gallery-pressed")) && !selected && v.Model.IsEnabled;

                v.Item.Opacity = v.Model.IsEnabled ? 1.0 : disabledOpacity;

                if (IsUnderline) {
                    IBrush color = selected ? text : hover ? text : textSecondary;
                    v.Label.Foreground = color;
                    v.Label.FontWeight = selected ? selectedUnderlineWeight : calloutFontWeight;
                } else {
                    if (v.Fill is not null && v.SelectionFill is not null) {
                        v.Fill.Background = pressed ? controlBackgroundPressed : controlBackgroundHover;
                        v.Fill.Opacity = !selected && (pressed || hover) ? 1 : 0;
                        v.SelectionFill.Background = accentSoft;
                        v.SelectionFill.Opacity = selected ? 1 : 0;
                    }
                    IBrush color = selected ? accentText : (hover || pressed) ? text : textSecondary;
                    v.Label.Foreground = color;
                    if (v.Glyph is not null)
                        v.Glyph.Stroke = color;
                    if (v.GlyphFill is not null)
                        v.GlyphFill.Fill = color;
                }
            }

            if (underline is not null)
                underline.Background = accent;
            if (baseline is not null)
                baseline.Background = border;
        }

        private void PositionUnderline(bool animate) {
            if (track is null || underline is null || !IsUnderline || items.Count == 0)
                return;

            lastTrackSize = track.Bounds.Size;
            int index = Math.Clamp(SelectedIndex, 0, items.Count - 1);
            Border selected = items[index].Item;
            underline.Width = selected.Bounds.Width;

            TransformOperations operations = TransformOperations.Parse($"translateX({selected.Bounds.X.ToString(CultureInfo.InvariantCulture)}px)");
            if (animate) {
                underline.RenderTransform = operations;
                return;
            }

            var transitions = underline.Transitions;
            underline.Transitions = null;
            underline.RenderTransform = operations;
            underline.Transitions = transitions;
        }

        private void UpdateRing() {
            for (int i = 0; i < items.Count; i++)
                FocusRing.SetShowAlways(items[i].Item, hasKeyboardFocus && i == SelectedIndex);
        }

        private void OnSelectionChanged(int previous) {
            ApplyStateColors();
            PositionUnderline(animate: true);
            UpdateRing();
            AnnounceSelected(previous, false);
            AnnounceSelected(SelectedIndex, true);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AnnounceSelected(int index, bool selected) {
            if (index >= 0 && index < items.Count && ControlAutomationPeer.FromElement(items[index].Item) is AutomationPeer peer)
                peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, !selected, selected);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new TabStripAutomationPeer(this);

        private sealed class TabItemHost(TabStrip strip, TabStripItem model) : Border {
            protected override Type StyleKeyOverride => typeof(Border);
            public TabStrip Strip { get; } = strip;
            public TabStripItem Model { get; } = model;
            public int Index => Strip.items.FindIndex(v => ReferenceEquals(v.Item, this));
            protected override AutomationPeer OnCreateAutomationPeer() => new TabItemAutomationPeer(this);
        }

        private sealed class TabStripAutomationPeer(TabStrip owner) : ControlAutomationPeer(owner), ISelectionProvider {
            private TabStrip Strip => (TabStrip)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;

            public void ItemsRebuilt() => InvalidateChildren();

            protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() =>
                Strip.items.ConvertAll(v => CreatePeerForElement(v.Item));

            bool ISelectionProvider.CanSelectMultiple => false;
            bool ISelectionProvider.IsSelectionRequired => true;

            IReadOnlyList<AutomationPeer> ISelectionProvider.GetSelection() =>
                Strip.SelectedIndex >= 0 && Strip.SelectedIndex < Strip.items.Count ? [CreatePeerForElement(Strip.items[Strip.SelectedIndex].Item)] : [];
        }

        private sealed class TabItemAutomationPeer(TabItemHost owner) : ControlAutomationPeer(owner), ISelectionItemProvider {
            private TabItemHost Host => (TabItemHost)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;
            protected override bool IsEnabledCore() => Host.Model.IsEnabled;

            bool ISelectionItemProvider.IsSelected => Host.Index >= 0 && Host.Index == Host.Strip.SelectedIndex;
            ISelectionProvider? ISelectionItemProvider.SelectionContainer => CreatePeerForElement(Host.Strip) as ISelectionProvider;

            void ISelectionItemProvider.Select() {
                if (Host.Model.IsEnabled && Host.Index >= 0)
                    Host.Strip.SelectedIndex = Host.Index;
            }

            void ISelectionItemProvider.AddToSelection() => ((ISelectionItemProvider)this).Select();
            void ISelectionItemProvider.RemoveFromSelection() { }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (Items.Count == 0)
                return;

            switch (e.Key) {
                case Key.Left:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Home:
                    JumpToFirstEnabled();
                    e.Handled = true;
                    break;
                case Key.End:
                    JumpToLastEnabled();
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int step) {
            int index = SelectedIndex;
            for (int attempts = 0; attempts < Items.Count; attempts++) {
                index = (index + step + Items.Count) % Items.Count;
                if (Items[index].IsEnabled) {
                    SelectedIndex = index;
                    return;
                }
            }
        }

        private void JumpToFirstEnabled() {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].IsEnabled) {
                    SelectedIndex = i;
                    return;
                }
        }

        private void JumpToLastEnabled() {
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i].IsEnabled) {
                    SelectedIndex = i;
                    return;
                }
        }
    }
}
