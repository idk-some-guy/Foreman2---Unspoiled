using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Models {
    public static class PlantTowerRules {
        public static bool IsChoosableTower(IAssembler a) => a.EntityType == EntityType.AgriculturalTower && a.ModuleSlots > 0;

        public static bool IsAssemblerListEntry(IAssembler a) => a.EntityType == EntityType.Assembler || IsChoosableTower(a);

        public static IReadOnlyList<IAssembler> TowerOptions(DataCache cache) =>
            [.. cache.Assemblers.Values.Where(a => IsChoosableTower(a) && a.Enabled).OrderBy(a => a.LFriendlyName)];

        public static IReadOnlyList<IModule> ModuleOptions(IAssembler tower, IQuality quality) =>
            [.. tower.Modules.Where(m => m.Enabled && m.GetProductivityBonus(quality) != 0).OrderBy(m => m.LFriendlyName)];

        public static bool HasHiddenModules(IAssembler tower, IQuality quality) {
            IReadOnlyList<IModule> options = ModuleOptions(tower, quality);
            return tower.Modules.Any(m => m.Enabled && !options.Contains(m));
        }
    }
}
