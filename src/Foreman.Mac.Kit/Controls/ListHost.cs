using Avalonia;
using Avalonia.Controls;

namespace Foreman.Mac.Kit.Controls {
    public static class ListHost {
        public static readonly AttachedProperty<bool> IsKeyProperty =
            AvaloniaProperty.RegisterAttached<ListBox, bool>("IsKey", typeof(ListHost), true, inherits: true);

        public static bool GetIsKey(ListBox host) => host.GetValue(IsKeyProperty);
        public static void SetIsKey(ListBox host, bool value) => host.SetValue(IsKeyProperty, value);
    }
}
