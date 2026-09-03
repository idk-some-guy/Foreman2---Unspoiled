using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Views;
using SkiaSharp;
using System.Collections.Generic;

namespace Foreman.Mac.ViewModels {
    //Splits PresetComparatorWindow.axaml.cs's per-tab row assembly off into its own VM (Task 8):
    //ProcessMods/ProcessObjects/EvaluateSimilarity stay on the window (existing tests call them there),
    //RebuildTab is what UpdateUnfilteredRows used to do. RebuildTab always hands back fresh List instances
    //per bucket rather than clearing the previous ones in place. This window never binds to these
    //properties either; the reference-equality short-circuit lives in UpdateFilteredLists, which builds
    //brand-new filtered lists before each ItemsSource assignment (see its own comment).
    internal sealed partial class PresetComparatorViewModel : ObservableObject {
        private static readonly IBrush AvailableForeground = new ImmutableSolidColorBrush(Color.FromRgb(0, 0, 0));
        private static readonly IBrush UnavailableForeground = new ImmutableSolidColorBrush(Color.FromRgb(139, 0, 0));
        private static readonly IBrush CloseEnoughBackground = new ImmutableSolidColorBrush(Color.FromRgb(240, 230, 140));
        private static readonly IBrush DifferentBackground = new ImmutableSolidColorBrush(Color.FromRgb(255, 192, 203));

        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        [ObservableProperty]
        private IReadOnlyList<ComparatorRow> leftOnlyRows = [];

        [ObservableProperty]
        private IReadOnlyList<ComparatorRow> leftRows = [];

        [ObservableProperty]
        private IReadOnlyList<ComparatorRow> rightRows = [];

        [ObservableProperty]
        private IReadOnlyList<ComparatorRow> rightOnlyRows = [];

        //Ports UpdateUnfilteredRows (reference PresetComparatorWindow.axaml.cs, pre-Task 8): rebuilds all
        //4 row lists for the given tab, then colors the matched Left/Right pair.
        public void RebuildTab(int tabIndex, List<object>[] tabObjects) {
            List<ComparatorRow> left = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.Left]);
            List<ComparatorRow> right = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.Right]);
            ColorMatchedPairs(tabIndex, left, right);

            LeftOnlyRows = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.LeftOnly]);
            LeftRows = left;
            RightRows = right;
            RightOnlyRows = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.RightOnly]);
        }

        public void Clear() {
            LeftOnlyRows = [];
            LeftRows = [];
            RightRows = [];
            RightOnlyRows = [];
        }

        private List<ComparatorRow> BuildRows(int tabIndex, List<object> objects) {
            var rows = new List<ComparatorRow>();
            foreach (object obj in objects)
                rows.Add(tabIndex == 0 ? NewModRow((string)obj) : NewObjectRow((IDataObjectBase)obj));
            return rows;
        }

        private static void ColorMatchedPairs(int tabIndex, List<ComparatorRow> left, List<ComparatorRow> right) {
            for (int i = 0; i < left.Count; i++) {
                ComparatorRow l = left[i];
                ComparatorRow r = right[i];
                (bool similarInternals, bool similarNames) = PresetComparatorWindow.EvaluateSimilarity(tabIndex, l.Name, l.Tag, r.Name, r.Tag);

                PresetComparatorWindow.RowSimilarity similarity = similarInternals
                    ? (similarNames ? PresetComparatorWindow.RowSimilarity.Equal : PresetComparatorWindow.RowSimilarity.CloseEnough)
                    : PresetComparatorWindow.RowSimilarity.Different;
                IBrush background = similarity switch {
                    PresetComparatorWindow.RowSimilarity.Equal => PresetComparatorWindow.EqualBackground,
                    PresetComparatorWindow.RowSimilarity.CloseEnough => CloseEnoughBackground,
                    _ => DifferentBackground,
                };
                l.Similarity = similarity;
                l.Background = background;
                r.Similarity = similarity;
                r.Background = background;
            }
        }

        //Mod rows have no Available concept - unconditionally regular/black, matching upstream's mod
        //branch. FilterKey intentionally keeps the raw (unlowered) text here too, mirroring upstream's
        //lvItem.Name = lvItem.Text for mods (as opposed to doBase.Name.ToLowerInvariant() below).
        private static ComparatorRow NewModRow(string text) =>
            new(text, text, text, null, AvailableForeground, FontStyle.Normal);

        private ComparatorRow NewObjectRow(IDataObjectBase dataObject) =>
            new(dataObject, dataObject.FriendlyName, dataObject.Name.ToLowerInvariant(), GetOrBakeIcon(dataObject.Icon),
                dataObject.Available ? AvailableForeground : UnavailableForeground,
                dataObject.Available ? FontStyle.Normal : FontStyle.Italic);

        private Bitmap GetOrBakeIcon(SKBitmap icon) {
            if (!bakedIconCache.TryGetValue(icon, out Bitmap? baked)) {
                baked = BakeIcon(icon);
                bakedIconCache[icon] = baked;
            }
            return baked;
        }

        //Matches upstream's comparator-specific IconList.ImageSize of 32x32 (its other lists elsewhere
        //in the app use 24x24 - this form is the one exception, ported as-is).
        private static Bitmap BakeIcon(SKBitmap icon) {
            const int size = 32;
            using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var paint = new SKPaint { IsAntialias = true };
            surface.Canvas.DrawBitmap(icon, new SKRect(0, 0, size, size), paint);
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }
    }
}
