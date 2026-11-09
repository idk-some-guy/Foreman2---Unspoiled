using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Foreman.Mac.Services {
    internal static class ThemeResources {
        //ActualThemeVariant throws off the UI thread
        internal static Color ResolveColor(string key, Color fallback) =>
            ResolveColor(key, Dispatcher.UIThread.CheckAccess() ? Application.Current?.ActualThemeVariant ?? ThemeVariant.Light : ThemeVariant.Light, fallback);

        internal static Color ResolveColor(string key, ThemeVariant variant, Color fallback) =>
            Application.Current?.TryGetResource(key, variant, out object? value) == true && value is Color color
                ? color
                : fallback;
    }
}
