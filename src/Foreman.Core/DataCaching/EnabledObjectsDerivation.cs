using Foreman.DataCaching.DataTypes;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.DataCaching {
    //Shared by SaveFileLoadWindow.ProcessSaveDataAsync (io-reference.md §5) and, once built,
    //SciencePacksWindow's confirm handler (§6) - upstream implements this identical
    //assembler/beacon/module transitive-enable loop twice (SavefileLoadForm.cs:244-269 vs
    //SciencePacksLoadForm.cs:132-157). Factored here once so the second dialog reuses it.
    internal static class EnabledObjectsDerivation {
        public static void ResetToPlayerAssembler(DataCache cache, HashSet<IDataObjectBase> enabledObjects) {
            enabledObjects.Clear();
            if (cache.PlayerAssembler is not null)
                enabledObjects.Add(cache.PlayerAssembler);
        }

        public static void DeriveAssemblersBeaconsModules(DataCache cache, HashSet<IDataObjectBase> enabledObjects) {
            foreach (IAssembler assembler in cache.Assemblers.Values) {
                bool enabled = false;
                foreach (IReadOnlyCollection<IRecipe> recipes in assembler.AssociatedItems.Select(item => item.ProductionRecipes))
                    foreach (IRecipe recipe in recipes)
                        enabled |= enabledObjects.Contains(recipe);
                if (enabled)
                    enabledObjects.Add(assembler);
            }

            foreach (IBeacon beacon in cache.Beacons.Values) {
                bool enabled = false;
                foreach (IReadOnlyCollection<IRecipe> recipes in beacon.AssociatedItems.Select(item => item.ProductionRecipes))
                    foreach (IRecipe recipe in recipes)
                        enabled |= enabledObjects.Contains(recipe);
                if (enabled)
                    enabledObjects.Add(beacon);
            }

            foreach (IModule module in cache.Modules.Values) {
                bool enabled = false;
                foreach (IRecipe recipe in module.AssociatedItem.ProductionRecipes)
                    enabled |= enabledObjects.Contains(recipe);
                if (enabled)
                    enabledObjects.Add(module);
            }
        }
    }
}
