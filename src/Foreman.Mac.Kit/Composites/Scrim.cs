using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Primitives;
using System;

namespace Foreman.Mac.Kit.Composites {
    public class Scrim : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(Scrim);

        public static readonly StyledProperty<double> TopInsetProperty =
            AvaloniaProperty.Register<Scrim, double>(nameof(TopInset));

        public double TopInset {
            get => GetValue(TopInsetProperty);
            set => SetValue(TopInsetProperty, value);
        }

        static Scrim() {
            TopInsetProperty.Changed.AddClassHandler<Scrim>((s, _) => s.Margin = new Thickness(0, s.TopInset, 0, 0));
        }

        public Scrim() => IsHitTestVisible = true;

        protected override AutomationPeer OnCreateAutomationPeer() => new ScrimAutomationPeer(this);

        private sealed class ScrimAutomationPeer(Scrim owner) : ControlAutomationPeer(owner) {
            protected override bool IsControlElementCore() => false;
            protected override bool IsContentElementCore() => false;
        }
    }
}
