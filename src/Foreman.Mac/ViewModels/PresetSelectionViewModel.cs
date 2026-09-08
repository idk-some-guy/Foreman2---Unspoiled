using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching;
using Foreman.Mac.Views.Sheets;
using System.Collections.Generic;

namespace Foreman.Mac.ViewModels {
    //Splits PresetSelectionSheet.axaml.cs's sort + row-assembly off into its own VM (Task 8):
    //BuildRows stays on the sheet (existing tests call it there); Selected mirrors the ListBox's
    //current selection, set by the sheet's SelectionChanged handler and read back by Commit.
    internal sealed partial class PresetSelectionViewModel : ObservableObject {
        public IReadOnlyList<PresetRow> Presets { get; private set; } = [];

        [ObservableProperty]
        private PresetRow? selected;

        //Ports the constructor's sort call (reference PresetSelectionForm.cs:14-53): List<T>.Sort calls
        //PresetErrorPackage.CompareTo, so the closest-matching preset lands first.
        public void Load(List<PresetErrorPackage> presetErrors) {
            presetErrors.Sort();
            Presets = PresetSelectionSheet.BuildRows(presetErrors);
        }
    }
}
