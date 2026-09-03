using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class AnnotationRenderTests {
        private const int Half = 200;

        private static SKSurface Render(AnnotationElement element) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Translate(Half, Half);
            element.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            return surface;
        }

        private static SKColor SamplePixel(SKSurface surface, int graphX, int graphY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(Half + graphX, Half + graphY);
        }

        //---- load-from-save (mirrors AnnotationJsonTests' inline-JSON idiom, since no fixture .fjson has annotations) ----

        [Fact]
        public void LoadFromSave_ConstructedSaveJson_CreatesTypedElements() {
            JsonNode? root = JsonNode.Parse("""
                {
                  "Annotations": [
                    { "Type": "Text", "X": 10, "Y": 20, "Width": 100, "Height": 50, "Text": "hello", "FontFamily": "Arial", "FontSize": 18, "FontStyle": 0, "TextColor": { "A": 255, "R": 1, "G": 2, "B": 3 }, "BackColor": { "A": 0, "R": 0, "G": 0, "B": 0 }, "TextAlign": 1 },
                    { "Type": "Shape", "X": -10, "Y": -20, "Width": 80, "Height": 60, "ShapeType": "Ellipse", "FillColor": { "A": 255, "R": 4, "G": 5, "B": 6 }, "BorderColor": { "A": 255, "R": 7, "G": 8, "B": 9 }, "BorderWidth": 3 }
                  ]
                }
                """);
            IReadOnlyList<AnnotationSaveData>? saved = AnnotationJson.DeserializeListFromRoot(root);
            Assert.NotNull(saved);

            IReadOnlyList<AnnotationElement> loaded = AnnotationLoader.LoadFromSave(saved, savedDpi: null, deviceDpi: 96);

            Assert.Equal(2, loaded.Count);
            var text = Assert.IsType<TextAnnotationElement>(loaded[0]);
            Assert.Equal("hello", text.Text);
            Assert.Equal(new Point(10, 20), text.Location);
            var shape = Assert.IsType<ShapeAnnotationElement>(loaded[1]);
            Assert.Equal(ShapeAnnotationElement.ShapeType.Ellipse, shape.CurrentShapeType);
            Assert.Equal(new Point(-10, -20), shape.Location);
        }

        [Fact]
        public void LoadFromSave_EmptyList_ReturnsEmpty() {
            IReadOnlyList<AnnotationElement> loaded = AnnotationLoader.LoadFromSave([], savedDpi: null, deviceDpi: 96);

            Assert.Empty(loaded);
        }

        [Fact]
        public void LoadFromSave_SavedDpiHalfOfDevice_ScalesTextAnnotationBoxUp() {
            var data = new TextAnnotationSaveData { X = 0, Y = 0, Width = 100, Height = 40, Text = "x" };

            IReadOnlyList<AnnotationElement> loaded = AnnotationLoader.LoadFromSave([data], savedDpi: 48, deviceDpi: 96);

            var text = Assert.IsType<TextAnnotationElement>(loaded[0]);
            Assert.Equal(200, text.Width);
            Assert.Equal(80, text.Height);
        }

        //---- shape fill/border pixel assertions ----

        [Fact]
        public void ShapeAnnotationElement_Rectangle_FillAndBorderPixelsMatchSaveData() {
            var fillColor = new ColorSaveData(255, 10, 20, 200);
            var borderColor = new ColorSaveData(255, 250, 5, 5);
            var data = new ShapeAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 150,
                ShapeType = "Rectangle", FillColor = fillColor, BorderColor = borderColor, BorderWidth = 10
            };
            var element = ShapeAnnotationElement.FromSaveData(data);

            using SKSurface surface = Render(element);

            Assert.Equal(new SKColor(10, 20, 200), SamplePixel(surface, 0, 0));
            Assert.Equal(new SKColor(250, 5, 5), SamplePixel(surface, 0, -70));
        }

        [Fact]
        public void ShapeAnnotationElement_Ellipse_FillAndBorderPixelsMatchSaveData() {
            var fillColor = new ColorSaveData(255, 30, 40, 50);
            var borderColor = new ColorSaveData(255, 200, 100, 50);
            var data = new ShapeAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 150,
                ShapeType = "Ellipse", FillColor = fillColor, BorderColor = borderColor, BorderWidth = 10
            };
            var element = ShapeAnnotationElement.FromSaveData(data);

            using SKSurface surface = Render(element);

            Assert.Equal(new SKColor(30, 40, 50), SamplePixel(surface, 0, 0));
            Assert.Equal(new SKColor(200, 100, 50), SamplePixel(surface, 0, -70));
        }

        [Fact]
        public void ShapeAnnotationElement_ZeroAlphaFillAndBorder_DrawsNothing() {
            var data = new ShapeAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 150, ShapeType = "Rectangle",
                FillColor = new ColorSaveData(0, 10, 20, 200), BorderColor = new ColorSaveData(0, 250, 5, 5), BorderWidth = 10
            };
            var element = ShapeAnnotationElement.FromSaveData(data);

            using SKSurface surface = Render(element);

            Assert.Equal(SKColors.White, SamplePixel(surface, 0, 0));
            Assert.Equal(SKColors.White, SamplePixel(surface, 0, -70));
        }

        //---- selection handles (reference §6's DrawResizeHandles) ----

        [Fact]
        public void DrawResizeHandles_SelectedAnnotation_BorderPixelMatchesUpstreamsColorFromArgb60_100_200() {
            var shape = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 150, ShapeType = "Rectangle",
                FillColor = new ColorSaveData(0, 0, 0, 0), BorderColor = new ColorSaveData(0, 0, 0, 0), BorderWidth = 0
            });
            shape.IsSelected = true;
            //ViewScale = 0.3 widens the handle's own draw half-size and stroke width enough (both divide by
            //ViewScale) that a pixel sampled exactly on the border's edge centerline reads the pure stroke
            //color instead of an AA-blended tint, while staying small enough that DrawSelectionHighlight's own
            //much larger translucent-blue outline (its stroke width is 2.5/ViewScale too) doesn't reach as far
            //out as the TopLeft handle and contaminate the sample.
            shape.Context = new AnnotationElementContext { ViewScale = () => 0.3f, SelectedAnnotations = [], SelectedNodeCount = () => 0 };

            using SKSurface surface = Render(shape);

            //TopLeft handle center is (-100,-75); its border rect's top edge sits at y=-91 (drawHalf=16 at
            //this ViewScale), sampled at the rect's own x-center.
            Assert.Equal(new SKColor(60, 100, 200), SamplePixel(surface, -100, -91));
        }

        //Phase 7 task 2 (perf-packaging-reference.md §1b): pins DrawSelectionHighlight's translucent-blue
        //stroke pixel so converting its per-frame `new SKPaint` (AnnotationElement.cs:251) to a mutate-reset
        //per-instance paint (its StrokeWidth tracks 2.5/ViewScale every frame) can't drift it.
        [Fact]
        public void DrawSelectionHighlight_SelectedAnnotation_StrokeBlendsOverBackground() {
            var shape = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 150, ShapeType = "Rectangle",
                FillColor = new ColorSaveData(0, 0, 0, 0), BorderColor = new ColorSaveData(0, 0, 0, 0), BorderWidth = 0
            });
            shape.IsSelected = true;
            //Same ViewScale=0.3 rationale as DrawResizeHandles_SelectedAnnotation_... above: widens the
            //2.5/ViewScale stroke enough for a clean, non-AA-blended center sample. x=-40 on the top edge
            //sits clear of both the TopLeft(-100,-75) and TopCenter(0,-75) handles' own draw radius.
            shape.Context = new AnnotationElementContext { ViewScale = () => 0.3f, SelectedAnnotations = [], SelectedNodeCount = () => 0 };

            using SKSurface surface = Render(shape);

            Assert.Equal(new SKColor(104, 173, 255), SamplePixel(surface, -40, -83));
        }

        //---- text presence ----

        [Fact]
        public void TextAnnotationElement_NonEmptyText_PaintsInkSomewhereInBounds() {
            var data = new TextAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 100,
                Text = "AAAA", FontFamily = "Segoe UI", FontSize = 60, FontStyle = 1,
                TextColor = new ColorSaveData(255, 0, 0, 0), BackColor = new ColorSaveData(0, 255, 255, 255), TextAlign = 1
            };
            var element = TextAnnotationElement.FromSaveData(data);

            using SKSurface surface = Render(element);
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();

            bool foundInk = false;
            for (int x = -100; x < 100 && !foundInk; x++)
                for (int y = -50; y < 50; y++)
                    if (pixmap.GetPixelColor(Half + x, Half + y) != SKColors.White) {
                        foundInk = true;
                        break;
                    }

            Assert.True(foundInk);
        }

        [Fact]
        public void TextAnnotationElement_EmptyText_PaintsNoInk() {
            var data = new TextAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 100, Text = "",
                TextColor = new ColorSaveData(255, 0, 0, 0), BackColor = new ColorSaveData(0, 255, 255, 255)
            };
            var element = TextAnnotationElement.FromSaveData(data);

            using SKSurface surface = Render(element);

            Assert.Equal(SKColors.White, SamplePixel(surface, 0, 0));
        }

        [Fact]
        public void TextAnnotationElement_OpaqueBackColor_FillsBoundsBeforeText() {
            var data = new TextAnnotationSaveData {
                X = 0, Y = 0, Width = 200, Height = 100, Text = "",
                BackColor = new ColorSaveData(255, 15, 25, 35)
            };
            var element = TextAnnotationElement.FromSaveData(data);

            using SKSurface surface = Render(element);

            Assert.Equal(new SKColor(15, 25, 35), SamplePixel(surface, 90, 40));
        }

        //---- ContainsPointFull / ForceVisible ----

        [Fact]
        public void ContainsPointFull_PointInsideBounds_ReturnsTrue() {
            var data = new ShapeAnnotationSaveData { X = 50, Y = 50, Width = 200, Height = 100 };
            var element = ShapeAnnotationElement.FromSaveData(data);

            Assert.True(element.ContainsPointFull(new Point(50, 50)));
        }

        [Fact]
        public void ContainsPointFull_PointOutsideBounds_ReturnsFalse() {
            var data = new ShapeAnnotationSaveData { X = 50, Y = 50, Width = 200, Height = 100 };
            var element = ShapeAnnotationElement.FromSaveData(data);

            Assert.False(element.ContainsPointFull(new Point(1000, 1000)));
        }

        [Fact]
        public void ForceVisible_AfterOutOfZoneUpdate_RestoresHitTesting() {
            var data = new ShapeAnnotationSaveData { X = 0, Y = 0, Width = 200, Height = 100 };
            var element = ShapeAnnotationElement.FromSaveData(data);
            element.UpdateVisibility(new Rectangle(10_000, 10_000, 10, 10));
            Assert.False(element.ContainsPointFull(new Point(0, 0)));

            element.ForceVisible();

            Assert.True(element.ContainsPointFull(new Point(0, 0)));
        }

        //---- GetExportBounds math ----

        [Fact]
        public void GraphExportBounds_GetGraphBounds_CentersOnAnnotationLocation() {
            var data = new ShapeAnnotationSaveData { X = 10, Y = 20, Width = 100, Height = 50 };
            var element = ShapeAnnotationElement.FromSaveData(data);

            Rectangle bounds = GraphExportBounds.GetGraphBounds(element);

            Assert.Equal(new Rectangle(-40, -5, 100, 50), bounds);
        }

        [Fact]
        public void GraphExportBounds_Compute_GraphAndAnnotationsPresent_UnionsWithNoPadding() {
            var graphBounds = new Rectangle(0, 0, 100, 100);
            Rectangle[] annotationBounds = [new(-40, -5, 100, 50)];

            Rectangle result = GraphExportBounds.Compute(graphBounds, annotationBounds);

            Assert.Equal(new Rectangle(-40, -5, 140, 105), result);
        }

        [Fact]
        public void GraphExportBounds_Compute_AnnotationsOnlyNoGraph_AddsPadding() {
            Rectangle[] annotationBounds = [new(-40, -5, 100, 50)];

            Rectangle result = GraphExportBounds.Compute(Rectangle.Empty, annotationBounds);

            Assert.Equal(new Rectangle(-90, -55, 200, 150), result);
        }

        [Fact]
        public void GraphExportBounds_Compute_GraphOnlyNoAnnotations_ReturnsGraphBoundsUnchanged() {
            var graphBounds = new Rectangle(5, 5, 40, 30);

            Rectangle result = GraphExportBounds.Compute(graphBounds, []);

            Assert.Equal(graphBounds, result);
        }

        [Fact]
        public void GraphExportBounds_Compute_NeitherPresent_ReturnsEmpty() {
            Rectangle result = GraphExportBounds.Compute(Rectangle.Empty, []);

            Assert.Equal(Rectangle.Empty, result);
        }

        [Fact]
        public void AnnotationLoader_GetExportBounds_MatchesDirectComputeOverLoadedElements() {
            var data = new ShapeAnnotationSaveData { X = 10, Y = 20, Width = 100, Height = 50 };
            IReadOnlyList<AnnotationElement> loaded = AnnotationLoader.LoadFromSave([data], savedDpi: null, deviceDpi: 96);
            var graphBounds = new Rectangle(0, 0, 100, 100);

            Rectangle result = AnnotationLoader.GetExportBounds(graphBounds, loaded);

            Assert.Equal(GraphExportBounds.Compute(graphBounds, loaded.Select(GraphExportBounds.GetGraphBounds)), result);
        }

        [Fact]
        public void AnnotationLoader_GetAnnotationAtPoint_ReturnsTopmostHit() {
            var back = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData { X = 0, Y = 0, Width = 200, Height = 200 });
            var front = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData { X = 0, Y = 0, Width = 50, Height = 50 });
            AnnotationElement[] elements = [back, front];

            AnnotationElement? hit = AnnotationLoader.GetAnnotationAtPoint(elements, new Point(0, 0));

            Assert.Same(front, hit);
        }

        [Fact]
        public void AnnotationLoader_GetAnnotationAtPoint_NoHit_ReturnsNull() {
            var element = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData { X = 0, Y = 0, Width = 20, Height = 20 });

            AnnotationElement? hit = AnnotationLoader.GetAnnotationAtPoint([element], new Point(1000, 1000));

            Assert.Null(hit);
        }

        //---- round-trip stability ----

        [Fact]
        public void TextAnnotationElement_ToSaveData_RoundTripsThroughFromSaveData() {
            var original = new TextAnnotationSaveData {
                X = 12, Y = 34, Width = 100, Height = 50, Text = "Label",
                FontFamily = "Arial", FontSize = 16f, FontStyle = 3,
                TextColor = new ColorSaveData(255, 10, 20, 30), BackColor = new ColorSaveData(128, 40, 50, 60), TextAlign = 2
            };

            var restored = (TextAnnotationSaveData)TextAnnotationElement.FromSaveData(original).ToSaveData();

            Assert.Equal(original.X, restored.X);
            Assert.Equal(original.Y, restored.Y);
            Assert.Equal(original.Width, restored.Width);
            Assert.Equal(original.Height, restored.Height);
            Assert.Equal(original.Text, restored.Text);
            Assert.Equal(original.FontFamily, restored.FontFamily);
            Assert.Equal(original.FontSize, restored.FontSize);
            Assert.Equal(original.FontStyle, restored.FontStyle);
            Assert.Equal(original.TextColor.A, restored.TextColor.A);
            Assert.Equal(original.TextColor.R, restored.TextColor.R);
            Assert.Equal(original.TextColor.G, restored.TextColor.G);
            Assert.Equal(original.TextColor.B, restored.TextColor.B);
            Assert.Equal(original.BackColor.A, restored.BackColor.A);
            Assert.Equal(original.TextAlign, restored.TextAlign);
        }

        [Fact]
        public void ShapeAnnotationElement_ToSaveData_RoundTripsThroughFromSaveData() {
            var original = new ShapeAnnotationSaveData {
                X = 1, Y = 2, Width = 80, Height = 90, ShapeType = "Ellipse",
                FillColor = new ColorSaveData(80, 1, 2, 3), BorderColor = new ColorSaveData(255, 4, 5, 6), BorderWidth = 3
            };

            var restored = (ShapeAnnotationSaveData)ShapeAnnotationElement.FromSaveData(original).ToSaveData();

            Assert.Equal(original.X, restored.X);
            Assert.Equal(original.Y, restored.Y);
            Assert.Equal(original.Width, restored.Width);
            Assert.Equal(original.Height, restored.Height);
            Assert.Equal(original.ShapeType, restored.ShapeType);
            Assert.Equal(original.BorderWidth, restored.BorderWidth);
            Assert.Equal(original.FillColor.R, restored.FillColor.R);
            Assert.Equal(original.BorderColor.G, restored.BorderColor.G);
        }

        [Fact]
        public void AnnotationElement_FromSaveData_UnknownDerivedType_Throws() {
            AnnotationSaveData bogus = new BogusAnnotationSaveData();

            Assert.Throws<InvalidOperationException>(() => AnnotationElement.FromSaveData(bogus));
        }

        private sealed class BogusAnnotationSaveData : AnnotationSaveData;

        //---- reference render: text + rect + ellipse, for the SDD workspace ----

        [Fact]
        public void RenderTextRectAndEllipse_WritesReferencePngToSddWorkspace() {
            var text = TextAnnotationElement.FromSaveData(new TextAnnotationSaveData {
                X = -120, Y = -100, Width = 180, Height = 60, Text = "Notes", FontFamily = "Segoe UI", FontSize = 22, FontStyle = 1,
                TextColor = new ColorSaveData(255, 20, 20, 20), BackColor = new ColorSaveData(255, 245, 245, 200), TextAlign = 1
            });
            var rect = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData {
                X = 100, Y = -80, Width = 160, Height = 100, ShapeType = "Rectangle",
                FillColor = new ColorSaveData(80, 80, 160, 255), BorderColor = new ColorSaveData(255, 60, 120, 220), BorderWidth = 4
            });
            var ellipse = ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData {
                X = 0, Y = 120, Width = 220, Height = 120, ShapeType = "Ellipse",
                FillColor = new ColorSaveData(120, 220, 140, 60), BorderColor = new ColorSaveData(255, 180, 90, 20), BorderWidth = 6
            });

            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Translate(Half, Half);
            text.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            rect.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            ellipse.Paint(surface.Canvas, NodeDrawingStyle.Regular);

            string workspaceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".superpowers", "sdd", "2026-09-01-phase3-canvas-readonly");
            Directory.CreateDirectory(workspaceDir);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream file = File.OpenWrite(Path.Combine(workspaceDir, "task-5-annotations.png"));
            data.SaveTo(file);
            surface.Dispose();

            Assert.True(File.Exists(Path.Combine(workspaceDir, "task-5-annotations.png")));
        }
    }
}
