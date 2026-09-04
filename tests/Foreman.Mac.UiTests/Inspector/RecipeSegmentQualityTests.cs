using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    //Real-preset icon-compositing coverage for RecipeSegmentViewModel.PickQuality, which writes through immediately to the node's own assembler.
    public class RecipeSegmentQualityTests {
        private const string PresetName = "Factorio 2.0 Space Age";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                sharedCache ??= await LoadCacheAsync().ConfigureAwait(false);
                return sharedCache;
            } finally {
                CacheGate.Release();
            }
        }

        private static async Task<DataCache> LoadCacheAsync() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(PresetName, true, true), new System.Progress<KeyValuePair<int, string>>()).ConfigureAwait(false);
            return cache;
        }

        private static (GraphCanvasControl Control, IQuality Normal, IQuality Legendary, IRecipeNodeViewModel Node) NewFixture(DataCache cache) {
            IQuality normal = cache.DefaultQuality!;
            IQuality legendary = cache.Qualities["legendary"];
            IRecipe recipe = cache.Recipes["wooden-chest"]; //9 module options, no beacon needed for module coverage
            IAssembler assembler = cache.Assemblers["assembling-machine-2"];

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            control.Viewer.Graph.MaxQualitySteps = 2;

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;

            if (control.Viewer.Session.Editor.RequestNodeController(id) is not RecipeNodeController controller)
                throw new Xunit.Sdk.XunitException("Recipe node has no controller.");
            controller.SetAssembler(new AssemblerQualityPair(assembler, normal));
            controller.RemoveAssemblerModules();
            control.Viewer.Graph.UpdateNodeValues();

            return (control, normal, legendary, node);
        }

        [AvaloniaFact]
        public async Task PickQuality_IconsResolve_ForModuleOptions() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache, new AppSettings());

            PickGridItem qualityItem = vm.QualityOptions.Single(i => Equals(i.Key, legendary));
            vm.PickQuality(qualityItem);

            Assert.Equal(legendary, node.SelectedAssembler.Quality);
            Assert.Equal(9, vm.ModuleOptions.Count);
            Assert.All(vm.ModuleOptions, option => Assert.NotNull(option.Icon));
        }

        [AvaloniaFact]
        public async Task PickQuality_ThenAddModule_EquipsAtThePickedQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache, new AppSettings());

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));
            vm.AddModule(vm.ModuleOptions[0]);

            Assert.Single(node.AssemblerModules);
            Assert.Equal(legendary, node.AssemblerModules[0].Quality);
        }

        [AvaloniaFact]
        public async Task PickQuality_ThenPickBeacon_UsesThePickedQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache, new AppSettings());

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));
            Assert.NotEmpty(vm.BeaconOptions);
            vm.PickBeacon(vm.BeaconOptions[0]);

            Assert.True(node.SelectedBeacon);
            Assert.Equal(legendary, node.SelectedBeacon.Quality);
        }

        [AvaloniaFact]
        public async Task PickQuality_ThenAddBeaconModule_UsesThePickedQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache, new AppSettings());

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));
            vm.PickBeacon(vm.BeaconOptions[0]);
            Assert.NotEmpty(vm.BeaconModuleOptions);
            vm.AddBeaconModule(vm.BeaconModuleOptions[0]);

            Assert.Single(node.BeaconModules);
            Assert.Equal(legendary, node.BeaconModules[0].Quality);
        }

        [AvaloniaFact]
        public async Task PickQuality_SetsGraphDefaultAssemblerQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache, new AppSettings());

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));

            Assert.Equal(legendary, control.Viewer.Graph.DefaultAssemblerQuality);
        }
    }
}
