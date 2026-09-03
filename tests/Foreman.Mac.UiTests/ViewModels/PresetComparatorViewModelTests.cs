using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    //Covers PresetComparatorViewModel's row-assembly split off PresetComparatorWindow.axaml.cs (Task 8):
    //ProcessMods/ProcessObjects/EvaluateSimilarity stay on the window (existing tests call them there
    //unchanged) and feed straight into RebuildTab here, matching how the window itself will wire it.
    public class PresetComparatorViewModelTests {
        private static DataCache NewEmptyCache() => new(filterRecipes: false);

        private static SubgroupPrototype NewSubgroup(DataCache cache) {
            var group = new GroupPrototype(cache, "g", "G", "a");
            var subgroup = new SubgroupPrototype(cache, "sg", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            return subgroup;
        }

        private static List<object>[] NewEmptyTabObjects() => [[], [], [], []];

        [Fact]
        public void RebuildTab_Mods_PopulatesFourBucketsFromProcessedMods() {
            var leftMods = new Dictionary<string, string> { ["base"] = "1.0", ["shared"] = "2.0" };
            var rightMods = new Dictionary<string, string> { ["shared"] = "2.0", ["extra"] = "3.0" };
            List<object>[] modsTab = NewEmptyTabObjects();
            PresetComparatorWindow.ProcessMods(leftMods, rightMods, modsTab);
            var vm = new PresetComparatorViewModel();

            vm.RebuildTab(0, modsTab);

            Assert.Equal("base_1.0", Assert.Single(vm.LeftOnlyRows).Name);
            Assert.Equal("extra_3.0", Assert.Single(vm.RightOnlyRows).Name);
            Assert.Equal("shared_2.0", Assert.Single(vm.LeftRows).Name);
            Assert.Equal("shared_2.0", Assert.Single(vm.RightRows).Name);
            Assert.Equal(PresetComparatorWindow.RowSimilarity.Equal, vm.LeftRows[0].Similarity);
        }

        [AvaloniaFact]
        public void RebuildTab_Items_TagsRowsWithSourceObjectAndColorsMatchedPairBySimilarity() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype leftOnly = new(cache, "left-only", "Left Only", subgroup, "a") { Available = true };
            ItemPrototype matchedLeft = new(cache, "shared", "Shared", subgroup, "a") { Available = true };
            ItemPrototype matchedRight = new(cache, "shared", "Shared", subgroup, "a") { Available = true };
            ItemPrototype rightOnly = new(cache, "right-only", "Right Only", subgroup, "a") { Available = false };

            var leftDict = new Dictionary<string, IItem> { ["left-only"] = leftOnly, ["shared"] = matchedLeft };
            var rightDict = new Dictionary<string, IItem> { ["shared"] = matchedRight, ["right-only"] = rightOnly };
            List<object>[] itemsTab = NewEmptyTabObjects();
            PresetComparatorWindow.ProcessObjects(leftDict, rightDict, itemsTab);
            var vm = new PresetComparatorViewModel();

            vm.RebuildTab(1, itemsTab);

            Assert.Same(leftOnly, Assert.Single(vm.LeftOnlyRows).Tag);
            Assert.Same(rightOnly, Assert.Single(vm.RightOnlyRows).Tag);
            Assert.Equal(PresetComparatorWindow.RowSimilarity.Equal, Assert.Single(vm.LeftRows).Similarity);
            Assert.Equal(FontStyle.Italic, Assert.Single(vm.RightOnlyRows).FontStyle);
        }

        [AvaloniaFact]
        public void RebuildTab_DifferentTabsProduceDifferentCategoryContent() {
            var leftMods = new Dictionary<string, string> { ["only-mod"] = "1.0" };
            List<object>[] modsTab = NewEmptyTabObjects();
            PresetComparatorWindow.ProcessMods(leftMods, new Dictionary<string, string>(), modsTab);

            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype onlyItem = new(cache, "only-item", "Only Item", subgroup, "a") { Available = true };
            var leftDict = new Dictionary<string, IItem> { ["only-item"] = onlyItem };
            List<object>[] itemsTab = NewEmptyTabObjects();
            PresetComparatorWindow.ProcessObjects(leftDict, new Dictionary<string, IItem>(), itemsTab);

            var vm = new PresetComparatorViewModel();
            vm.RebuildTab(0, modsTab);
            string modsRowName = Assert.Single(vm.LeftOnlyRows).Name;

            vm.RebuildTab(1, itemsTab);
            string itemsRowName = Assert.Single(vm.LeftOnlyRows).Name;

            Assert.Equal("only-mod_1.0", modsRowName);
            Assert.Equal("Only Item", itemsRowName);
        }

        //Regression net for the same reference-equality bug PresetComparatorWindowTests.
        //RealClickOnTabHeader_SwitchesSelectionAndRepaintsRealizedRows guards at the UI layer: every
        //RebuildTab call must hand back brand-new list instances so a bound ListBox always sees the
        //change, even across two calls with identical input.
        [Fact]
        public void RebuildTab_CalledTwice_ProducesFreshListInstancesEachTime() {
            var leftMods = new Dictionary<string, string> { ["m"] = "1.0" };
            List<object>[] modsTab = NewEmptyTabObjects();
            PresetComparatorWindow.ProcessMods(leftMods, new Dictionary<string, string>(), modsTab);
            var vm = new PresetComparatorViewModel();

            vm.RebuildTab(0, modsTab);
            IReadOnlyList<ComparatorRow> firstLeftOnly = vm.LeftOnlyRows;

            vm.RebuildTab(0, modsTab);
            IReadOnlyList<ComparatorRow> secondLeftOnly = vm.LeftOnlyRows;

            Assert.NotSame(firstLeftOnly, secondLeftOnly);
            Assert.Equal(firstLeftOnly.Count, secondLeftOnly.Count);
        }
    }
}
