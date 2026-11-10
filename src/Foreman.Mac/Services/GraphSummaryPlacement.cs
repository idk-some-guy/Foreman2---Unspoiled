using Avalonia;

namespace Foreman.Mac.Services {
    internal static class GraphSummaryPlacement {
        public const double Gap = 12;
        public const double Cascade = 28;

        public static PixelPoint Resolve(PixelRect owner, PixelSize size, PixelRect? screen, double scaling) {
            int gap = (int)System.Math.Round(Gap * scaling);
            int right = owner.Right + gap;
            if (screen is not PixelRect area || right + size.Width <= area.Right)
                return new PixelPoint(right, owner.Y);

            int left = owner.X - gap - size.Width;
            if (left >= area.X)
                return new PixelPoint(left, owner.Y);

            int step = (int)System.Math.Round(Cascade * scaling);
            int x = System.Math.Clamp(owner.X + step, area.X, System.Math.Max(area.X, area.Right - size.Width));
            int y = System.Math.Clamp(owner.Y + step, area.Y, System.Math.Max(area.Y, area.Bottom - size.Height));
            return new PixelPoint(x, y);
        }
    }
}
