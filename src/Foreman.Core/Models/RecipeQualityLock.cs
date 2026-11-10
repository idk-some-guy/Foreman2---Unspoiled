using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using System.Linq;

namespace Foreman.Models {
    public static class RecipeQualityLock {
        public static IQuality LowestEnabled(DataCache cache) =>
            cache.AvailableQualities.Where(q => q.Enabled).MinBy(q => q.Level) ?? cache.DefaultQuality!;

        public static IQuality PlacementQuality(IRecipe recipe, IQuality requested, DataCache cache) => recipe.CanSetQuality ? requested : LowestEnabled(cache);
    }
}
