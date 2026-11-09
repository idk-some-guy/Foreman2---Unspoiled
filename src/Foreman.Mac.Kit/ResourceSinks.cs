using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit {
    internal static class ResourceSinks {
        public sealed class DoubleSink(Action<double> onNext, double seed) : IObserver<object?> {
            private double current = seed;

            public void OnNext(object? value) {
                double next = value is double d ? d : current;
                if (current == next)
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        public sealed class BrushSink(Action<IBrush> onNext, IBrush seed) : IObserver<object?> {
            private IBrush current = seed;

            public void OnNext(object? value) {
                IBrush next = value switch { IBrush b => b, Color c => new SolidColorBrush(c).ToImmutable(), _ => current };
                if (SameBrush(current, next))
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }

            private static bool SameBrush(IBrush a, IBrush b) =>
                ReferenceEquals(a, b) || (a is ISolidColorBrush sa && b is ISolidColorBrush sb && sa.Color == sb.Color);
        }

        public sealed class WeightSink(Action<FontWeight> onNext, FontWeight seed) : IObserver<object?> {
            private FontWeight current = seed;

            public void OnNext(object? value) {
                FontWeight next = value is FontWeight w ? w : current;
                if (current == next)
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        public sealed class RadiusSink(Action<CornerRadius> onNext, CornerRadius seed) : IObserver<object?> {
            private CornerRadius current = seed;

            public void OnNext(object? value) {
                CornerRadius next = value is CornerRadius r ? r : current;
                if (current == next)
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        public sealed class ThicknessSink(Action<Thickness> onNext, Thickness seed) : IObserver<object?> {
            private Thickness current = seed;

            public void OnNext(object? value) {
                Thickness next = value is Thickness t ? t : current;
                if (current == next)
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        public sealed class Sink<T>(Action<T> onNext, T seed) : IObserver<object?> {
            private T current = seed;

            public void OnNext(object? value) {
                T next = value is T t ? t : current;
                if (EqualityComparer<T>.Default.Equals(current, next))
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
