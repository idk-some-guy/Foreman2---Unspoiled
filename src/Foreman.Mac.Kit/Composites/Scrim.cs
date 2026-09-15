using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Primitives;
using System;

namespace Foreman.Mac.Kit.Composites {
    //Blocks pointer input under an open Kit.SheetFrame; a click never dismisses (a sheet is window-modal, not a popover).
    public class Scrim : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(Scrim);

        //Set by the host to the titlebar's bottom so the scrim never covers the traffic lights.
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

        //Purely a click-blocker, never a control a screen reader should land on.
        protected override AutomationPeer OnCreateAutomationPeer() => new ScrimAutomationPeer(this);

        private sealed class ScrimAutomationPeer(Scrim owner) : ControlAutomationPeer(owner) {
            protected override bool IsControlElementCore() => false;
            protected override bool IsContentElementCore() => false;
        }
    }
}
