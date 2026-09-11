using Foreman.Mac.Services;
using System;

namespace Foreman.Mac.Canvas.KitSkia {
    //Minimal canvas token snapshot; full colour migration is later work (dependency graph risk 6).
    public sealed record CanvasTokens(double Scale, double NodePadding, double TypeFontSize);

    public static class TokenBridge {
        private const double BaseNodePadding = 8.0;
        private const double BaseTypeFontSize = 10.0;

        public static CanvasTokens Current { get; private set; } = new(1.0, BaseNodePadding, BaseTypeFontSize);

        //Painters subscribe to re-request a redraw; GraphCanvasControl does.
        public static event EventHandler? Changed;

        public static void Refresh() {
            double scale = WindowScaler.CurrentScale;
            Current = new CanvasTokens(scale, BaseNodePadding * scale, BaseTypeFontSize * scale);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static float Scaled(float baseValue) => (float)(baseValue * Current.Scale);

        public static int ScaledInt(int baseValue) => (int)Math.Round(baseValue * Current.Scale);
    }
}
