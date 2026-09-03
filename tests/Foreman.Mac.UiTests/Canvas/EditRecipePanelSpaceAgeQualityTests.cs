using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Panels;
using Foreman.Models;
using Foreman.Models.Nodes;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Investigates the human's third module-options report: a row of near-identical gray cells under the
    //Space Age preset with Maximum Quality Steps raised above 1. Traced against the human's own saved graph
    //(loaded through the real Space Age Modified preset + GraphViewer.LoadDocument, both off-repo) - every
    //node in it already has AssemblerModules.Count == Assembler.ModuleSlots (fully equipped), which is
    //exactly what turns every option cell gray: IconButton.PaintOnto's GrayscaleFilter (docs comment atop
    //IconButton.cs) only ever fires on IsEnabled == false, and UpdateAssemblerModules's
    //`nodeData.AssemblerModules.Count < nodeData.SelectedAssembler.Assembler.ModuleSlots` predicate is a
    //byte-for-byte port of upstream's own (EditRecipePanel.cs:265-266) - a fully-slotted assembler goes gray
    //in the real WinForms Foreman too. Module data (eligibility filtering, per-quality icon compositing) all
    //checked out against real Space Age recipes/assemblers; nothing here diverges from upstream. These tests
    //pin the free-slots case (cells stay enabled and colorful) so a future change can't quietly start
    //graying out cells that do have room, and the full-slots case (cells correctly go gray, matching
    //upstream) so nobody "fixes" that expected state into a divergence later.
    public class EditRecipePanelSpaceAgeQualityTests {
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

        [AvaloniaFact]
        public async Task ModuleOptions_FreeSlots_MaxQualityStepsAboveOne_StayEnabled() {
            DataCache cache = await GetCacheAsync();
            IQuality normal = cache.DefaultQuality!;
            IRecipe recipe = cache.Recipes["iron-gear-wheel"];
            IAssembler assembler = cache.Assemblers["assembling-machine-2"];

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            control.Viewer.Graph.MaxQualitySteps = 2;

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;

            var recipeController = (RecipeNodeController)control.Viewer.Session.Editor.RequestNodeController(id)!;
            recipeController.SetAssembler(new AssemblerQualityPair(assembler, normal));
            recipeController.RemoveAssemblerModules();
            control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, control.Viewer);

            Assert.NotEmpty(panel.AModuleOptions);
            List<string> disabledCellNames = panel.AModuleOptions
                .Where(b => !b.IsEnabled)
                .Select(b => (b.DataObject as IModule)?.Name ?? "?")
                .ToList();

            Assert.True(node.AssemblerModules.Count < assembler.ModuleSlots,
                $"fixture invariant broken: {node.AssemblerModules.Count} modules already equipped of {assembler.ModuleSlots} slots.");
            Assert.Empty(disabledCellNames);
        }

        //Matches the human's screenshot count: wooden-chest disallows the productivity effect (real Space
        //Age recipe data - allowed_effects.productivity == false), so the nine module options are the
        //speed/efficiency/quality families only (3 tiers each, no productivity family). Pins that those nine
        //cells' names and enabled state are what upstream's GetAssemblerModuleOptions/UpdateAssemblerModules
        //would produce with two open slots.
        [AvaloniaFact]
        public async Task ModuleOptions_ProductivityDisallowedRecipe_NineCells_FreeSlots_StayEnabled() {
            DataCache cache = await GetCacheAsync();
            IQuality normal = cache.DefaultQuality!;
            IRecipe recipe = cache.Recipes["wooden-chest"];
            IAssembler assembler = cache.Assemblers["assembling-machine-2"];

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            control.Viewer.Graph.MaxQualitySteps = 2;

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;

            var recipeController = (RecipeNodeController)control.Viewer.Session.Editor.RequestNodeController(id)!;
            recipeController.SetAssembler(new AssemblerQualityPair(assembler, normal));
            recipeController.RemoveAssemblerModules();
            control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, control.Viewer);

            List<string> cellNames = panel.AModuleOptions.Select(b => (b.DataObject as IModule)?.Name ?? "?").ToList();
            Assert.Equal(9, cellNames.Count);
            Assert.DoesNotContain(cellNames, n => n.StartsWith("productivity-module", System.StringComparison.Ordinal));

            List<string> disabledCellNames = panel.AModuleOptions
                .Where(b => !b.IsEnabled)
                .Select(b => (b.DataObject as IModule)?.Name ?? "?")
                .ToList();
            Assert.Empty(disabledCellNames);
        }

        //Same nine-cell scenario, but with the panel's own Quality selector moved off Normal (the human's
        //"changed Maximum Quality Steps during gate testing" almost certainly also left this selector on a
        //non-default tier - Space Age's own Quality Selector - InitializeBaseButton/ModuleQualityPair.Icon
        //composite path). Every option cell in a family with legendary selected shares the same
        //IconCacheProcessor.CombinedQualityIcon(module.Icon, quality.Icon) overlay; if the legendary icon
        //itself failed to resolve, every cell's Icon collapses to null and IconButton.PaintOnto falls back
        //to a flat FillColor box - visually a row of identical gray cells with no per-family color at all,
        //independent of IsEnabled.
        [AvaloniaFact]
        public async Task ModuleOptions_ProductivityDisallowedRecipe_LegendaryQualitySelected_IconsResolve() {
            DataCache cache = await GetCacheAsync();
            IQuality normal = cache.DefaultQuality!;
            IQuality legendary = cache.Qualities["legendary"];
            IRecipe recipe = cache.Recipes["wooden-chest"];
            IAssembler assembler = cache.Assemblers["assembling-machine-2"];

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            control.Viewer.Graph.MaxQualitySteps = 2;

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;

            var recipeController = (RecipeNodeController)control.Viewer.Session.Editor.RequestNodeController(id)!;
            recipeController.SetAssembler(new AssemblerQualityPair(assembler, normal));
            recipeController.RemoveAssemblerModules();
            control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, control.Viewer);

            List<IQuality> panelQualities = cache.AvailableQualities.Where(q => q.Enabled).ToList();
            int legendaryIndex = panelQualities.IndexOf(legendary);
            Assert.True(legendaryIndex >= 0, "legendary quality is not selectable on this panel.");
            panel.QualitySelector.Selector.SelectedIndex = legendaryIndex;
            Assert.Equal(legendary, panel.QualitySelector.SelectedQuality);

            Assert.Equal(9, panel.AModuleOptions.Count);
            Assert.True(legendary.Icon is not null, "legendary quality icon failed to load from the preset icon cache.");

            foreach (IconButton option in panel.AModuleOptions) {
                var module = (IModule)option.DataObject!;
                Assert.True(module.Icon is not null, $"{module.Name} base icon failed to load.");
                Assert.True(option.Icon is not null, $"{module.Name} at legendary quality composited to a null icon.");
            }
        }

        //The actual root cause behind the screenshot: an assembler whose module slots are already full.
        //Every option cell correctly goes IsEnabled == false here (the same
        //`AssemblerModules.Count < ModuleSlots` predicate upstream uses), so PaintOnto's grayscale filter
        //applies to all nine - visually identical gray cells with no per-family color, exactly like the
        //human's screenshot. This is not a bug: it's the intended "no free slot" affordance, and it renders
        //this way in upstream too. Pinned so nobody "fixes" gray-when-full into a divergence from upstream.
        [AvaloniaFact]
        public async Task ModuleOptions_FullSlots_AllCellsDisabled_IconsStillResolve_MatchesUpstream() {
            DataCache cache = await GetCacheAsync();
            IQuality normal = cache.DefaultQuality!;
            IRecipe recipe = cache.Recipes["wooden-chest"];
            IAssembler assembler = cache.Assemblers["assembling-machine-2"];

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;

            var recipeController = (RecipeNodeController)control.Viewer.Session.Editor.RequestNodeController(id)!;
            recipeController.SetAssembler(new AssemblerQualityPair(assembler, normal));
            IModule speedModule = (IModule)cache.Modules["speed-module"];
            recipeController.AddAssemblerModules(new ModuleQualityPair(speedModule, normal)); //fills every slot (right-click semantics)
            control.Viewer.Graph.UpdateNodeValues();
            Assert.Equal(assembler.ModuleSlots, node.AssemblerModules.Count);

            var panel = new EditRecipePanel(node, control.Viewer);
            Assert.Equal(9, panel.AModuleOptions.Count);
            Assert.All(panel.AModuleOptions, option => Assert.False(option.IsEnabled));
            Assert.All(panel.AModuleOptions, option => Assert.NotNull(option.Icon)); //still the real per-module icon, just grayscaled at paint time
        }

        //Closes the gap between the two extremes above: a partially-filled assembler (1 of 2 slots) must
        //stay enabled and colorful, not gray, until the last slot is actually taken. Confirms the predicate
        //keys on AssemblerModules.Count vs ModuleSlots and nothing else (e.g. "any module equipped at all")
        //that could gray cells out too early.
        [AvaloniaFact]
        public async Task ModuleOptions_OneOfTwoSlotsFilled_CellsStayEnabled() {
            DataCache cache = await GetCacheAsync();
            IQuality normal = cache.DefaultQuality!;
            IRecipe recipe = cache.Recipes["wooden-chest"];
            IAssembler assembler = cache.Assemblers["assembling-machine-2"];
            Assert.Equal(2, assembler.ModuleSlots);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;

            var recipeController = (RecipeNodeController)control.Viewer.Session.Editor.RequestNodeController(id)!;
            recipeController.SetAssembler(new AssemblerQualityPair(assembler, normal));
            recipeController.RemoveAssemblerModules();
            IModule speedModule = (IModule)cache.Modules["speed-module"];
            recipeController.AddAssemblerModule(new ModuleQualityPair(speedModule, normal)); //left click: adds exactly one
            control.Viewer.Graph.UpdateNodeValues();
            Assert.Single(node.AssemblerModules);

            var panel = new EditRecipePanel(node, control.Viewer);
            Assert.Equal(9, panel.AModuleOptions.Count);
            Assert.All(panel.AModuleOptions, option => Assert.True(option.IsEnabled));
        }
    }
}
