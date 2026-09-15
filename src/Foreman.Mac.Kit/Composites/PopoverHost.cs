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
        private readonly List<Action> pendingMutations = [];

        static PopoverHost() {
            PopoverFrame.IsOpenProperty.Changed.AddClassHandler<PopoverFrame>((frame, e) => {
                if ((bool)e.NewValue! && frame.FindAncestorOfType<PopoverHost>() is { } host)
                    host.BringToFront(frame);
            });
        }

        public PopoverHost() {
            //A bare Panel with no Background has no hit surface for an outside press.
            Background = Brushes.Transparent;
            Children.Add(Surface);
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }

        //Every consumer reaches the Surface through Add/Remove so a mutation fired mid-layout (e.g. TemplateApplied) queues instead of corrupting the enumeration in flight.
        internal void Add(Control popover, Action? onAdded = null) => RunOrDefer(() => {
            Surface.Children.Add(popover);
            onAdded?.Invoke();
        });

        internal void Remove(Control popover) => RunOrDefer(() => Surface.Children.Remove(popover));

        internal void BringToFront(PopoverFrame frame) => RunOrDefer(() => {
            if (!Surface.Children.Contains(frame))
                return;
            Surface.Children.Remove(frame);
            Surface.Children.Add(frame);
        });

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

        private void RunOrDefer(Action mutate) {
            if (layoutDepth == 0) {
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
