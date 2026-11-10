using Foreman.DataCaching;
using System.IO;
using System.Threading.Tasks;

namespace ForemanTest.support {
    /// <summary>Loads the vanilla preset once per test run (expensive).</summary>
    internal static class VanillaDataCacheFixture {
        public const string PresetName = "Factorio 2.0 Vanilla";

        public static string PresetsDirectory => PresetDataCacheFixture.PresetsDirectory;

        public static bool PresetsAvailable => Directory.Exists(PresetsDirectory);

        public static Task<DataCache> GetLoadedAsync(bool filterRecipes = true) =>
            PresetDataCacheFixture.GetLoadedAsync(PresetName, filterRecipes);

        public static void Reset() => PresetDataCacheFixture.Reset(PresetName);
    }
}
