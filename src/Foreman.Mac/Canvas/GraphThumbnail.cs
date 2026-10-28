using SkiaSharp;
using System;
using System.Drawing;

namespace Foreman.Mac.Canvas {
    public static class GraphThumbnail {
        public const int Size = 60;

        public static byte[]? Render(GraphViewer viewer) {
            Rectangle bounds = AnnotationLoader.GetExportBounds(viewer.Graph.Bounds, viewer.Annotations);
            if (!GraphExportBounds.IsExportable(bounds))
                return null;

            float scale = Math.Min((float)Size / bounds.Width, (float)Size / bounds.Height);
            int padX = (int)Math.Round(((Size / scale) - bounds.Width) / 2);
            int padY = (int)Math.Round(((Size / scale) - bounds.Height) / 2);
            var transform = new GraphViewer.ExportTransform(0, 0, scale, new Point(-bounds.X + padX, -bounds.Y + padY));

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Clear(viewer.BackgroundColor);
            viewer.Paint(surface.Canvas, fullGraph: true, clearBackground: false, exportTransform: transform);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
