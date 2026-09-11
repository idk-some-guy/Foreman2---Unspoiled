using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
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
        //Resolved fresh at the top of every RebuildTab call (below) against the app's active theme variant.
        //Foreground colors stay the vivid role (text); row-background fills use the Soft variant.
        private static IBrush AvailableForeground = new ImmutableSolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
        private static IBrush UnavailableForeground = new ImmutableSolidColorBrush(Color.FromRgb(0xD7, 0x00, 0x15));
        private static IBrush CloseEnoughBackground = new ImmutableSolidColorBrush(Color.FromArgb(0x38, 0xB2, 0x50, 0x00));
        private static IBrush DifferentBackground = new ImmutableSolidColorBrush(Color.FromArgb(0x3D, 0xD7, 0x00, 0x15));

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
            AvailableForeground = ThemeResources.ResolveBrush("Foreman.Color.Success", Color.FromRgb(0x34, 0xC7, 0x59));
            UnavailableForeground = ThemeResources.ResolveBrush("Foreman.Color.Error", Color.FromRgb(0xD7, 0x00, 0x15));
            CloseEnoughBackground = ThemeResources.ResolveBrush("Foreman.Color.WarningSoft", Color.FromArgb(0x38, 0xB2, 0x50, 0x00));
            DifferentBackground = ThemeResources.ResolveBrush("Foreman.Color.ErrorSoft", Color.FromArgb(0x3D, 0xD7, 0x00, 0x15));
            PresetComparatorWindow.EqualBackground = ThemeResources.ResolveBrush("Foreman.Color.ControlBackground", Color.FromRgb(255, 255, 255));

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

        //Matches upstream's comparator-specific IconList.ImageSize of 32x32 (its other lists elsewhere
        //in the app use 24x24 - this form is the one exception, ported as-is).
        private Bitmap GetOrBakeIcon(SKBitmap icon) => IconBaker.GetOrBake(bakedIconCache, icon, 32);
    }
}
