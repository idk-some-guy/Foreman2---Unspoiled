using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching;
using Foreman.Mac.Views.Sheets;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    //The sheet's ranking and rows: BuildRows stays on the sheet; Selected mirrors the ListBox's current
    //selection, set by the sheet's SelectionChanged handler and read back by CommitAsync.
    internal sealed partial class PresetSelectionViewModel : ObservableObject {
        public IReadOnlyList<PresetRow> Presets { get; private set; } = [];

        [ObservableProperty]
        private PresetRow? selected;

        //Descending by overall match: mods, then items, then recipes (copy page §01); OrderBy keeps ties in input order.
        public void Load(List<PresetErrorPackage> presetErrors) {
            List<PresetErrorPackage> ranked = [.. presetErrors
                .Select(p => (Package: p, Match: PresetSelectionSheet.Compatibility(p)))
                .OrderByDescending(e => e.Match.Mods)
                .ThenByDescending(e => e.Match.Items)
                .ThenByDescending(e => e.Match.Recipes)
                .Select(e => e.Package)];
            Presets = PresetSelectionSheet.BuildRows(ranked);
        }
    }
}
