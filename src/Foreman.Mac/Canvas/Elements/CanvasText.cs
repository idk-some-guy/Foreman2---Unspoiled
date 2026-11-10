using Foreman.Mac.Canvas.KitSkia;

namespace Foreman.Mac.Canvas.Elements {
    internal static class CanvasText {
        //A float zoom of 0.7 times 10 lands a hair under the 7 pt floor.
        private const double Tolerance = 0.001;

        internal static bool IsLegible(float fontSize, double viewScale) => (fontSize * viewScale) + Tolerance >= TokenBridge.Current.CanvasMinTextSize;
    }
}
