using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    public class SidebarRow : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(SidebarRow);

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<SidebarRow, string?>(nameof(Label));

        public static readonly StyledProperty<Geometry?> GlyphProperty =
            AvaloniaProperty.Register<SidebarRow, Geometry?>(nameof(Glyph));

        public static readonly StyledProperty<IImage?> IconProperty =
            AvaloniaProperty.Register<SidebarRow, IImage?>(nameof(Icon));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<SidebarRow, bool>(nameof(IsSelected));

        public static readonly StyledProperty<bool> IsKeyProperty =
            AvaloniaProperty.Register<SidebarRow, bool>(nameof(IsKey), true);

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public Geometry? Glyph {
            get => GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public IImage? Icon {
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
        private DockPanel? iconRow;
        private TextBlock? labelText;
        private Path? glyphPath;
        private Border? iconWell;
        private bool hovering;
        private bool hasKeyboardFocus;
        private double iconGap = 8;

        private IBrush accentSoft = Brushes.Transparent;
        private IBrush selectionInactive = Brushes.Transparent;
        private IBrush controlBackgroundHover = Brushes.Transparent;
        private IBrush text = Brushes.Transparent;
        private IBrush accentText = Brushes.Transparent;
        private IBrush textSecondary = Brushes.Transparent;
        private FontWeight normalWeight = FontWeight.Normal;
        private FontWeight selectedWeight = FontWeight.SemiBold;
        private readonly List<IDisposable> subs = [];

        static SidebarRow() {
            FocusableProperty.OverrideDefaultValue<SidebarRow>(true);
            IsSelectedProperty.Changed.AddClassHandler<SidebarRow>((r, _) => r.ApplyState());
            IsKeyProperty.Changed.AddClassHandler<SidebarRow>((r, e) => r.ApplyState());
            IconProperty.Changed.AddClassHandler<SidebarRow>((r, _) => r.ApplyIconSlot());
            AffectsArrange<SidebarRow>(LabelProperty);
            IsPointerOverProperty.Changed.AddClassHandler<SidebarRow>((r, e) => {
                r.hovering = e.NewValue is true;
                r.ApplyState();
            });
        }

        public SidebarRow() {
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
            iconRow = e.NameScope.Find<DockPanel>("PART_IconRow");
            labelText = e.NameScope.Find<TextBlock>("PART_Label");
            if (labelText is not null) {
                TrimmedTooltip.SetHost(labelText, this);
                TrimmedTooltip.SetEnabled(labelText, true);
            }
            glyphPath = e.NameScope.Find<Path>("PART_Glyph");
            iconWell = e.NameScope.Find<Border>("PART_IconWell");
            ApplyIconSlot();
            ApplyIconGap();
            ApplyState();
            UpdateRing();
        }

        private void ApplyIconGap() {
            if (iconRow is not null)
                iconRow.HorizontalSpacing = iconGap;
        }

        private void ApplyIconSlot() {
            if (glyphPath is not null)
                glyphPath.IsVisible = Icon is null;
            if (iconWell is not null)
                iconWell.IsVisible = Icon is not null;
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SidebarRowAutomationPeer(this);

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
            subs.Add(this.GetResourceObservable("Foreman.Size.SidebarRowIconGap").Subscribe(new ResourceSinks.DoubleSink(v => { iconGap = v; ApplyIconGap(); }, iconGap)));
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentSoft").Subscribe(new ResourceSinks.BrushSink(v => { accentSoft = v; ApplyState(); }, accentSoft)));
            subs.Add(this.GetResourceObservable("Foreman.Color.SelectionInactive").Subscribe(new ResourceSinks.BrushSink(v => { selectionInactive = v; ApplyState(); }, selectionInactive)));
            subs.Add(this.GetResourceObservable("Foreman.Color.ControlBackgroundHover").Subscribe(new ResourceSinks.BrushSink(v => { controlBackgroundHover = v; ApplyState(); }, controlBackgroundHover)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Text").Subscribe(new ResourceSinks.BrushSink(v => { text = v; ApplyState(); }, text)));
            subs.Add(this.GetResourceObservable("Foreman.Color.AccentText").Subscribe(new ResourceSinks.BrushSink(v => { accentText = v; ApplyState(); }, accentText)));
            subs.Add(this.GetResourceObservable("Foreman.Color.TextSecondary").Subscribe(new ResourceSinks.BrushSink(v => { textSecondary = v; ApplyState(); }, textSecondary)));
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

            IBrush labelForeground = IsSelected ? (IsKey ? accentText : text) : text;
            IBrush glyphForeground = IsSelected ? (IsKey ? accentText : text) : textSecondary;

            if (labelText is not null) {
                labelText.Foreground = labelForeground;
                labelText.FontWeight = IsSelected && IsKey ? selectedWeight : normalWeight;
            }

            if (glyphPath is not null)
                glyphPath.Stroke = glyphForeground;
        }

        private sealed class SidebarRowAutomationPeer(SidebarRow owner) : ControlAutomationPeer(owner) {
            protected override string? GetNameCore() {
                string? name = base.GetNameCore();
                return string.IsNullOrEmpty(name) ? ((SidebarRow)Owner).Label : name;
            }
        }
    }
}
