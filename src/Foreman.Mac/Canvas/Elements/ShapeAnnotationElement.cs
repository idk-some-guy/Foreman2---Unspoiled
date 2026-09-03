using Foreman.Mac.Services;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/ShapeAnnotationElement.cs in full. P3 ported Draw/ToSaveData/
    //FromSaveData; this task adds the P4 half: mutable properties for live editing, the blank "Add Shape"
    //constructors, and SaveDefaults/ApplyDefaults wired to AppSettings' AnnotShape* fields. Unlike
    //TextAnnotationElement there's no cached Skia object to rebuild - Draw already builds its SKPaints fresh
    //per call - so this port has no RebuildGdiObjects counterpart.
    public sealed class ShapeAnnotationElement : AnnotationElement {
        public enum ShapeType { Rectangle, Ellipse }

        private const int DefaultWidth = 200;
        private const int DefaultHeight = 150;

        public ShapeType CurrentShapeType { get; set; }
        public SKColor FillColor { get; set; }
        public SKColor BorderColor { get; set; }
        public int BorderWidth { get; set; }

        private static ShapeType _defaultShapeType = ShapeType.Rectangle;
        private static SKColor _defaultFillColor = ArgbIntToColor(5278975);
        private static SKColor _defaultBorderColor = ArgbIntToColor(-600016676);
        private static int _defaultBorderWidth = 2;

        //Ports the static-field cold-start read of Properties.Settings.Default (reference §6) - see
        //TextAnnotationElement.LoadDefaultsFrom for why this is a method rather than a static initializer.
        public static void LoadDefaultsFrom(AppSettings settings) {
            _defaultShapeType = Enum.IsDefined(typeof(ShapeType), settings.AnnotShapeType) ? (ShapeType)settings.AnnotShapeType : ShapeType.Rectangle;
            _defaultFillColor = ArgbIntToColor(settings.AnnotShapeFillColorARGB);
            _defaultBorderColor = ArgbIntToColor(settings.AnnotShapeBorderColorARGB);
            _defaultBorderWidth = settings.AnnotShapeBorderWidth;
        }

        //Ports SaveDefaults (reference §6) - see TextAnnotationElement.SaveDefaults for the persistence split
        //(statics always updated, AppSettings only when supplied, disk write left to the caller).
        public static void SaveDefaults(ShapeAnnotationElement element, AppSettings? settings) {
            _defaultShapeType = element.CurrentShapeType;
            _defaultFillColor = element.FillColor;
            _defaultBorderColor = element.BorderColor;
            _defaultBorderWidth = element.BorderWidth;

            if (settings is null)
                return;
            settings.AnnotShapeType = (int)_defaultShapeType;
            settings.AnnotShapeFillColorARGB = ColorToArgbInt(_defaultFillColor);
            settings.AnnotShapeBorderColorARGB = ColorToArgbInt(_defaultBorderColor);
            settings.AnnotShapeBorderWidth = _defaultBorderWidth;
        }

        //Ports the interactive "Add Shape" constructors (reference §6): the parameterless-size overload is
        //Annotation_FinishDrawShape's fallback for a too-small drag; the width/height overload is the drawn
        //rubber-band's normal outcome.
        public ShapeAnnotationElement(Point graphLocation) : this(graphLocation, DefaultWidth, DefaultHeight) {
        }

        public ShapeAnnotationElement(Point graphLocation, int width, int height) : base(graphLocation, width, height) {
            CurrentShapeType = _defaultShapeType;
            FillColor = _defaultFillColor;
            BorderColor = _defaultBorderColor;
            BorderWidth = _defaultBorderWidth;
        }

        private ShapeAnnotationElement(Point location, Size size, ShapeType shapeType, SKColor fillColor, SKColor borderColor, int borderWidth)
            : base(location, size.Width, size.Height) {
            CurrentShapeType = shapeType;
            FillColor = fillColor;
            BorderColor = borderColor;
            BorderWidth = borderWidth;
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            Rectangle r = GetGraphRect();
            DrawSelectionHighlight(canvas, r);
            var skRect = new SKRect(r.Left, r.Top, r.Right, r.Bottom);

            if (FillColor.Alpha > 0) {
                SKPaint fillPaint = GraphicsStuff.RentFillPaint(FillColor);
                DrawShape(canvas, skRect, fillPaint);
            }

            if (BorderWidth > 0 && BorderColor.Alpha > 0) {
                float strokeWidth = Math.Max(1, BorderWidth);
                SKRect insetRect = SKRect.Inflate(skRect, -strokeWidth / 2f, -strokeWidth / 2f);
                SKPaint borderPaint = GraphicsStuff.RentStrokePaint(BorderColor, strokeWidth);
                DrawShape(canvas, insetRect, borderPaint);
            }

            DrawResizeHandles(canvas);
        }

        private void DrawShape(SKCanvas canvas, SKRect rect, SKPaint paint) {
            switch (CurrentShapeType) {
                case ShapeType.Rectangle:
                    canvas.DrawRect(rect, paint);
                    break;
                case ShapeType.Ellipse:
                    canvas.DrawOval(rect, paint);
                    break;
            }
        }

        public override AnnotationSaveData ToSaveData() => new ShapeAnnotationSaveData {
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ShapeType = CurrentShapeType.ToString(),
            FillColor = ColorToSave(FillColor),
            BorderColor = ColorToSave(BorderColor),
            BorderWidth = BorderWidth
        };

        public static ShapeAnnotationElement FromSaveData(ShapeAnnotationSaveData data) {
            ShapeType shapeType = Enum.TryParse(data.ShapeType, out ShapeType parsed) ? parsed : ShapeType.Rectangle;
            return new ShapeAnnotationElement(
                new Point(data.X, data.Y),
                new Size(data.Width, data.Height),
                shapeType,
                ColorFromSave(data.FillColor),
                ColorFromSave(data.BorderColor),
                data.BorderWidth);
        }
    }
}
