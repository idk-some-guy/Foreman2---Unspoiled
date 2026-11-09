using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.UiTests.Inspector {
    internal static class InspectorTestSupport {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string SpaceAgePresetName = "Factorio 2.0 Space Age";
        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;
        private static DataCache? sharedSpaceAgeCache;

        internal static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            //ConfigureAwait(false) on both awaits: a sync caller blocking on this deadlocks on a cold cache otherwise.
            await CacheGate.WaitAsync().ConfigureAwait(false);
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>()).ConfigureAwait(false);
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        internal static async Task<DataCache> GetSpaceAgeCacheAsync() {
            if (sharedSpaceAgeCache is not null)
                return sharedSpaceAgeCache;
            await CacheGate.WaitAsync().ConfigureAwait(false);
            try {
                if (sharedSpaceAgeCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(SpaceAgePresetName, true, true), new Progress<KeyValuePair<int, string>>()).ConfigureAwait(false);
                    sharedSpaceAgeCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedSpaceAgeCache;
        }

        internal static async Task<MainWindow> OpenWindowWithGraphAsync() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            //ApplyLoadedSettings seeds Context.DCache and DefaultAssemblerQuality only when Settings is set.
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            var pair = new RecipeQualityPair(cache.Recipes["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(pair, new Point(0, 0));
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        internal static async Task<MainWindow> OpenEmptyWindowAsync() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        //RequestStateUpdate and PrePaint move the node to its real hit-test geometry before a click.
        internal static async Task<(MainWindow Window, INodeViewModel Node)> OpenWithPassthroughAsync() {
            MainWindow window = await OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreatePassthroughNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node);
            BaseNodeElement element = window.GraphCanvas.NodeElements.Single(n => n.ViewModel.Id == id);
            element.RequestStateUpdate();
            element.PrePaint();
            Dispatcher.UIThread.RunJobs();
            return (window, node!);
        }

        internal static void Select(MainWindow window, INodeViewModel node) {
            BaseNodeElement element = window.GraphCanvas.NodeElements.Single(n => n.ViewModel.Id == node.Id);
            window.GraphCanvas.Viewer.SetSelection([element]);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
