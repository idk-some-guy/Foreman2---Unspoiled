using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Drawing;
using System.Globalization;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/TextAnnotationElement.cs in full. P3 ported Draw/ToSaveData/
    //FromSaveData; this task adds the P4 half: mutable properties for live editing, font-size resize math,
    //the blank "Add Text" constructor, and SaveDefaults/ApplyDefaults wired to AppSettings' AnnotText* fields
    //(present since phase 2, unused until now). GDI's StringFormat with NoWrap+Trimming.None+
    //LineAlignment.Center is still reproduced directly (see the P3 header note); font family resolution still
    //goes through AnnotationFontResolver.
    public sealed class TextAnnotationElement : AnnotationElement {
        private const int DefaultWidth = 200;
        private const int DefaultHeight = 60;

        public string Text { get; set; }
        public string FontFamily { get; set; }
        public float FontSize { get; set; }
        public int FontStyleFlags { get; set; }
        public SKColor TextColor { get; set; }
        public SKColor BackColor { get; set; }
        public int TextAlign { get; set; }

        private SKTypeface _typeface;
        private float _resizeStartFontSize;

        //Per-instance mutate-reset paints/font: colors and font size are per-element and user-editable.
        private readonly SKPaint _backPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _textPaint = new() { IsAntialias = true };
        private SKFont _font;

        private static string _defaultFontFamily = "Segoe UI";
        private static float _defaultFontSize = 14f;
        private static int _defaultFontStyleFlags = 1;
        private static SKColor _defaultTextColor = new(0, 0, 0, 255);
        private static SKColor _defaultBackColor = new(0, 0, 0, 0);
        private static int _defaultTextAlign = 1;

        //Ports the static-field cold-start read of Properties.Settings.Default (reference §6): our AppSettings
        //isn't available at static-init time (it's loaded async by SettingsService), so MainWindow calls this
        //once settings are ready instead, seeding the same statics upstream's field initializers do inline.
        public static void LoadDefaultsFrom(AppSettings settings) {
            _defaultFontFamily = settings.AnnotTextFontFamily;
            _defaultFontSize = float.TryParse(settings.AnnotTextFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out float fs) ? fs : 14f;
            _defaultFontStyleFlags = settings.AnnotTextFontStyle;
            _defaultTextColor = ArgbIntToColor(settings.AnnotTextColorARGB);
            _defaultBackColor = ArgbIntToColor(settings.AnnotTextBackColorARGB);
            _defaultTextAlign = settings.AnnotTextAlign;
        }

        //Ports SaveDefaults (reference §6): remembers this element's current values as the session-lifetime
        //defaults and, when settings is supplied, writes them through to AppSettings so the next launch starts
        //from the last-confirmed values too - persistence to disk is the caller's job (MainWindow already
        //flushes AppSettings on window close).
        public static void SaveDefaults(TextAnnotationElement element, AppSettings? settings) {
            _defaultFontFamily = element.FontFamily;
            _defaultFontSize = element.FontSize;
            _defaultFontStyleFlags = element.FontStyleFlags;
            _defaultTextColor = element.TextColor;
            _defaultBackColor = element.BackColor;
            _defaultTextAlign = element.TextAlign;

            if (settings is null)
                return;
            settings.AnnotTextFontFamily = _defaultFontFamily;
            settings.AnnotTextFontSize = _defaultFontSize.ToString(CultureInfo.InvariantCulture);
            settings.AnnotTextFontStyle = _defaultFontStyleFlags;
            settings.AnnotTextColorARGB = ColorToArgbInt(_defaultTextColor);
            settings.AnnotTextBackColorARGB = ColorToArgbInt(_defaultBackColor);
            settings.AnnotTextAlign = _defaultTextAlign;
        }

        //Ports the interactive "Add Text" constructor (reference §6): starts from the remembered defaults,
        //fits the box to the placeholder text around the unchanged click-center.
        public TextAnnotationElement(Point graphLocation) : base(graphLocation, DefaultWidth, DefaultHeight) {
            Text = "Label";
            FontFamily = _defaultFontFamily;
            FontSize = _defaultFontSize;
            FontStyleFlags = _defaultFontStyleFlags;
            TextColor = _defaultTextColor;
            BackColor = _defaultBackColor;
            TextAlign = _defaultTextAlign;
            _typeface = AnnotationFontResolver.Resolve(FontFamily, ToSkFontStyle(FontStyleFlags));
            _font = new SKFont(_typeface, FontSize);
            FitBoxToTextAtCenter();
        }

        private TextAnnotationElement(
            Point location,
            Size size,
            string text,
            string fontFamily,
            float fontSize,
            int fontStyleFlags,
            SKColor textColor,
            SKColor backColor,
            int textAlign)
            : base(location, size.Width, size.Height) {
            Text = text;
            FontFamily = fontFamily;
            FontSize = fontSize;
            FontStyleFlags = fontStyleFlags;
            TextColor = textColor;
            BackColor = backColor;
            TextAlign = textAlign;
            _typeface = AnnotationFontResolver.Resolve(fontFamily, ToSkFontStyle(fontStyleFlags));
            _font = new SKFont(_typeface, FontSize);
        }

        private static SKFontStyle ToSkFontStyle(int gdiFontStyleFlags) {
            bool bold = (gdiFontStyleFlags & 1) != 0;
            bool italic = (gdiFontStyleFlags & 2) != 0;
            return new SKFontStyle(
                bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        }

        //Rebuilds the cached typeface after FontFamily/FontStyleFlags changes - callers (the properties
        //dialog's live-preview handlers, SetFontSizeInPoints) call this explicitly, matching upstream's own
        //explicit RebuildGdiObjects() calls rather than an auto-rebuilding property setter.
        public void RebuildGdiObjects() {
            SKTypeface previousTypeface = _typeface;
            SKFont previousFont = _font;
            _typeface = AnnotationFontResolver.Resolve(FontFamily, ToSkFontStyle(FontStyleFlags));
            _font = new SKFont(_typeface, FontSize);
            previousFont.Dispose();
            previousTypeface.Dispose();
        }

        //Measures text and sets Width/Height around the unchanged center (X, Y).
        public void FitBoxToTextAtCenter() {
            Size box = TextAnnotationLayout.MeasureBoxForText(Text, _typeface, FontSize);
            Width = box.Width;
            Height = box.Height;
        }

        public void SetFontSizeInPoints(float sizeInPoints) {
            sizeInPoints = Math.Clamp(sizeInPoints, TextAnnotationLayout.MinFontSizePt, TextAnnotationLayout.MaxFontSizePt);
            if (TextAnnotationLayout.NearlyEqualFontSize(FontSize, sizeInPoints))
                return;

            FontSize = sizeInPoints;
            RebuildGdiObjects();
        }

        protected override void OnAfterMouseDown() {
            if (IsResizing)
                _resizeStartFontSize = FontSize;
        }

        //Ports OnResized (reference §6): rescales font size to track the resize handle's area change, unlike
        //ShapeAnnotationElement which just stretches.
        protected override void OnResized() {
            float newSize = TextAnnotationLayout.ComputeResizeFontSize(_resizeStartFontSize, ResizeStartWidth, ResizeStartHeight, Width, Height);
            SetFontSizeInPoints(newSize);
        }

        public override bool ContainsPoint(Point graphPoint) =>
            Visible && (base.ContainsPoint(graphPoint) || Bounds.Contains(GraphToLocal(graphPoint)));

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            Rectangle bounds = GetGraphRect();
            DrawSelectionHighlight(canvas, bounds);

            if (BackColor.Alpha > 0) {
                _backPaint.Color = BackColor;
                canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), _backPaint);
            }

            if (!string.IsNullOrEmpty(Text))
                DrawAnnotationText(canvas, bounds);

            DrawResizeHandles(canvas);
        }

        private void DrawAnnotationText(SKCanvas canvas, Rectangle bounds) {
            _textPaint.Color = TextColor;

            SKTextAlign skAlign = TextAlign switch {
                0 => SKTextAlign.Left,
                2 => SKTextAlign.Right,
                _ => SKTextAlign.Center
            };
            float x = TextAlign switch {
                0 => bounds.Left,
                2 => bounds.Right,
                _ => bounds.Left + bounds.Width / 2f
            };

            string[] lines = Text.Split('\n');
            SKFontMetrics metrics = _font.Metrics;
            float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
            float totalHeight = lineHeight * lines.Length;
            float y = bounds.Top + (bounds.Height - totalHeight) / 2 - metrics.Ascent;

            foreach (string line in lines) {
                canvas.DrawText(line, x, y, skAlign, _font, _textPaint);
                y += lineHeight;
            }
        }

        public override AnnotationSaveData ToSaveData() => new TextAnnotationSaveData {
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Text = Text,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontStyle = FontStyleFlags,
            TextColor = ColorToSave(TextColor),
            BackColor = ColorToSave(BackColor),
            TextAlign = TextAlign
        };

        public override void Dispose() {
            _font.Dispose();
            _typeface.Dispose();
            _backPaint.Dispose();
            _textPaint.Dispose();
            base.Dispose();
        }

        public static TextAnnotationElement FromSaveData(TextAnnotationSaveData data) {
            int align = data.TextAlign is >= 0 and <= 2 ? data.TextAlign : 1;
            return new TextAnnotationElement(
                new Point(data.X, data.Y),
                new Size(data.Width, data.Height),
                data.Text,
                data.FontFamily,
                data.FontSize,
                data.FontStyle,
                ColorFromSave(data.TextColor),
                ColorFromSave(data.BackColor),
                align);
        }
    }
}
