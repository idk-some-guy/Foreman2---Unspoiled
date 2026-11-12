using Avalonia;
using Avalonia.Controls;
using Foreman.Mac.Services;
using System;
using System.Linq;

namespace Foreman.Mac.Views.Research {
    internal static class ResearchPlacement {
        public const double DefaultWidth = 620;
        public const double DefaultHeight = 600;

        public static bool HasSavedPosition(AppSettings? settings) => settings is { ResearchLeft: not null, ResearchTop: not null };

        public static void Apply(Window window, AppSettings? settings) {
            window.Width = DefaultWidth * WindowScaler.CurrentScale;
            window.Height = DefaultHeight * WindowScaler.CurrentScale;
            if (settings is null)
                return;
            if (settings.ResearchWidth is double width && settings.ResearchHeight is double height) {
                window.Width = Math.Max(width * WindowScaler.CurrentScale, window.MinWidth);
                window.Height = Math.Max(height * WindowScaler.CurrentScale, window.MinHeight);
            }
            if (settings.ResearchLeft is double left && settings.ResearchTop is double top) {
                var saved = new PixelPoint((int)left, (int)top);
                if (window.Screens.All.Count == 0 || window.Screens.All.Any(s => s.WorkingArea.Contains(saved)))
                    window.Position = saved;
            }
        }

        public static void Remember(Window window, AppSettings? settings, int segment) {
            if (settings is null)
                return;
            if (window.ClientSize.Width > 0 && window.ClientSize.Height > 0) {
                settings.ResearchWidth = window.ClientSize.Width / WindowScaler.CurrentScale;
                settings.ResearchHeight = window.ClientSize.Height / WindowScaler.CurrentScale;
            }
            settings.ResearchLeft = window.Position.X;
            settings.ResearchTop = window.Position.Y;
            settings.ResearchSegment = segment;
        }

        public static PixelPoint Resolve(PixelRect owner, PixelSize size, PixelRect? screen, double scaling) =>
            GraphSummaryPlacement.Resolve(owner, size, screen, scaling);
    }
}
