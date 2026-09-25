using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    //Addendum §09's quick-add result row: selection (IsSelected/IsKey) is driven externally by the hosting panel, mirroring Kit.ListRow.
    public class PaletteRow : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(PaletteRow);

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<PaletteRow, string?>(nameof(Label));

        public static readonly StyledProperty<string?> KindProperty =
            AvaloniaProperty.Register<PaletteRow, string?>(nameof(Kind));

        public static readonly StyledProperty<IBrush?> IconProperty =
            AvaloniaProperty.Register<PaletteRow, IBrush?>(nameof(Icon));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<PaletteRow, bool>(nameof(IsSelected));

        public static readonly StyledProperty<bool> IsKeyProperty =
            AvaloniaProperty.Register<PaletteRow, bool>(nameof(IsKey), true);

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string? Kind {
            get => GetValue(KindProperty);
            set => SetValue(KindProperty, value);
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

        public event EventHandler? Activated;

        private Border? fill;
        private Border? selectionFill;
        private TextBlock? labelText;
        private TextBlock? kindText;
        private bool hovering;

        private IBrush accentSoft = Brushes.Transparent;
        private IBrush selectionInactive = Brushes.Transparent;
        private IBrush controlBackgroundHover = Brushes.Transparent;
        private IBrush text = Brushes.Transparent;
        private IBrush accentText = Brushes.Transparent;
        private IBrush textTertiary = Brushes.Transparent;
        private FontWeight normalWeight = FontWeight.Normal;
        private FontWeight selectedWeight = FontWeight.SemiBold;
        private readonly List<IDisposable> subs = [];

        static PaletteRow() {
            FocusableProperty.OverrideDefaultValue<PaletteRow>(false);
            IsTabStopProperty.OverrideDefaultValue<PaletteRow>(false);
            IsSelectedProperty.Changed.AddClassHandler<PaletteRow>((r, _) => r.ApplyState());
            IsKeyProperty.Changed.AddClassHandler<PaletteRow>((r, _) => r.ApplyState());
            IsPointerOverProperty.Changed.AddClassHandler<PaletteRow>((r, e) => {
                r.hovering = e.NewValue is true;
                r.ApplyState();
            });
        }

        public PaletteRow() => AddHandler(PointerReleasedEvent, OnPointerReleased);

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) {
            if (e.InitialPressMouseButton == MouseButton.Left)
                RaiseActivated();
        }

        internal void RaiseActivated() => Activated?.Invoke(this, EventArgs.Empty);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            fill = e.NameScope.Find<Border>("PART_Fill");
            selectionFill = e.NameScope.Find<Border>("PART_SelectionFill");
            labelText = e.NameScope.Find<TextBlock>("PART_Label");
            kindText = e.NameScope.Find<TextBlock>("PART_Kind");
            ApplyState();
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

        //Selection follows the row rule (addendum §09): AccentSoft + AccentText at 600 when key, SelectionInactive with normal text when not - hover suppressed on the selected row.
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
            //Trailing kind is always TextTertiary - it disambiguates, it doesn't track selection.
            if (kindText is not null)
                kindText.Foreground = textTertiary;
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new PaletteRowAutomationPeer(this);

        private sealed class PaletteRowAutomationPeer(PaletteRow owner) : ControlAutomationPeer(owner), IInvokeProvider {
            private PaletteRow Row => (PaletteRow)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

            void IInvokeProvider.Invoke() => Row.RaiseActivated();
        }
    }
}
