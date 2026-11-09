using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.Mac;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class MainWindowDirtyStateTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
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

        [AvaloniaFact]
        public void IsDirty_FreshWindow_IsFalse() {
            var window = new MainWindow();
            window.Show();

            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public async Task GraphMutation_NodeAdded_SetsIsDirty() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);

            Assert.True(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public async Task NewGraphAsync_ClearsIsDirty() {
            var window = new MainWindow();
            window.Show();
            window.ViewModel.IsDirty = true;

            await window.NewGraphAsync();

            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void ApplyLoadedGraphUiState_ClearsIsDirty() {
            var window = new MainWindow();
            window.Show();
            window.ViewModel.IsDirty = true;

            window.ApplyLoadedGraphUiState("/tmp/does-not-need-to-exist.fjson");

            Assert.False(window.ViewModel.IsDirty);
        }

        private static ToolbarItem SaveToolbarItem(MainWindow window) =>
            window.GetVisualDescendants().OfType<ToolbarItem>().Single(i => i.Label == "Save");

        [AvaloniaFact]
        public void SaveToolbarItem_IsOn_TracksIsDirty() {
            var window = new MainWindow();
            window.Show();
            ToolbarItem save = SaveToolbarItem(window);
            Assert.False(save.IsOn);

            window.ViewModel.IsDirty = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsOn);

            window.ViewModel.IsDirty = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsOn);
        }
    }
}
