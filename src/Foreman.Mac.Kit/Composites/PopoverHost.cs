using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //Overlay layer a window adds above its content: owns the Canvas a PopoverFrame positions itself against.
    public class PopoverHost : Panel {
        public Canvas Surface { get; } = new();

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

        internal void BringToFront(PopoverFrame frame) {
            if (!Surface.Children.Contains(frame))
                return;
            Surface.Children.Remove(frame);
            Surface.Children.Add(frame);
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
