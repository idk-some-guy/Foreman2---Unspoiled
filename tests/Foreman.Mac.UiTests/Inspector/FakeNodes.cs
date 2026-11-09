using Foreman.DataCaching;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.UiTests.Inspector {
    internal static class FakeNodes {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private static readonly Lazy<(DataCache Cache, ProductionGraphSession Session)> LazyFixture = new(BuildFixture);

        public static IRecipeNodeViewModel Recipe() {
            (DataCache cache, ProductionGraphSession session) = LazyFixture.Value;
            var pair = new RecipeQualityPair(cache.Recipes["iron-plate"], cache.DefaultQuality!);
            NodeId id = session.Editor.CreateRecipeNode(pair, new Point(0, 0));
            session.View.TryGetNode(id, out INodeViewModel? node);
            return (IRecipeNodeViewModel)node!;
        }

        public static IPassthroughNodeViewModel Passthrough() {
            (DataCache cache, ProductionGraphSession session) = LazyFixture.Value;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId id = session.Editor.CreatePassthroughNode(pair, new Point(0, 0));
            session.View.TryGetNode(id, out INodeViewModel? node);
            return (IPassthroughNodeViewModel)node!;
        }

        private static (DataCache, ProductionGraphSession) BuildFixture() {
            var cache = new DataCache(filterRecipes: true);
            cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>()).GetAwaiter().GetResult();
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            return (cache, session);
        }
    }
}
