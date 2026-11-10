using Foreman.DataCaching;

namespace Foreman.Mac.UiTests.support {
    internal static class CacheVersions {
        public static DataCache Create(int version, FactorioGameVersion? gameVersion = null, bool filterRecipes = false) {
            var cache = new DataCache(filterRecipes);
            typeof(DataCache).GetProperty(nameof(DataCache.Version))!.SetValue(cache, version);
            typeof(DataCache).GetProperty(nameof(DataCache.GameVersion))!.SetValue(cache, gameVersion);
            return cache;
        }
    }
}
