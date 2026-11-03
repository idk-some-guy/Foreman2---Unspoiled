using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Reactive;
using System;

namespace Foreman.Mac.Services {
    internal static class LiveBrush {
        public static void Bind(Control target, AvaloniaProperty<IBrush?> property, string key) =>
            target.Bind(property, target.GetResourceObservable(key, AsBrush));

        public static IDisposable Observe(Control target, string key, Action<IBrush> apply) =>
            target.GetResourceObservable(key, AsBrush).Subscribe(new AnonymousObserver<object?>(value => {
                if (value is IBrush brush)
                    apply(brush);
            }));

        private static object? AsBrush(object? value) => value switch {
            IBrush brush => brush,
            Color color => new ImmutableSolidColorBrush(color),
            _ => null,
        };
    }
}
