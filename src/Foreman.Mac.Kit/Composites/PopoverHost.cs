using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //Overlay layer a window adds above its content: owns the Canvas a PopoverFrame positions itself against.
    public class PopoverHost : Panel {
        public Canvas Surface { get; } = new();

        private int layoutDepth;
        private bool isDetaching;
        private readonly List<Action> pendingMutations = [];
        private Window? topLevelWindow;

        static PopoverHost() {
            PopoverFrame.IsOpenProperty.Changed.AddClassHandler<PopoverFrame>((frame, e) => {
                if (frame.FindAncestorOfType<PopoverHost>() is not { } host)
                    return;
                if ((bool)e.NewValue!)
                    host.BringToFront(frame);
                host.UpdateBackground();
            });
        }

        public PopoverHost() {
            Children.Add(Surface);
            Surface.Children.CollectionChanged += (_, _) => UpdateBackground();
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            //A Panel with a null Background has no hit surface of its own; Surface children still hit-test on their own.
            UpdateBackground();
        }

        //Every consumer reaches the Surface through Add/Remove so a mutation fired mid-layout (e.g. TemplateApplied) queues instead of corrupting the enumeration in flight.
        internal void Add(Control popover, Action? onAdded = null) => RunOrDefer(() => {
            Surface.Children.Add(popover);
            onAdded?.Invoke();
        });

        internal void Remove(Control popover) => RunOrDefer(() => Surface.Children.Remove(popover));

        private void UpdateBackground() =>
            Background = Surface.Children.OfType<PopoverFrame>().Any(f => f.IsOpen) ? Brushes.Transparent : null;

        internal void BringToFront(PopoverFrame frame) => RunOrDefer(() => {
            if (!Surface.Children.Contains(frame))
                return;
            Surface.Children.Remove(frame);
            Surface.Children.Add(frame);
        });

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            isDetaching = false;
            topLevelWindow = TopLevel.GetTopLevel(this) as Window;
            if (topLevelWindow is not null)
                topLevelWindow.Deactivated += OnTopLevelDeactivated;
        }

        //Set before the base call recurses into children, so a hosted control's own detach handler defers instead of mutating the Surface mid-cascade.
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            isDetaching = true;
            if (topLevelWindow is not null)
                topLevelWindow.Deactivated -= OnTopLevelDeactivated;
            topLevelWindow = null;
            base.OnDetachedFromVisualTree(e);
        }

        //macOS dismisses an open popover when its window resigns key - closing every open frame here mirrors that.
        private void OnTopLevelDeactivated(object? sender, EventArgs e) {
            foreach (PopoverFrame frame in Surface.Children.OfType<PopoverFrame>().Where(f => f.IsOpen).ToList())
                frame.Close();
        }

        protected override Size MeasureOverride(Size availableSize) {
            layoutDepth++;
            try {
                return base.MeasureOverride(availableSize);
            } finally {
                layoutDepth--;
            }
        }

        protected override Size ArrangeOverride(Size finalSize) {
            layoutDepth++;
            try {
                return base.ArrangeOverride(finalSize);
            } finally {
                layoutDepth--;
            }
        }

        //Mid-layout or mid-detach both defer, so a mutation never corrupts a pass or cascade in flight.
        private void RunOrDefer(Action mutate) {
            if (layoutDepth == 0 && !isDetaching) {
                mutate();
                return;
            }
            pendingMutations.Add(mutate);
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushPending);
        }

        private void FlushPending() {
            if (pendingMutations.Count == 0)
                return;
            Action[] queued = [.. pendingMutations];
            pendingMutations.Clear();
            foreach (Action mutate in queued)
                mutate();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e) {
            PopoverFrame? open = Surface.Children.OfType<PopoverFrame>().FirstOrDefault(f => f.IsOpen);
            if (open is null)
                return;
            Point local = e.GetPosition(open);
            if (new Rect(open.Bounds.Size).Contains(local))
                return;
            open.Close();
            //macOS light-dismiss swallows the click that closes an overlay.
            e.Handled = true;
        }
    }
}
