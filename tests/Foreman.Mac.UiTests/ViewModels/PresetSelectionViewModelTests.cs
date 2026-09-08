using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.ViewModels;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    //Covers PresetSelectionViewModel's split off PresetSelectionSheet.axaml.cs (Task 8): Load reuses
    //PresetSelectionSheet.BuildRows (existing tests call that directly, unchanged) and Selected is the
    //VM's own bindable state, mirroring the sheet's own control selection.
    public class PresetSelectionViewModelTests {
        private static PresetErrorPackage NewPackage(Preset preset, int missingMods) {
            var errors = new PresetErrorPackage(preset);
            errors.RequiredMods.Add("mod0");
            for (int i = 0; i < missingMods; i++)
                errors.MissingMods.Add("missing-mod" + i);
            errors.RequiredItems.Add("item0");
            errors.RequiredRecipes.Add("recipe0");
            return errors;
        }

        [Fact]
        public void Load_SortsAndBuildsPresetsFromErrorPackages() {
            var worst = new Preset("Worst", false, false);
            var best = new Preset("Best", false, false);
            var errors = new List<PresetErrorPackage> { NewPackage(worst, missingMods: 1), NewPackage(best, missingMods: 0) };
            var vm = new PresetSelectionViewModel();

            vm.Load(errors);

            Assert.Equal(["Best", "Worst"], vm.Presets.Select(r => r.Name));
        }

        [Fact]
        public void Selected_DefaultsNull_ThenReflectsAssignedRow() {
            var preset = new Preset("P", false, false);
            var errors = new List<PresetErrorPackage> { NewPackage(preset, missingMods: 0) };
            var vm = new PresetSelectionViewModel();
            vm.Load(errors);

            Assert.Null(vm.Selected);

            vm.Selected = vm.Presets[0];

            Assert.Equal(preset, vm.Selected!.Preset);
        }
    }
}
