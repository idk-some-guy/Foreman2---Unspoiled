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

        public GroupBox() => MaxWidth = maxWidthToken;

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
            subs.Add(this.GetResourceObservable("Foreman.Metrics.GroupBoxMaxWidth").Subscribe(new ResourceSinks.DoubleSink(v => { maxWidthToken = v; MaxWidth = maxWidthToken; }, maxWidthToken)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

    }
}
