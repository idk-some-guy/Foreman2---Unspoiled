using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching;
using Foreman.Mac.Views;
using System.Collections.Generic;

namespace Foreman.Mac.ViewModels {
    //Splits PresetSelectionWindow.axaml.cs's sort + row-assembly off into its own VM (Task 8):
    //BuildRows stays on the window (existing tests call it there); Selected mirrors the ListBox's
    //current selection, set by the window's SelectionChanged handler and read back by Commit.
    internal sealed partial class PresetSelectionViewModel : ObservableObject {
        public IReadOnlyList<PresetRow> Presets { get; private set; } = [];

        [ObservableProperty]
        private PresetRow? selected;

        //Ports the constructor's sort call (reference PresetSelectionForm.cs:14-53): List<T>.Sort calls
        //PresetErrorPackage.CompareTo, so the closest-matching preset lands first.
        public void Load(List<PresetErrorPackage> presetErrors) {
            presetErrors.Sort();
            Presets = PresetSelectionWindow.BuildRows(presetErrors);
        }
    }
}
