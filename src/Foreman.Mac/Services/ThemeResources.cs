using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Foreman.Mac.Services {
    //Resolves a Foreman.Color.* token for surfaces that build brushes/colors in code instead of through a
    //XAML DynamicResource - row DTOs (GraphSummary/Comparator/EnabledObjects) and the canvas clear color.
    internal static class ThemeResources {
        internal static IBrush ResolveBrush(string key, Color fallback) =>
            new ImmutableSolidColorBrush(ResolveColor(key, fallback));

        //ActualThemeVariant is a StyledProperty and throws off the UI thread (Dispatcher.VerifyAccess) -
        //callers off-thread (e.g. a ViewModel test with no Avalonia dispatcher pumped) get the light
        //fallback rather than a crash.
        internal static Color ResolveColor(string key, Color fallback) =>
            ResolveColor(key, Dispatcher.UIThread.CheckAccess() ? Application.Current?.ActualThemeVariant ?? ThemeVariant.Light : ThemeVariant.Light, fallback);

        internal static Color ResolveColor(string key, ThemeVariant variant, Color fallback) =>
            Application.Current?.TryGetResource(key, variant, out object? value) == true && value is Color color
                ? color
                : fallback;
    }
}
