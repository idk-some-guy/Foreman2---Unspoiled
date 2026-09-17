using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;

namespace Foreman.Mac.Kit.Composites {
    //A single module/beacon slot; PickGrid drives selection and focus from outside since cells never receive real focus.
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

        public static readonly StyledProperty<string?> AccessibleNameProperty =
            AvaloniaProperty.Register<PickCell, string?>(nameof(AccessibleName));

        //The empty-slot tooltip's noun ("module", "beacon", ...) - the kit itself names no consumer's
        //vocabulary, so a grid without one set reads as a plain "press Return to choose".
        public static readonly StyledProperty<string?> EmptyItemNounProperty =
            AvaloniaProperty.Register<PickCell, string?>(nameof(EmptyItemNoun));

        internal static readonly StyledProperty<IBrush?> RingBrushProperty =
            AvaloniaProperty.Register<PickCell, IBrush?>(nameof(RingBrush), Brushes.Transparent);

        public IImage? Icon {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
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

        static PickCell() {
            IsSelectedProperty.Changed.AddClassHandler<PickCell>((c, _) => c.PseudoClasses.Set(":selected", c.IsSelected));
            IsEmptyProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            IsMissingProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            IconProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyContentState());
            AccessibleNameProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyTooltip());
            EmptyItemNounProperty.Changed.AddClassHandler<PickCell>((c, _) => c.ApplyTooltip());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            icon = e.NameScope.Find<Image>("PART_Icon");
            ApplyContentState();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new PickCellAutomationPeer(this);

        internal void RaiseInvoked() => Invoked?.Invoke(this, EventArgs.Empty);

        private void ApplyContentState() {
            PseudoClasses.Set(":empty", IsEmpty && !IsMissing);
            PseudoClasses.Set(":missing", IsMissing);
            if (icon is not null) {
                icon.Source = Icon;
                icon.Opacity = IsEmpty || IsMissing ? 0 : 1;
            }
            ApplyTooltip();
        }

        private void ApplyTooltip() {
            string? tip = IsMissing ? AccessibleName
                : IsEmpty ? EmptyTooltip()
                : AccessibleName;
            ToolTip.SetTip(this, tip);
        }

        private string EmptyTooltip() => string.IsNullOrEmpty(EmptyItemNoun)
            ? "Empty slot — press Return to choose"
            : $"Empty slot — press Return to choose a {EmptyItemNoun}";

        private sealed class PickCellAutomationPeer(PickCell owner) : ControlAutomationPeer(owner), IInvokeProvider {
            private PickCell Cell => (PickCell)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

            protected override string? GetNameCore() {
                string? name = base.GetNameCore();
                return string.IsNullOrEmpty(name) ? (Cell.IsEmpty ? "Empty" : Cell.AccessibleName) : name;
            }

            void IInvokeProvider.Invoke() => Cell.RaiseInvoked();
        }
    }
}
