using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    public class GroupBox : HeaderedContentControl {
        protected override Type StyleKeyOverride => typeof(GroupBox);

        private double maxWidthToken = 420;
        private readonly List<IDisposable> subs = [];

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Metrics.GroupBoxMaxWidth").Subscribe(new Sink(v => { maxWidthToken = v; MaxWidth = maxWidthToken; })));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private sealed class Sink(Action<double> onNext) : IObserver<object?> {
            public void OnNext(object? value) => onNext(value is double d ? d : 420.0);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
