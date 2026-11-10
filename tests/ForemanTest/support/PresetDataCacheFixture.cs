using Foreman;
using Foreman.DataCaching;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ForemanTest.support {
    /// <summary>Loads each bundled preset once per test run (expensive).</summary>
    internal static class PresetDataCacheFixture {
        public const string Vanilla21 = "Factorio 2.1 Vanilla", SpaceAge21 = "Factorio 2.1 Space Age";

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly ConcurrentDictionary<string, DataCache> caches = [];

        public static string PresetsDirectory =>
            Path.Combine(AppContext.BaseDirectory, "Presets");

        public static async Task<DataCache> GetLoadedAsync(string presetName, bool filterRecipes = true) {
            if (caches.TryGetValue(presetName, out DataCache? ready) && ready.PresetName == presetName)
                return ready;

            await Gate.WaitAsync().ConfigureAwait(false);
            try {
                if (!caches.TryGetValue(presetName, out DataCache? cache) || cache.PresetName != presetName) {
                    var next = new DataCache(filterRecipes);
                    await next.LoadAllData(
                        new Preset(presetName, true, true),
                        NullProgress.Instance,
                        loadIcons: false).ConfigureAwait(false);
                    caches[presetName] = cache = next;
                }
                return cache;
            } finally {
                Gate.Release();
            }
        }

        public static void Reset(string presetName) {
            Gate.Wait();
            try {
                caches.TryRemove(presetName, out _);
            } finally {
                Gate.Release();
            }
        }
    }
}
