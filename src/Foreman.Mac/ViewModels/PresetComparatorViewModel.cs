using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Views;
using SkiaSharp;
using System.Collections.Generic;

namespace Foreman.Mac.ViewModels {
    internal sealed partial class PresetComparatorViewModel : ObservableObject {
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        [ObservableProperty]
        private IReadOnlyList<ComparatorRow> leftOnlyRows = [];

        [ObservableProperty]
        private IReadOnlyList<ComparatorPairRow> bothRows = [];

        [ObservableProperty]
        private IReadOnlyList<ComparatorRow> rightOnlyRows = [];

        public void RebuildTab(int tabIndex, List<object>[] tabObjects) {
            List<ComparatorRow> left = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.Left]);
            List<ComparatorRow> right = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.Right]);
            var pairs = new List<ComparatorPairRow>(left.Count);
            for (int i = 0; i < left.Count; i++) {
                (bool similarInternals, bool similarNames) = PresetComparatorWindow.EvaluateSimilarity(tabIndex, left[i].Name, left[i].Tag, right[i].Name, right[i].Tag);
                PresetComparatorWindow.RowSimilarity similarity = similarInternals
                    ? (similarNames ? PresetComparatorWindow.RowSimilarity.Equal : PresetComparatorWindow.RowSimilarity.CloseEnough)
                    : PresetComparatorWindow.RowSimilarity.Different;
                pairs.Add(new ComparatorPairRow(left[i], right[i], similarity));
            }

            LeftOnlyRows = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.LeftOnly]);
            BothRows = pairs;
            RightOnlyRows = BuildRows(tabIndex, tabObjects[PresetComparatorWindow.RightOnly]);
        }

        public void Clear() {
            LeftOnlyRows = [];
            BothRows = [];
            RightOnlyRows = [];
        }

        private List<ComparatorRow> BuildRows(int tabIndex, List<object> objects) {
            var rows = new List<ComparatorRow>();
            foreach (object obj in objects)
                rows.Add(tabIndex == 0 ? NewModRow((string)obj) : NewObjectRow((IDataObjectBase)obj));
            return rows;
        }

        private static ComparatorRow NewModRow(string key) {
            int split = key.LastIndexOf('_');
            string text = split < 0 ? key : key[..split] + " " + key[(split + 1)..];
            return new(key, text, key, null, false, text);
        }

        private ComparatorRow NewObjectRow(IDataObjectBase dataObject) {
            string tooltip = $"{dataObject.FriendlyName} ({dataObject.Name})" + (dataObject.Available ? "" : " — unavailable in this preset");
            return new(dataObject, dataObject.FriendlyName, dataObject.Name.ToLowerInvariant(), IconBaker.GetOrBake(bakedIconCache, dataObject.Icon, 40),
                !dataObject.Available, tooltip);
        }
    }
}
