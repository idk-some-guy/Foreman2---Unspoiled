using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.Loading;
using Foreman.Mac;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.UiTests.Inspector {
    //Shared MainWindow + cached vanilla DataCache spin-up for the inspector sidebar test suite - same
    //shape as MainWindowShellTests' own GetCacheAsync/ApplyLoadedSettings pattern, kept separate so this
    //file doesn't reach into that class's private members.
    internal static class InspectorTestSupport {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        internal static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        //One recipe node on an otherwise-empty graph, so tests can select it and assert on the sidebar's
        //identity row without each test re-deriving the cache/window boilerplate.
        internal static async Task<MainWindow> OpenWindowWithGraphAsync() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            //ApplyLoadedSettings only seeds DefaultAssemblerQuality when Settings is set (a full boot does
            //that); no test-only AppSettings exists here, so set it directly - same as FakeNodes' bare
            //ProductionGraphSession fixture.
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            var pair = new RecipeQualityPair(cache.Recipes["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(pair, new Point(0, 0));
            Dispatcher.UIThread.RunJobs();
            return window;
        }
    }
}
