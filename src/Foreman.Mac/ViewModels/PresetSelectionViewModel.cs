using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching;
using Foreman.Mac.Views.Sheets;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    internal sealed partial class PresetSelectionViewModel : ObservableObject {
        public IReadOnlyList<PresetRow> Presets { get; private set; } = [];

        [ObservableProperty]
        private PresetRow? selected;

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
