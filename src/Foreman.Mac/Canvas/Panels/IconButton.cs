using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Foreman.DataCaching.DataTypes;
using SkiaSharp;
using System;

namespace Foreman.Mac.Canvas.Panels {
    //Ports NFButton (Controls/IRChooserPanel.cs), shared by grid cells and group buttons upstream too:
    //background-color-coded fill (docs/panels-reference.md §2's 4-color scheme) plus NFButton.OnEnabledChanged's
    //luminance-weighted grayscale-on-disable (alpha x0.4). Upstream caches a converted grayscale Bitmap and
    //swaps BackgroundImage on Enabled changes; we hold Icon as pure data and apply the equivalent SKColorFilter
    //at paint time instead, since there is no BackgroundImage to mutate.
    //
    //Live bug (found driving the real app): opening "Add Item"/"Add Recipe" turned the whole window gray,
    //toolbar included, with no crash and no logged exception. Root cause, confirmed by bisecting the real app:
    //a populated IconButton used to paint itself through its own ICustomDrawOperation (a raw
    //ISkiaSharpApiLeaseFeature GPU lease), and on macOS's Metal-backed Avalonia.Skia renderer, any such lease
    //taken from a nested composited control (as opposed to GraphCanvasControl's own single top-level one)
    //corrupts the whole window's next composited frame the moment it draws a bitmap - consolidating every
    //button into one shared lease (tried first) still broke it, since it was still a second, nested lease.
    //Baking each button's pixels into a real Avalonia Bitmap and drawing that through DrawingContext
    //sidesteps the custom-draw-operation/GPU-lease path entirely, the same ordinary image-compositing route
    //every other Avalonia control's own bitmaps already render through live.
    public sealed class IconButton : Control, IDisposable {
        private static readonly SKColorFilter GrayscaleFilter = SKColorFilter.CreateColorMatrix([
            .2126f, .7152f, .0722f, 0, 0,
            .2126f, .7152f, .0722f, 0, 0,
            .2126f, .7152f, .0722f, 0, 0,
            0, 0, 0, 0.4f, 0,
        ]);

        public static readonly Color EmptyFillColor = Color.FromRgb(105, 105, 105);

        public event EventHandler<PointerReleasedEventArgs>? Released;

        public IDataObjectBase? DataObject { get; private set; }
        public SKBitmap? Icon { get; private set; }
        public Color FillColor { get; private set; } = EmptyFillColor;
        public bool DrawBorderWhenEmpty { get; set; } = true;

        private Bitmap? liveBitmap;
        private bool liveBitmapDirty = true;
        private int liveBitmapRebuildCount;

        //Increments every time Render() actually re-bakes the composited frame; internal for tests
        //verifying the cache invalidates on state changes, same rationale as Bake() below.
        internal int LiveBitmapRebuildCount => liveBitmapRebuildCount;

        public IconButton() => DetachedFromVisualTree += (_, _) => Dispose();

        public void Dispose() {
            liveBitmap?.Dispose();
            liveBitmap = null;
            liveBitmapDirty = true;
        }

        public void SetEmpty() {
            DataObject = null;
            Icon = null;
            FillColor = EmptyFillColor;
            IsEnabled = false;
            ToolTip.SetTip(this, null);
            liveBitmapDirty = true;
            InvalidateVisual();
        }

        public void SetPopulated(IDataObjectBase populated, Color fillColor) => SetPopulated(populated, populated.Icon, fillColor);

        //Overload for EditRecipePanel's per-quality buttons (docs/panels-reference.md §3's InitializeBaseButton):
        //the grid cell's icon there is quality-combined (AssemblerQualityPair.Icon and friends), not the plain
        //data-object icon SetPopulated(IDataObjectBase, Color) above assumes.
        public void SetPopulated(IDataObjectBase populated, SKBitmap? icon, Color fillColor) {
            DataObject = populated;
            Icon = icon;
            FillColor = fillColor;
            IsEnabled = true;
            ToolTip.SetTip(this, string.IsNullOrEmpty(populated.FriendlyName) ? "-" : populated.FriendlyName);
            liveBitmapDirty = true;
            InvalidateVisual();
        }

        public void SetFillColor(Color fillColor) {
            FillColor = fillColor;
            liveBitmapDirty = true;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            Released?.Invoke(this, e);
            e.Handled = true;
        }

        //Mirrors upstream NFButton.OnEnabledChanged (see class comment above): callers outside this class
        //toggle IsEnabled directly (EditRecipePanel's UpdateAssemblerModules/UpdateBeaconModules gray out
        //option cells once slots fill), bypassing SetPopulated/SetFillColor/SetEmpty. Without this override
        //the grayscale filter's cached bitmap never invalidates, so a cell that goes disabled then re-enabled
        //stays stuck painting whatever it last baked.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == IsEnabledProperty) {
                liveBitmapDirty = true;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            base.Render(context);

            int w = Math.Max(1, (int)Math.Ceiling(Bounds.Width));
            int h = Math.Max(1, (int)Math.Ceiling(Bounds.Height));
            if (liveBitmapDirty || liveBitmap is null || liveBitmap.PixelSize.Width != w || liveBitmap.PixelSize.Height != h)
                RebuildLiveBitmap(w, h);

            if (liveBitmap is Bitmap bmp)
                context.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), new Rect(Bounds.Size));
        }

        private void RebuildLiveBitmap(int w, int h) {
            liveBitmap?.Dispose();
            liveBitmap = Bake(w, h);
            liveBitmapDirty = false;
            liveBitmapRebuildCount++;
        }

        //Pure - doesn't touch liveBitmap - so tests can verify this independently of Render's own caching/
        //dirty-tracking. Copies pixels straight out of the raster surface via PeekPixels into Bitmap's
        //raw-pointer constructor instead of round-tripping through a PNG encode/decode: the surface is
        //already Bgra8888/Premul, the exact format/alpha pair that constructor takes, so the copy is a
        //straight memcpy with no per-pixel conversion. WriteableBitmap was rejected for this the same reason
        //it was rejected before - its actual backing pixel format isn't guaranteed to match whatever
        //PixelFormat its own constructor was asked for on every Avalonia backend.
        internal Bitmap Bake(int w, int h) {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            PaintOnto(surface.Canvas, new SKRect(0, 0, w, h));
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }

        //Shared by the live Bake above and IRChooserPanel.RenderOffscreen's plain SKCanvas walk
        //(docs/panels-reference.md §9 step 2's PNG deliverable), which paints grid/group cells directly
        //from their known Canvas positions instead of going through a full Avalonia render pass.
        internal void PaintOnto(SKCanvas canvas, SKRect bounds) {
            using var fillPaint = new SKPaint { Color = ToSkColor(FillColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(bounds, fillPaint);

            if (Icon is SKBitmap bmp) {
                using var iconPaint = new SKPaint { IsAntialias = true };
                if (!IsEnabled)
                    iconPaint.ColorFilter = GrayscaleFilter;
                canvas.DrawBitmap(bmp, bounds, iconPaint);
            } else if (DrawBorderWhenEmpty) {
                using var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
                canvas.DrawRect(SKRect.Inflate(bounds, -0.5f, -0.5f), borderPaint);
            }
        }

        private static SKColor ToSkColor(Color c) => new(c.R, c.G, c.B, c.A);
    }
}
