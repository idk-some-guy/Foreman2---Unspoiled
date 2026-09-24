using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Views.Settings {
    internal static class ListRows {
        public static void SetMinimum(ListBox list, int rows) {
            double rowHeight = 0;
            Thickness framePadding = default;
            List<IDisposable> subs = [];
            void Apply() => list.MinHeight = (rows * rowHeight) + framePadding.Top + framePadding.Bottom + list.BorderThickness.Top + list.BorderThickness.Bottom;
            void Unsubscribe() {
                foreach (IDisposable sub in subs)
                    sub.Dispose();
                subs.Clear();
            }
            list.AttachedToVisualTree += (_, _) => {
                Unsubscribe();
                subs.Add(list.GetResourceObservable("Foreman.Size.ListRowHeight").Subscribe(new Sink(v => {
                    if (v is double height) {
                        rowHeight = height;
                        Apply();
                    }
                })));
                subs.Add(list.GetResourceObservable("Foreman.Thickness.ListFramePadding").Subscribe(new Sink(v => {
                    if (v is Thickness padding) {
                        framePadding = padding;
                        Apply();
                    }
                })));
            };
            list.DetachedFromVisualTree += (_, _) => Unsubscribe();
        }

        private sealed class Sink(Action<object?> onNext) : IObserver<object?> {
            public void OnNext(object? value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
