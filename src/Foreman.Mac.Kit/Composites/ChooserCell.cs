using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using System;

namespace Foreman.Mac.Kit.Composites {
    public class ChooserCell : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ChooserCell);

        public static readonly StyledProperty<IImage?> IconProperty =
            AvaloniaProperty.Register<ChooserCell, IImage?>(nameof(Icon));

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<ChooserCell, string?>(nameof(Label));

        public static readonly StyledProperty<string?> FullNameProperty =
            AvaloniaProperty.Register<ChooserCell, string?>(nameof(FullName));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<ChooserCell, bool>(nameof(IsSelected));

        public static readonly StyledProperty<bool> IsUnavailableProperty =
            AvaloniaProperty.Register<ChooserCell, bool>(nameof(IsUnavailable));

        public IImage? Icon {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string? Label {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string? FullName {
            get => GetValue(FullNameProperty);
            set => SetValue(FullNameProperty, value);
        }

        public bool IsSelected {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public bool IsUnavailable {
            get => GetValue(IsUnavailableProperty);
            set => SetValue(IsUnavailableProperty, value);
        }

        public static readonly StyledProperty<bool> IsIconMissingProperty =
            AvaloniaProperty.Register<ChooserCell, bool>(nameof(IsIconMissing));

        public bool IsIconMissing {
            get => GetValue(IsIconMissingProperty);
            set => SetValue(IsIconMissingProperty, value);
        }

        public event EventHandler? Invoked;

        static ChooserCell() {
            IsSelectedProperty.Changed.AddClassHandler<ChooserCell>((c, _) => c.PseudoClasses.Set(":selected", c.IsSelected));
            IsUnavailableProperty.Changed.AddClassHandler<ChooserCell>((c, _) => c.PseudoClasses.Set(":unavailable", c.IsUnavailable));
            IsIconMissingProperty.Changed.AddClassHandler<ChooserCell>((c, _) => c.PseudoClasses.Set(":icon-missing", c.IsIconMissing));
            FullNameProperty.Changed.AddClassHandler<ChooserCell>((c, _) => ToolTip.SetTip(c, c.FullName));
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            ToolTip.SetTip(this, FullName);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ChooserCellAutomationPeer(this);

        internal void RaiseInvoked() => Invoked?.Invoke(this, EventArgs.Empty);

        private sealed class ChooserCellAutomationPeer(ChooserCell owner) : ControlAutomationPeer(owner), IInvokeProvider {
            private ChooserCell Cell => (ChooserCell)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

            protected override string? GetNameCore() {
                string? name = base.GetNameCore();
                return string.IsNullOrEmpty(name) ? Cell.FullName : name;
            }

            protected override string? GetHelpTextCore() =>
                Cell.IsUnavailable && string.IsNullOrEmpty(AutomationProperties.GetHelpText(Cell)) ? "Unavailable" : base.GetHelpTextCore();

            void IInvokeProvider.Invoke() => Cell.RaiseInvoked();
        }
    }
}
