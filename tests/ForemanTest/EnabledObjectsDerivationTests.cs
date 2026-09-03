using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ForemanTest {
    //Covers the assembler/beacon/module transitive-enable derivation shared by SaveFileLoadWindow's
    //ProcessSaveData (io-reference.md §5) and SciencePacksWindow's confirm handler (§6, not yet built) -
    //upstream implements this identical loop twice (SavefileLoadForm.cs:244-269 vs
    //SciencePacksLoadForm.cs:132-157); this helper is the single shared copy.
    [TestClass]
    public class EnabledObjectsDerivationTests {
        private static Task<DataCache> GetCacheAsync() => VanillaDataCacheFixture.GetLoadedAsync();

        [TestMethod]
        public async Task ResetToPlayerAssembler_ClearsExistingMembershipAndAddsOnlyThePlayerAssembler() {
            DataCache cache = await GetCacheAsync();
            var enabledObjects = new HashSet<IDataObjectBase> { cache.Recipes.Values.First(), cache.Qualities.Values.First() };

            EnabledObjectsDerivation.ResetToPlayerAssembler(cache, enabledObjects);

            var expected = new HashSet<IDataObjectBase>();
            if (cache.PlayerAssembler is not null)
                expected.Add(cache.PlayerAssembler);
            Assert.IsTrue(enabledObjects.SetEquals(expected));
        }

        [TestMethod]
        public async Task DeriveAssemblersBeaconsModules_EnablesOnlyAssemblersThatCanProduceAnEnabledRecipe() {
            DataCache cache = await GetCacheAsync();
            IRecipe recipe = cache.Recipes.Values.First(r =>
                cache.Assemblers.Values.Any(a => a.AssociatedItems.Any(i => i.ProductionRecipes.Contains(r))));
            IAssembler matchingAssembler = cache.Assemblers.Values.First(a =>
                a.AssociatedItems.Any(i => i.ProductionRecipes.Contains(recipe)));
            IAssembler? nonMatchingAssembler = cache.Assemblers.Values.FirstOrDefault(a =>
                !a.AssociatedItems.Any(i => i.ProductionRecipes.Contains(recipe)));
            var enabledObjects = new HashSet<IDataObjectBase> { recipe };

            EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(cache, enabledObjects);

            Assert.IsTrue(enabledObjects.Contains(matchingAssembler));
            if (nonMatchingAssembler is not null)
                Assert.IsFalse(enabledObjects.Contains(nonMatchingAssembler));
        }

        [TestMethod]
        public async Task DeriveAssemblersBeaconsModules_EnablesOnlyModulesWhoseAssociatedItemHasAnEnabledProductionRecipe() {
            DataCache cache = await GetCacheAsync();
            IModule module = cache.Modules.Values.First(m => m.AssociatedItem.ProductionRecipes.Count > 0);
            IRecipe recipe = module.AssociatedItem.ProductionRecipes.First();
            IModule? otherModule = cache.Modules.Values.FirstOrDefault(m => m != module && !m.AssociatedItem.ProductionRecipes.Contains(recipe));
            var enabledObjects = new HashSet<IDataObjectBase> { recipe };

            EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(cache, enabledObjects);

            Assert.IsTrue(enabledObjects.Contains(module));
            if (otherModule is not null)
                Assert.IsFalse(enabledObjects.Contains(otherModule));
        }

        [TestMethod]
        public async Task DeriveAssemblersBeaconsModules_LeavesEverythingDisabledWhenNoRecipeIsEnabled() {
            DataCache cache = await GetCacheAsync();
            var enabledObjects = new HashSet<IDataObjectBase>();

            EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(cache, enabledObjects);

            Assert.AreEqual(0, enabledObjects.Count);
        }
    }
}
