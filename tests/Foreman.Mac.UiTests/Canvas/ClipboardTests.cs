using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises reference §5's clipboard flow: NodeClipboard's copy/cut/paste fragment pipeline,
    //RecipeNodeElement's paste-options right-click block (reference §4c), and the Cmd+C/X/V keyboard routing
    //(reference §7, Cmd replaces Ctrl per the Cmd-mapping note).
    public class ClipboardTests {
        private const int Half = 250;
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        //---- real vanilla DataCache, loaded once and shared across tests (loading is expensive) ----

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

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }
        }

        private static Fixture NewFixture(DataCache cache) {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            control.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            control.Viewer.Context.DCache = cache;
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();
            return new Fixture { Cache = cache, Control = control, Window = window };
        }

        private static RecipeNodeElement AddRecipeNode(Fixture fx, string recipeName, Point location) {
            IRecipe recipe = fx.Cache.Recipes[recipeName];
            var pair = new RecipeQualityPair(recipe, fx.Cache.DefaultQuality!);
            fx.Control.Viewer.Graph.CreateRecipeNode(pair, location);
            IRecipeNodeViewModel viewModel = fx.Control.Viewer.Session.View.Nodes.OfType<IRecipeNodeViewModel>().Last();
            return (RecipeNodeElement)fx.Control.Viewer.NodeElementDictionary[viewModel.Id];
        }

        private static RecipeNodeController ControllerFor(Fixture fx, IRecipeNodeViewModel viewModel) {
            fx.Control.Viewer.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            return (RecipeNodeController)fx.Control.Viewer.Graph.RequestNodeController(node!)!;
        }

        private static void SetAssemblerModuleCount(Fixture fx, IRecipeNodeViewModel viewModel, string moduleName, int count) {
            IModule module = fx.Cache.Modules[moduleName];
            var modules = Enumerable.Repeat(new ModuleQualityPair(module, fx.Cache.DefaultQuality!), count);
            ControllerFor(fx, viewModel).SetAssemblerModules(modules, filterModules: false);
        }

        private static void SetBeaconWithModules(Fixture fx, IRecipeNodeViewModel viewModel, int moduleCount, double beaconCount) {
            IBeacon beacon = fx.Cache.Beacons["beacon"];
            RecipeNodeController controller = ControllerFor(fx, viewModel);
            controller.SetBeacon(new BeaconQualityPair(beacon, fx.Cache.DefaultQuality!));
            controller.SetBeaconCount(beaconCount);

            IModule module = fx.Cache.Modules["speed-module"];
            var modules = Enumerable.Repeat(new ModuleQualityPair(module, fx.Cache.DefaultQuality!), moduleCount);
            controller.SetBeaconModules(modules, filterModules: false);
        }

        //---- NodeClipboard.Copy/Cut/Paste (reference §5's fragment pipeline) ----

        [AvaloniaFact]
        public async Task Copy_ScopesFragmentToSelectedNodesOnly() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement gear = AddRecipeNode(fx, "iron-gear-wheel", new Point(-120, 0));
            AddRecipeNode(fx, "iron-gear-wheel", new Point(120, 0));
            fx.Control.Viewer.SetSelection([gear]);

            string fragmentJson = NodeClipboard.Copy(fx.Control.Viewer);

            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadGraphPayload(fragmentJson);
            Assert.NotNull(document);
            Assert.Single(document.Nodes);
        }

        [AvaloniaFact]
        public async Task Copy_DoesNotMutateTheGraph() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement gear = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            fx.Control.Viewer.SetSelection([gear]);

            NodeClipboard.Copy(fx.Control.Viewer);

            Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.Null(fx.Control.Viewer.Graph.SerializeNodeIdSet);
        }

        [AvaloniaFact]
        public async Task Cut_DeletesSelectedNodesAndReturnsTheSameFragmentAsCopy() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement gear = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            AddRecipeNode(fx, "iron-gear-wheel", new Point(300, 0));
            NodeId cutId = gear.ViewModel.Id;
            fx.Control.Viewer.SetSelection([gear]);

            string fragmentJson = NodeClipboard.Cut(fx.Control.Viewer);

            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadGraphPayload(fragmentJson);
            Assert.NotNull(document);
            Assert.Single(document.Nodes);
            Assert.False(fx.Control.Viewer.NodeElementDictionary.ContainsKey(cutId));
            Assert.Single(fx.Control.Viewer.NodeElements);
        }

        [AvaloniaFact]
        public async Task Paste_IntoEmptyGraph_CreatesNodeCenteredOnOrigin() {
            DataCache cache = await GetCacheAsync();
            Fixture source = NewFixture(cache);
            RecipeNodeElement gear = AddRecipeNode(source, "iron-gear-wheel", new Point(60, 0));
            source.Control.Viewer.SetSelection([gear]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture(cache);
            var origin = new Point(240, -180);

            NodeClipboard.Paste(target.Control.Viewer, cache, fragmentJson, origin);

            BaseNodeElement pasted = Assert.Single(target.Control.Viewer.NodeElements);
            Assert.Equal(origin, pasted.ViewModel.Location);
        }

        [AvaloniaFact]
        public async Task Paste_MultipleNodes_CentersCentroidOnOrigin_PreservingRelativeLayout() {
            DataCache cache = await GetCacheAsync();
            Fixture source = NewFixture(cache);
            RecipeNodeElement left = AddRecipeNode(source, "iron-gear-wheel", new Point(-120, 0));
            RecipeNodeElement right = AddRecipeNode(source, "iron-gear-wheel", new Point(120, 0));
            source.Control.Viewer.SetSelection([left, right]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture(cache);
            var origin = new Point(0, 500);

            NodeClipboard.Paste(target.Control.Viewer, cache, fragmentJson, origin);

            List<BaseNodeElement> pasted = [.. target.Control.Viewer.NodeElements];
            Assert.Equal(2, pasted.Count);
            double centroidX = pasted.Average(n => n.ViewModel.Location.X);
            double centroidY = pasted.Average(n => n.ViewModel.Location.Y);
            Assert.Equal(origin.X, centroidX, 0.001);
            Assert.Equal(origin.Y, centroidY, 0.001);
            //relative offset between the two pasted nodes is preserved from the copied layout
            Assert.Equal(240, pasted.Max(n => n.ViewModel.Location.X) - pasted.Min(n => n.ViewModel.Location.X));
        }

        [AvaloniaFact]
        public async Task Paste_ReplacesSelectionWithJustThePastedNodes() {
            DataCache cache = await GetCacheAsync();
            Fixture fx = NewFixture(cache);
            RecipeNodeElement existing = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            fx.Control.Viewer.SetSelection([existing]);
            string fragmentJson = NodeClipboard.Copy(fx.Control.Viewer);

            NodeClipboard.Paste(fx.Control.Viewer, cache, fragmentJson, new Point(500, 500));

            BaseNodeElement pasted = Assert.Single(fx.Control.Viewer.SelectedNodes);
            Assert.NotEqual(existing.ViewModel.Id, pasted.ViewModel.Id);
        }

        [AvaloniaTheory]
        [InlineData("not json at all")]
        [InlineData("")]
        public async Task Paste_UnparsableClipboardText_NoCrash_NoNewNodes(string clipboardText) {
            Fixture fx = NewFixture(await GetCacheAsync());

            NodeClipboard.Paste(fx.Control.Viewer, fx.Cache, clipboardText, Point.Empty);

            Assert.Empty(fx.Control.Viewer.NodeElements);
        }

        //---- NodeCopyOptions (reference §5's domain-level clipboard payload) ----

        [AvaloniaFact]
        public async Task NodeCopyOptions_FromViewModel_CapturesAssemblerAndNeighbourCount() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement gear = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var viewModel = (IRecipeNodeViewModel)gear.ViewModel;

            var options = new NodeCopyOptions(viewModel);

            Assert.Equal(viewModel.SelectedAssembler.Assembler, options.Assembler.Assembler);
            Assert.Equal(viewModel.NeighbourCount, options.NeighbourCount);
            Assert.Equal(viewModel.ExtraProductivity, options.ExtraProductivityBonus);
        }

        [AvaloniaFact]
        public async Task NodeCopyOptions_GetNodeCopyOptions_RoundTripsThroughSerializedText() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement gear = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var original = new NodeCopyOptions((IRecipeNodeViewModel)gear.ViewModel);
            string serialized = GraphSaveCodec.WriteNodeCopyOptionsToString(original.ToSaveDocument());

            NodeCopyOptions? restored = NodeCopyOptions.GetNodeCopyOptions(serialized, fx.Cache);

            Assert.NotNull(restored);
            Assert.Equal(original.Assembler.Assembler.Name, restored.Assembler.Assembler.Name);
            Assert.Equal(original.NeighbourCount, restored.NeighbourCount);
        }

        [AvaloniaTheory]
        [InlineData("not json at all")]
        [InlineData("")]
        public async Task NodeCopyOptions_GetNodeCopyOptions_UnparsableText_ReturnsNull(string clipboardText) {
            DataCache cache = await GetCacheAsync();
            Assert.Null(NodeCopyOptions.GetNodeCopyOptions(clipboardText, cache));
        }

        //---- RecipeNodeElement.AddRClickMenuOptions (reference §4c, the ~280-line paste-options body) ----

        [AvaloniaFact]
        public async Task RecipeMenu_AlwaysHasCopyThisAssemblersOptions_WhichWritesClipboardText() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement gear = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var viewModel = (IRecipeNodeViewModel)gear.ViewModel;
            string? written = null;
            fx.Control.Viewer.Context.SetClipboardText = text => written = text;

            var entries = gear.BuildRightClickMenu();
            entries.Single(e => e.Caption == "Copy this assembler's options").Invoke!.Invoke();

            Assert.Equal(GraphSaveCodec.WriteNodeCopyOptionsToString(new NodeCopyOptions(viewModel).ToSaveDocument()), written);
        }

        [AvaloniaFact]
        public async Task RecipeMenu_NotInSelection_OnlyHasCopyOptions_NoApplyOrPasteItems() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement selected = AddRecipeNode(fx, "iron-gear-wheel", new Point(-120, 0));
            RecipeNodeElement excluded = AddRecipeNode(fx, "iron-gear-wheel", new Point(120, 0));
            fx.Control.Viewer.SetSelection([selected]);

            var entries = excluded.BuildRightClickMenu();

            Assert.DoesNotContain(entries, e => e.Caption == "Apply default assembler(s)");
            Assert.Single(entries, e => e.Caption == "Copy this assembler's options");
        }

        [AvaloniaFact]
        public async Task RecipeMenu_ApplyDefaultModules_AppliesToEveryTargetInSelection() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement primary = AddRecipeNode(fx, "iron-gear-wheel", new Point(-120, 0));
            RecipeNodeElement other = AddRecipeNode(fx, "iron-gear-wheel", new Point(120, 0));
            var otherViewModel = (IRecipeNodeViewModel)other.ViewModel;
            ControllerFor(fx, otherViewModel).SetAssemblerModules([], filterModules: false);
            fx.Control.Viewer.SetSelection([primary, other]);

            var entries = primary.BuildRightClickMenu();
            entries.Single(e => e.Caption == "Apply default modules").Invoke!.Invoke();

            Assert.Equal(((IRecipeNodeViewModel)primary.ViewModel).AssemblerModules.Count, otherViewModel.AssemblerModules.Count);
        }

        [AvaloniaFact]
        public async Task RecipeMenu_RemoveModules_OnlyShownWhenATargetHasModules_AndClearsThem() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement bare = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var bareViewModel = (IRecipeNodeViewModel)bare.ViewModel;
            ControllerFor(fx, bareViewModel).SetAssemblerModules([], filterModules: false);
            fx.Control.Viewer.SetSelection([bare]);
            Assert.DoesNotContain(bare.BuildRightClickMenu(), e => e.Caption == "Remove modules");

            SetAssemblerModuleCount(fx, bareViewModel, "speed-module", 1);
            var entries = bare.BuildRightClickMenu();
            entries.Single(e => e.Caption == "Remove modules").Invoke!.Invoke();

            Assert.Empty(bareViewModel.AssemblerModules);
        }

        [AvaloniaFact]
        public async Task RecipeMenu_RemoveBeacons_OnlyShownWhenATargetHasABeacon_AndClearsIt() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement node = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var viewModel = (IRecipeNodeViewModel)node.ViewModel;
            fx.Control.Viewer.SetSelection([node]);
            Assert.DoesNotContain(node.BuildRightClickMenu(), e => e.Caption == "Remove beacons");

            SetBeaconWithModules(fx, viewModel, moduleCount: 1, beaconCount: 1);
            var entries = node.BuildRightClickMenu();
            entries.Single(e => e.Caption == "Remove beacons").Invoke!.Invoke();

            Assert.False(viewModel.SelectedBeacon);
        }

        [AvaloniaFact]
        public async Task RecipeMenu_NoDataCache_NoPasteOptionsBlock() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement node = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var viewModel = (IRecipeNodeViewModel)node.ViewModel;
            fx.Control.Viewer.Context.GetClipboardText = () => GraphSaveCodec.WriteNodeCopyOptionsToString(new NodeCopyOptions(viewModel).ToSaveDocument());
            fx.Control.Viewer.Context.DCache = null;

            var entries = node.BuildRightClickMenu();

            Assert.DoesNotContain(entries, e => e.Caption == "Paste selected options");
        }

        [AvaloniaFact]
        public async Task RecipeMenu_PasteOptions_IncompatibleTargetNode_ApplySkipsItButNotTheCompatibleOne() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement compatible = AddRecipeNode(fx, "iron-gear-wheel", new Point(-120, 0));
            RecipeNodeElement incompatible = AddRecipeNode(fx, "plastic-bar", new Point(120, 0));
            var compatibleViewModel = (IRecipeNodeViewModel)compatible.ViewModel;
            var incompatibleViewModel = (IRecipeNodeViewModel)incompatible.ViewModel;
            string incompatibleAssemblerBefore = incompatibleViewModel.SelectedAssembler.Assembler.Name;
            string compatibleAssemblerName = compatibleViewModel.SelectedAssembler.Assembler.Name;
            string clipboardText = GraphSaveCodec.WriteNodeCopyOptionsToString(new NodeCopyOptions(compatibleViewModel).ToSaveDocument());
            fx.Control.Viewer.Context.GetClipboardText = () => clipboardText;
            fx.Control.Viewer.SetSelection([compatible, incompatible]);

            var entries = compatible.BuildRightClickMenu();
            MenuEntry assemblerCheckbox = entries.Single(e => e.Caption == compatibleViewModel.SelectedAssembler.Assembler.GetEntityTypeName(false));
            Assert.True(assemblerCheckbox.Enabled);
            assemblerCheckbox.Checkbox!.Checked = true;

            entries.Single(e => e.Caption == "Paste selected options").Invoke!.Invoke();

            Assert.Equal(incompatibleAssemblerBefore, incompatibleViewModel.SelectedAssembler.Assembler.Name);
            Assert.Equal(compatibleAssemblerName, compatibleViewModel.SelectedAssembler.Assembler.Name);
        }

        [AvaloniaFact]
        public async Task RecipeMenu_PasteOptions_UncheckedField_ApplySkipsEvenACompatibleTarget() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement node = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var viewModel = (IRecipeNodeViewModel)node.ViewModel;
            SetAssemblerModuleCount(fx, viewModel, "speed-module", 1);
            string clipboardText = GraphSaveCodec.WriteNodeCopyOptionsToString(new NodeCopyOptions(viewModel).ToSaveDocument());
            ControllerFor(fx, viewModel).SetAssemblerModules([], filterModules: false);
            fx.Control.Viewer.Context.GetClipboardText = () => clipboardText;
            fx.Control.Viewer.SetSelection([node]);

            var entries = node.BuildRightClickMenu();
            entries.Single(e => e.Caption == "Modules").Checkbox!.Checked = false;
            entries.Single(e => e.Caption == "Paste selected options").Invoke!.Invoke();

            Assert.Empty(viewModel.AssemblerModules);
        }

        [AvaloniaFact]
        public async Task RecipeMenu_PasteOptions_RememberedCheckboxState_PersistsAcrossTwoMenuBuilds() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement node = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            var viewModel = (IRecipeNodeViewModel)node.ViewModel;
            string clipboardText = GraphSaveCodec.WriteNodeCopyOptionsToString(new NodeCopyOptions(viewModel).ToSaveDocument());
            fx.Control.Viewer.Context.GetClipboardText = () => clipboardText;
            string assemblerCaption = viewModel.SelectedAssembler.Assembler.GetEntityTypeName(false);

            var firstEntries = node.BuildRightClickMenu();
            MenuEntry firstCheckbox = firstEntries.Single(e => e.Caption == assemblerCaption);
            bool flipped = !firstCheckbox.Checkbox!.Checked;
            firstCheckbox.Checkbox!.Checked = flipped;
            firstEntries.Single(e => e.Caption == "Paste selected options").Invoke!.Invoke();

            var secondEntries = node.BuildRightClickMenu();
            MenuEntry secondCheckbox = secondEntries.Single(e => e.Caption == assemblerCaption);

            Assert.Equal(flipped, secondCheckbox.Checkbox!.Checked);
        }

        //---- keyboard routing (reference §7, Cmd replaces Ctrl per the Cmd-mapping note) ----

        [AvaloniaFact]
        public async Task CmdC_CopiesSelectionFragmentToClipboard() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement node = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);
            string? written = null;
            fx.Control.Viewer.Context.SetClipboardText = text => written = text;
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Meta);

            Assert.NotNull(written);
            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadGraphPayload(written!);
            Assert.NotNull(document);
            Assert.Single(document.Nodes);
            Assert.Single(fx.Control.Viewer.NodeElements); //Cmd+C doesn't delete
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): Ctrl+C on
        //Linux does the same thing Cmd+C does on macOS, via the UseIsMacOs seam.
        [AvaloniaFact]
        public async Task CtrlC_OnLinux_CopiesSelectionFragmentToClipboard() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement node = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);
            string? written = null;
            fx.Control.Viewer.Context.SetClipboardText = text => written = text;
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);

            Assert.NotNull(written);
            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadGraphPayload(written!);
            Assert.NotNull(document);
            Assert.Single(document.Nodes);
        }

        [AvaloniaFact]
        public async Task CmdX_CutsSelectionAndWritesTheFragmentToClipboard() {
            Fixture fx = NewFixture(await GetCacheAsync());
            RecipeNodeElement node = AddRecipeNode(fx, "iron-gear-wheel", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);
            string? written = null;
            fx.Control.Viewer.Context.SetClipboardText = text => written = text;
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Meta);

            Assert.NotNull(written);
            Assert.Empty(fx.Control.Viewer.NodeElements);
        }

        [AvaloniaFact]
        public async Task CmdV_PastesClipboardFragmentAtTheCursor() {
            DataCache cache = await GetCacheAsync();
            Fixture source = NewFixture(cache);
            RecipeNodeElement node = AddRecipeNode(source, "iron-gear-wheel", Point.Empty);
            source.Control.Viewer.SetSelection([node]);
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture(cache);
            target.Control.Viewer.Context.GetClipboardText = () => fragmentJson;
            target.Control.Focus();
            var cursorScreenPoint = new AvaloniaPoint(Half + 60, Half - 40);
            target.Window.MouseMove(cursorScreenPoint, RawInputModifiers.None);

            target.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Meta);

            Assert.Single(target.Control.Viewer.NodeElements);
        }
    }
}
