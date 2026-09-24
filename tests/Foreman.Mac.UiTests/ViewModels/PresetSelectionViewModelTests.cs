using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.ViewModels;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    //Covers PresetSelectionViewModel: Load ranks the packages by overall match and builds the rows through
    //PresetSelectionSheet.BuildRows; Selected mirrors the sheet's list selection.
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

        private static PresetErrorPackage Package(string name, int requiredMods, int missingMods = 0, int wrongVersionMods = 0,
                int requiredItems = 1, int missingItems = 0, int requiredRecipes = 1, int missingRecipes = 0) {
            var errors = new PresetErrorPackage(new Preset(name, false, false));
            for (int i = 0; i < requiredMods; i++)
                errors.RequiredMods.Add("mod" + i + "|1.0.0");
            for (int i = 0; i < missingMods; i++)
                errors.MissingMods.Add("mod" + i + "|1.0.0");
            for (int i = 0; i < wrongVersionMods; i++)
                errors.WrongVersionMods.Add("mod" + (requiredMods - 1 - i) + "|1.0.0|2.0.0");
            for (int i = 0; i < requiredItems; i++)
                errors.RequiredItems.Add("item" + i);
            for (int i = 0; i < missingItems; i++)
                errors.MissingItems.Add("item" + i);
            for (int i = 0; i < requiredRecipes; i++)
                errors.RequiredRecipes.Add("recipe" + i);
            for (int i = 0; i < missingRecipes; i++)
                errors.MissingRecipes.Add("recipe" + i);
            return errors;
        }

        [Fact]
        public void Load_RanksByModsThenItemsThenRecipesPercent_Descending() {
            var vm = new PresetSelectionViewModel();

            //PresetErrorPackage.CompareTo counts missing mods first, so it would put B (0 missing, 1 wrong version, 50 %) above A (90 %).
            vm.Load([
                Package("B", requiredMods: 2, wrongVersionMods: 1),
                Package("A", requiredMods: 10, missingMods: 1),
                Package("C recipes", requiredMods: 1, requiredItems: 4, missingItems: 1, requiredRecipes: 4, missingRecipes: 2),
                Package("C items", requiredMods: 1, requiredItems: 4, missingItems: 2),
                Package("C best", requiredMods: 1, requiredItems: 4, missingItems: 1, requiredRecipes: 4, missingRecipes: 1),
            ]);

            Assert.Equal(["C best", "C recipes", "C items", "A", "B"], vm.Presets.Select(r => r.Name));
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
