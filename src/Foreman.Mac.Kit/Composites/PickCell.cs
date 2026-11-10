using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;

namespace Foreman.Mac.Kit.Composites {
    public class PickCell : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(PickCell);

        public static readonly StyledProperty<IImage?> IconProperty =
            AvaloniaProperty.Register<PickCell, IImage?>(nameof(Icon));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<PickCell, bool>(nameof(IsSelected));

        public static readonly StyledProperty<bool> IsEmptyProperty =
            AvaloniaProperty.Register<PickCell, bool>(nameof(IsEmpty));

        public static readonly StyledProperty<bool> IsMissingProperty =
            AvaloniaProperty.Register<PickCell, bool>(nameof(IsMissing));

        public static readonly StyledProperty<bool> IsUnavailableProperty =
            AvaloniaProperty.Register<PickCell, bool>(nameof(IsUnavailable));

        public static readonly StyledProperty<bool> IsNoneProperty =
            AvaloniaProperty.Register<PickCell, bool>(nameof(IsNone));

        public static readonly StyledProperty<bool> IsFullAndUnarmedProperty =
            AvaloniaProperty.Register<PickCell, bool>(nameof(IsFullAndUnarmed));

        public static readonly StyledProperty<string?> AccessibleNameProperty =
            AvaloniaProperty.Register<PickCell, string?>(nameof(AccessibleName));

        public static readonly StyledProperty<Color?> SwatchProperty =
            AvaloniaProperty.Register<PickCell, Color?>(nameof(Swatch));

        public static readonly StyledProperty<string?> EmptyItemNounProperty =
            AvaloniaProperty.Register<PickCell, string?>(nameof(EmptyItemNoun));

        internal static readonly StyledProperty<IBrush?> RingBrushProperty =
            AvaloniaProperty.Register<PickCell, IBrush?>(nameof(RingBrush), Brushes.Transparent);

        public IImage? Icon {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public Color? Swatch {
            get => GetValue(SwatchProperty);
            set => SetValue(SwatchProperty, value);
        }

        public bool IsSelected {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public bool IsEmpty {
            get => GetValue(IsEmptyProperty);
            set => SetValue(IsEmptyProperty, value);
        }

        public bool IsMissing {
            get => GetValue(IsMissingProperty);
            set => SetValue(IsMissingProperty, value);
        }

        public bool IsUnavailable {
            get => GetValue(IsUnavailableProperty);
            set => SetValue(IsUnavailableProperty, value);
        }

        public bool IsNone {
            get => GetValue(IsNoneProperty);
            set => SetValue(IsNoneProperty, value);
        }

        public bool IsFullAndUnarmed {
            get => GetValue(IsFullAndUnarmedProperty);
            set => SetValue(IsFullAndUnarmedProperty, value);
        }

        internal const string FullAndUnarmedMessage = "All slots full — select a slot to replace";

        internal bool IsChoiceItem { get; set; }

        public string? AccessibleName {
            get => GetValue(AccessibleNameProperty);
            set => SetValue(AccessibleNameProperty, value);
        }

        public string? EmptyItemNoun {
            get => GetValue(EmptyItemNounProperty);
            set => SetValue(EmptyItemNounProperty, value);
        }

        internal IBrush? RingBrush {
            get => GetValue(RingBrushProperty);
            set => SetValue(RingBrushProperty, value);
        }

        public event EventHandler? Invoked;

        private Image? icon;
        private Border? swatch;

        static PickCell() {
            IsSelectedProperty.Changed.AddClassHandler<PickCell>((c, _) => c.PseudoClasses.Set(":selected", c.IsSelected));
            IsEmptyProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            IsMissingProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            IsUnavailableProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            IsNoneProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            IsFullAndUnarmedProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            IconProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            SwatchProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            AccessibleNameProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyTooltip());
            EmptyItemNounProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyTooltip());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            icon = e.NameScope.Find<Image>("PART_Icon");
            swatch = e.NameScope.Find<Border>("PART_Swatch");
            ApplyContentState();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new PickCellAutomationPeer(this);

        internal void RaiseInvoked() => Invoked?.Invoke(this, EventArgs.Empty);

        private void ApplyContentState() {
            PseudoClasses.Set(":empty", IsEmpty && !IsMissing && !IsNone);
            PseudoClasses.Set(":missing", IsMissing);
            PseudoClasses.Set(":unavailable", IsUnavailable || IsFullAndUnarmed);
            PseudoClasses.Set(":none", IsNone);
            if (icon is not null) {
                icon.Source = Icon;
                icon.Opacity = (IsEmpty && !IsNone) || IsMissing ? 0 : 1;
            }
            if (swatch is not null) {
                swatch.IsVisible = Swatch is not null && !IsNone;
                swatch.Background = Swatch is Color color ? new ImmutableSolidColorBrush(color) : null;
            }
            ApplyTooltip();
        }

        private void ApplyTooltip() {
            string? tip = IsFullAndUnarmed ? FullAndUnarmedMessage
                : IsMissing ? AccessibleName
                : IsNone ? AccessibleName
                : IsEmpty ? EmptyTooltip()
                : IsUnavailable && !string.IsNullOrEmpty(AccessibleName) ? $"{AccessibleName} — not available in this save"
                : AccessibleName;
            ToolTip.SetTip(this, tip);
        }

        private string EmptyTooltip() => string.IsNullOrEmpty(EmptyItemNoun)
            ? "Empty slot — press Return to choose"
            : $"Empty slot — press Return to choose a {EmptyItemNoun}";

        private sealed class PickCellAutomationPeer(PickCell owner) : ControlAutomationPeer(owner), IInvokeProvider, ISelectionItemProvider {
            private PickCell Cell => (PickCell)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() =>
                Cell.IsChoiceItem ? AutomationControlType.RadioButton : AutomationControlType.Button;

            protected override string? GetNameCore() {
                string? name = base.GetNameCore();
                return string.IsNullOrEmpty(name) ? (Cell.IsEmpty && !Cell.IsNone ? "Empty" : Cell.AccessibleName) : name;
            }

            void IInvokeProvider.Invoke() => Cell.RaiseInvoked();

            bool ISelectionItemProvider.IsSelected => Cell.IsSelected;
            ISelectionProvider? ISelectionItemProvider.SelectionContainer => null;
            void ISelectionItemProvider.Select() { if (Cell.IsChoiceItem) Cell.RaiseInvoked(); }
            void ISelectionItemProvider.AddToSelection() { if (Cell.IsChoiceItem) Cell.RaiseInvoked(); }
            void ISelectionItemProvider.RemoveFromSelection() { }
        }
    }
}
