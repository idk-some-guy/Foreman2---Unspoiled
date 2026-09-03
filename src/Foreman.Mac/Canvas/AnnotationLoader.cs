using Foreman.DataCaching;
using Foreman.Mac.Canvas.Elements;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    //Ports the load-only slice of ProductionGraphViewer.Annotations.cs (LoadAnnotationsFromSave,
    //TryCreateAnnotationFromSave, GetAnnotationAtPoint, GetExportBounds) as free functions, since the
    //ProductionGraphViewer host itself hasn't landed (that's task 12's orchestration layer). deviceDpi
    //stands in for upstream's Control.DeviceDpi read, which has no equivalent until that control exists.
    public static class AnnotationLoader {
        public static IReadOnlyList<AnnotationElement> LoadFromSave(
            IReadOnlyList<AnnotationSaveData> annotations, int? savedDpi, int deviceDpi) {
            List<AnnotationElement> loaded = [];
            if (annotations.Count == 0)
                return loaded;

            float dpiScale = savedDpi is int dpi && dpi > 0 ? deviceDpi / (float)dpi : 1f;

            foreach (AnnotationSaveData data in annotations)
                if (TryCreateAnnotationFromSave(data, dpiScale, out AnnotationElement? annotation))
                    loaded.Add(annotation);

            return loaded;
        }

        public static bool TryCreateAnnotationFromSave(
            AnnotationSaveData data, float dpiScale, [NotNullWhen(true)] out AnnotationElement? annotation) {
            try {
                annotation = AnnotationElement.FromSaveData(data);
                if (Math.Abs(dpiScale - 1f) > 0.01f && annotation is TextAnnotationElement text) {
                    text.Width = (int)Math.Round(text.Width * dpiScale);
                    text.Height = (int)Math.Round(text.Height * dpiScale);
                }
                return true;
            } catch (Exception ex) {
                ErrorLogging.LogLine($"Skipping bad annotation: {ex.Message}");
                annotation = null;
                return false;
            }
        }

        //Ports GetAnnotationAtPoint (reference §6): PickAtPoint tests resize handles too when selected, so a
        //handle sitting just outside the bounds still resolves to this annotation instead of falling through.
        public static AnnotationElement? GetAnnotationAtPoint(IReadOnlyList<AnnotationElement> annotations, Point point) {
            for (int i = annotations.Count - 1; i >= 0; i--)
                if (annotations[i].PickAtPoint(point))
                    return annotations[i];
            return null;
        }

        public static Rectangle GetExportBounds(Rectangle graphBounds, IReadOnlyList<AnnotationElement> annotations) =>
            GraphExportBounds.Compute(graphBounds, annotations.Select(GraphExportBounds.GetGraphBounds));
    }
}
