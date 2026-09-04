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
            //ApplyLoadedSettings only seeds Context.DCache/DefaultAssemblerQuality when Settings is set (a
            //full boot does that); no test-only AppSettings exists here, so set both directly - same as
            //OpenEmptyWindowAsync below, and needed by the Recipe segment (RecipeSegmentViewModel reads
            //Context.DCache directly).
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            var pair = new RecipeQualityPair(cache.Recipes["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(pair, new Point(0, 0));
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        //A MainWindow with an empty graph over the shared vanilla cache, for tests that build their own
        //node mix (FlowSegmentTests' rate/key-node/simplify coverage) rather than reusing the one recipe
        //node OpenWindowWithGraphAsync seeds.
        internal static async Task<MainWindow> OpenEmptyWindowAsync() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            //ApplyLoadedSettings only seeds Context.DCache/DefaultAssemblerQuality when Settings is set (a
            //full boot does that); no test-only AppSettings exists here, so set both directly - EditNode's
            //click dispatch (FlowSegmentTests' click-selects-only coverage) bails out without Context.DCache.
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        //One passthrough node, selected-ready - the fixture the brief's Step 1 tests build against. Primes
        //RequestStateUpdate/PrePaint the same way ClickToEditTests' AddSupplierNode does, so a real click
        //lands on the node's actual (not still-centred) hit-test geometry.
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

        //Selects a node the same way a real click does (GraphViewer.SetSelection), so the inspector's
        //Node/Segment/SegmentHost wiring reacts exactly as it would through the canvas.
        internal static void Select(MainWindow window, INodeViewModel node) {
            BaseNodeElement element = window.GraphCanvas.NodeElements.Single(n => n.ViewModel.Id == node.Id);
            window.GraphCanvas.Viewer.SetSelection([element]);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
