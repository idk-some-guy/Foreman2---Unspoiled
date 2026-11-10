using Avalonia;
using Avalonia.Controls;
using System;

namespace Foreman.Mac.Kit.Controls {
    public class RevertButton : Button {
        protected override Type StyleKeyOverride => typeof(RevertButton);

        public static readonly StyledProperty<bool> IsReservedProperty =
            AvaloniaProperty.Register<RevertButton, bool>(nameof(IsReserved));

        static RevertButton() {
            IsReservedProperty.Changed.AddClassHandler<RevertButton>((b, e) => b.PseudoClasses.Set(":reserved", (bool)e.NewValue!));
        }

        public bool IsReserved {
            get => GetValue(IsReservedProperty);
            set => SetValue(IsReservedProperty, value);
        }
    }
}
