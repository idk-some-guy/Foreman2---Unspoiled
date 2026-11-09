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
    //Assemblers for every Space Age resource including the fluid-yielding ones (crude-oil, lava, water, ...)
    //- the data-loading and RecipeChooserPanel filter/group logic (RecipeMatchesKeyItem, GetSubgroupList,
    //GetSortedGroups) are byte-for-byte upstream ports and behave correctly, confirmed here. The real root
    //cause lives one layer down: the "Resource Extraction" group's own icon rendered blank (IconCache.GetIcon
    //resolving its Graphics/*.png path against the wrong working directory - see IconPipelineTests.cs and
    //the upstream-divergences.md entry for the fix), so nothing drew the group button apart from its plain
    //gray fill, and it went unnoticed in the group row. Space Age surfaces this far more than vanilla only
    //because it gives raw resources a second, differently-grouped producer (recycling, crushing) that
    //happens to populate the sticky default group (IRChooserPanel's process-wide `startingGroup`,
    //docs/panels-reference.md §2) with something, so there's no empty-grid cue to go looking at another tab.
    //These tests pin that the extraction recipe and its group stay reachable at the data/filter layer.
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
        //NewNodeType.Supplier - GraphCanvasControl.cs's DraggedLinkElement.EndDrag): on a fresh chooser
        //(no sticky group carried over from an earlier panel), the reroute-to-nearest-match logic upstream
        //itself ports (IRChooserPanel.GetSubgroupList's alternateGroup search) lands directly on the
        //"Resource Extraction" group, since that's the only group with a matching Product recipe for a raw
        //ore with no prior recycling/crushing byproduct chain competing for group real estate.
        [AvaloniaFact]
        public async Task LinkFromConsumerInput_ExtractionRecipeIsCompatible() {
            DataCache cache = await GetCacheAsync();
            IItem ironOre = cache.Items["iron-ore"];
            AddLinkContext link = SupplierLinkFor(cache, ironOre);

            var catalog = new AddCatalog(cache, new AppSettings());
            var rows = catalog.Search("", AddScope.All, link);

            Assert.Contains(rows, r => r.Recipe?.Name == "§§r:e:iron-ore");
        }

        //Ports the human's second-reported repro (the Add Item flow -> RecipeChooserPanel with
        //NewNodeType.Disconnected). Even when the sticky "logistics" group shows an unrelated match first
        //(concrete's recipe consumes iron ore as an ingredient, matching Disconnected mode's Ingredient-or-
        //Product filter), the "Resource Extraction" group icon stays present, enabled, and reachable -
        //selecting it surfaces the real extraction recipe.
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
