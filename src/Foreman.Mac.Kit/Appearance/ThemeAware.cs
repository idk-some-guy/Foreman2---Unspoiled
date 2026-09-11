using Avalonia.Controls;
using System;

namespace Foreman.Mac.Kit.Appearance {
    //Subscribes a control to the live AppearanceHub for as long as it's in the visual tree.
    public static class ThemeAware {
        private sealed class Adapter(Action refresh) : IThemeAware {
            public void RefreshTheme() => refresh();
        }

        public static void Attach(Control host, Action refresh) {
            Adapter? adapter = null;
            host.AttachedToVisualTree += (_, _) => {
                adapter = new Adapter(refresh);
                AppearanceHub.Current?.Subscribe(adapter);
            };
            host.DetachedFromVisualTree += (_, _) => {
                if (adapter is not null)
                    AppearanceHub.Current?.Unsubscribe(adapter);
                adapter = null;
            };
        }
    }
}
