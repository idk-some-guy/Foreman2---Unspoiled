using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    //Investigates the human-reported "no extraction recipe in the chooser" gate finding against the real
    //Space Age preset (not vanilla - a prior agent's vanilla-only check wrongly cleared it). The extraction
    //recipe (§§r:e:<item>) is present, Available, Enabled, and correctly linked to its mining-drill
    //Assemblers for every Space Age resource including the fluid-yielding ones (crude-oil, lava, water, ...).
    //The real root cause lived one layer down: the extraction group's own icon rendered blank
    //(IconCache.GetIcon resolving its Graphics/*.png path against the wrong working directory - see
    //IconPipelineTests.cs and the upstream-divergences.md entry for the fix). These tests pin that the
    //extraction recipe and its group stay reachable through AddCatalog/AddBrowserViewModel, the retired
    //on-canvas chooser panel's C2b successors.
    public class SpaceAgeExtractionRecipeTests {
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
            await cache.LoadAllData(new Preset(PresetName, true, true), new Progress<KeyValuePair<int, string>>()).ConfigureAwait(false);
            return cache;
        }

        private static readonly string[] RawResourceItemNames =
            ["iron-ore", "copper-ore", "coal", "stone", "uranium-ore", "scrap", "crude-oil", "lava", "water"];

        public static IEnumerable<object[]> RawResourceNames() =>
            RawResourceItemNames.Select(name => new object[] { name });

        [Theory]
        [MemberData(nameof(RawResourceNames))]
        public async Task ExtractionRecipe_AvailableEnabledWithAssemblers_ForEverySpaceAgeResource(string itemName) {
            DataCache cache = await GetCacheAsync();
            IItem item = cache.Items[itemName];
            string extractionRecipeName = "§§r:e:" + itemName;

            IRecipe recipe = item.ProductionRecipes.Single(r => r.Name == extractionRecipeName);

            Assert.True(recipe.Available, $"{extractionRecipeName} should be Available.");
            Assert.True(recipe.Enabled, $"{extractionRecipeName} should be Enabled.");
            Assert.NotEmpty(recipe.Assemblers);
            Assert.Contains(recipe.Assemblers, a => a.Enabled);
        }

        //Ports the human's first-reported repro (link-drag from a consumer's input, which resolves to
        //NewNodeType.Supplier): AddCatalog.IsCompatible surfaces the extraction recipe for a Supplier link
        //on the raw ore, exactly as the retired chooser's ingredient/product filter used to.
        [AvaloniaFact]
        public async Task LinkFromConsumerInput_ExtractionRecipeIsCompatible() {
            DataCache cache = await GetCacheAsync();
            IItem ironOre = cache.Items["iron-ore"];
            AddLinkContext link = SupplierLinkFor(cache, ironOre);

            var catalog = new AddCatalog(cache, new AppSettings());
            var rows = catalog.Search("", AddScope.All, link);

            Assert.Contains(rows, r => r.Recipe?.Name == "§§r:e:iron-ore");
        }

        //Ports the human's second-reported repro (the Add Item flow): the extraction group stays listed
        //and its recipe section still surfaces the extraction recipe once selected.
        [AvaloniaFact]
        public async Task ResourceExtractionGroup_IsListedAndPopulated() {
            DataCache cache = await GetCacheAsync();
            var vm = new AddBrowserViewModel(new AddCatalog(cache, new AppSettings()), cache, new AppSettings(), (_, _) => null) {
                Segment = BrowserSegment.Recipes,
            };

            var extractionGroup = Assert.Single(vm.Groups, g => g.Group.Name == "§§g:extra_group").Group;
            vm.SelectGroup(extractionGroup);

            Assert.Contains(vm.Sections.SelectMany(s => s.Cells), c => c.Candidate.Recipe?.Name == "§§r:e:iron-ore");
        }

        private static AddLinkContext SupplierLinkFor(DataCache cache, IItem item) {
            var pair = new ItemQualityPair(item, cache.DefaultQuality!);
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(600, 600);
            var window = new AvaloniaWindow { Content = control, Width = 600, Height = 600 };
            window.Show();

            NodeId nodeId = control.Viewer.Session.Editor.CreateConsumerNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement originElement = control.Viewer.NodeElementDictionary[nodeId];
            originElement.RequestStateUpdate();
            originElement.PrePaint();

            return new AddLinkContext(pair, NewNodeType.Supplier, new FRange(0, 0, true), originElement, NodeDirection.Up);
        }
    }
}
