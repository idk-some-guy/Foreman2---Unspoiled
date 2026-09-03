using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises RecipeNodeElement/AssemblerElement/BeaconElement against the real bundled vanilla preset
    //(iron-gear-wheel: a plain assembler recipe with no fluids, so any assembler tier can craft it), rather
    //than the synthetic single-item DataCache NodeRenderTests builds - assembler/module/beacon icons need
    //real bitmap content behind them for the icon-presence assertions below to mean anything.
    public class RecipeNodeRenderTests {
        private const int Half = 250;
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static readonly SKColor RecipeGreenBg = new(190, 217, 212);
        private static readonly SKColor EqualFlowBorder = new(0, 100, 0);
        private static readonly SKColor CanvasBackground = SKColors.White;

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
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required NodeElementContext Context { get; init; }
        }

        private static Fixture NewFixture(DataCache cache) {
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            var context = new NodeElementContext(session.View, new Viewport(2 * Half, 2 * Half));
            return new Fixture { Cache = cache, Graph = graph, Session = session, Context = context };
        }

        private static IRecipeNodeViewModel MakeIronGearWheelNode(Fixture fx) {
            IRecipe recipe = fx.Cache.Recipes["iron-gear-wheel"];
            var pair = new RecipeQualityPair(recipe, fx.Cache.DefaultQuality!);
            fx.Graph.CreateRecipeNode(pair, Point.Empty); //auto-selects assembler + default modules
            return fx.Session.View.Nodes.OfType<IRecipeNodeViewModel>().Single();
        }

        private static RecipeNodeController ControllerFor(Fixture fx, IRecipeNodeViewModel viewModel) {
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            return (RecipeNodeController)fx.Graph.RequestNodeController(node!)!;
        }

        private static void SetAssemblerModuleCount(Fixture fx, IRecipeNodeViewModel viewModel, string moduleName, int count) {
            IModule module = fx.Cache.Modules[moduleName];
            var modules = Enumerable.Repeat(new ModuleQualityPair(module, fx.Cache.DefaultQuality!), count);
            ControllerFor(fx, viewModel).SetAssemblerModules(modules, filterModules: false);
        }

        private static void SetBeaconWithModules(Fixture fx, IRecipeNodeViewModel viewModel, int moduleCount, double beaconCount, double beaconsConst = 0) {
            IBeacon beacon = fx.Cache.Beacons["beacon"];
            RecipeNodeController controller = ControllerFor(fx, viewModel);
            controller.SetBeacon(new BeaconQualityPair(beacon, fx.Cache.DefaultQuality!));
            controller.SetBeaconCount(beaconCount);
            controller.SetBeaconsCont(beaconsConst);

            IModule module = fx.Cache.Modules["speed-module"];
            var modules = Enumerable.Repeat(new ModuleQualityPair(module, fx.Cache.DefaultQuality!), moduleCount);
            controller.SetBeaconModules(modules, filterModules: false);
        }

        //---- render helpers (independent literal constants, matching NodeRenderTests.cs's discipline) ----

        private static SKSurface Render(RecipeNodeElement element, NodeDrawingStyle style = NodeDrawingStyle.Regular) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(CanvasBackground);
            surface.Canvas.Translate(Half, Half);
            element.PrePaint();
            element.Paint(surface.Canvas, style);
            return surface;
        }

        private static SKColor SamplePixel(SKSurface surface, int graphX, int graphY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(Half + graphX, Half + graphY);
        }

        private static bool RegionHasPixelUnlike(SKSurface surface, Rectangle graphRegion, params SKColor[] backgroundColors) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            for (int x = graphRegion.Left; x < graphRegion.Right; x++) {
                for (int y = graphRegion.Top; y < graphRegion.Bottom; y++) {
                    SKColor pixel = pixmap.GetPixelColor(Half + x, Half + y);
                    if (!backgroundColors.Contains(pixel))
                        return true;
                }
            }
            return false;
        }

        //---- base fill + border ----

        [Fact]
        public async Task RecipeNodeElement_Clean_CenterShowsRecipeGreenFillAndDarkGreenBorder() {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            var element = new RecipeNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            //(50, 0) sits inside the node's own background fill: clear of the input/output tabs (centered
            //near x=0) and clear of the assembler/beacon children (both anchored left-of-center). Node width
            //is MinWidth (144) for this recipe's one input + one output tab, so the border sample 2px in from
            //the left edge matches the same -70 offset NodeRenderTests.cs uses for other MinWidth nodes.
            Assert.Equal(RecipeGreenBg, SamplePixel(surface, 50, 0));
            Assert.Equal(EqualFlowBorder, SamplePixel(surface, -70, 0));
        }

        //---- assembler + beacon icon presence ----

        [Fact]
        public async Task AssemblerElement_RealRecipe_DrawsAssemblerIconAtLODMedium() {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            var element = new RecipeNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            //AssemblerElement draws its 54px icon starting 26px right of its own local origin (moduleSpacing*2+2);
            //LocalToGraph resolves through the real parent chain regardless of where RecipeNodeElement.UpdateState
            //positioned the child, so this doesn't need to re-derive that layout math by hand.
            Point trans = element.AssemblerElement.LocalToGraph(new Point(-element.AssemblerElement.Width / 2, -element.AssemblerElement.Height / 2));
            var iconRegion = new Rectangle(trans.X + 26, trans.Y, 54, 54);

            Assert.True(RegionHasPixelUnlike(surface, iconRegion, CanvasBackground, RecipeGreenBg));
        }

        [Fact]
        public async Task BeaconElement_SelectedBeacon_DrawsIconAndCountTextAtLODMedium() {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            SetBeaconWithModules(fx, viewModel, moduleCount: 2, beaconCount: 3);
            var element = new RecipeNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Point trans = element.BeaconElement.LocalToGraph(new Point(-element.BeaconElement.Width / 2, -element.BeaconElement.Height / 2));
            var iconRegion = new Rectangle(trans.X + 10 + 33, trans.Y, 28, 28);
            var textRegion = new Rectangle(trans.X + element.BeaconElement.Width, trans.Y, 60, 18);

            Assert.Equal(3, viewModel.BeaconCount);
            Assert.True(RegionHasPixelUnlike(surface, iconRegion, CanvasBackground, RecipeGreenBg));
            Assert.True(RegionHasPixelUnlike(surface, textRegion, CanvasBackground, RecipeGreenBg));
        }

        //---- LOD High stat readout ----

        [Fact]
        public async Task AssemblerElement_LevelOfDetailHigh_StatReadoutRegionNonEmpty() {
            //Auto-selected assembler for a plain crafting recipe is always EntityType.Assembler, the readout's
            //gating condition alongside Miner/OffshorePump.
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            fx.Context.LevelOfDetail = LevelOfDetail.High;
            var element = new RecipeNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            //Speed:/Prod:/Power: labels start right at the assembler's own right edge (trans.X + Width); a wide
            //enough window catches the whole stat block without needing to separate it pixel-for-pixel from the
            //assembler count text next to it - the point here is only that the readout drew something.
            Point trans = element.AssemblerElement.LocalToGraph(new Point(-element.AssemblerElement.Width / 2, -element.AssemblerElement.Height / 2));
            var readoutRegion = new Rectangle(trans.X + element.AssemblerElement.Width, trans.Y, 130, 50);

            Assert.True(RegionHasPixelUnlike(surface, readoutRegion, CanvasBackground, RecipeGreenBg));
        }

        [Fact]
        public async Task RecipeNodeElement_LevelOfDetailMediumVsHigh_RendersDifferentContent() {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);

            fx.Context.LevelOfDetail = LevelOfDetail.Medium;
            var mediumElement = new RecipeNodeElement(fx.Context, viewModel);
            using SKSurface mediumSurface = Render(mediumElement);

            fx.Context.LevelOfDetail = LevelOfDetail.High;
            var highElement = new RecipeNodeElement(fx.Context, viewModel);
            using SKSurface highSurface = Render(highElement);

            using SKImage mediumImage = mediumSurface.Snapshot();
            using SKImage highImage = highSurface.Snapshot();
            using SKPixmap mediumPixels = mediumImage.PeekPixels();
            using SKPixmap highPixels = highImage.PeekPixels();

            bool anyPixelDiffers = false;
            for (int x = 0; x < 2 * Half && !anyPixelDiffers; x++)
                for (int y = 0; y < 2 * Half; y++)
                    if (mediumPixels.GetPixelColor(x, y) != highPixels.GetPixelColor(x, y)) {
                        anyPixelDiffers = true;
                        break;
                    }

            Assert.True(anyPixelDiffers);
        }

        //---- module density-tier boundaries (behavior, not pixels) ----

        [Theory]
        [InlineData(6, ModuleDisplayTier.Icons)]
        [InlineData(7, ModuleDisplayTier.Dots)]
        [InlineData(28, ModuleDisplayTier.Dots)]
        [InlineData(29, ModuleDisplayTier.Tally)]
        public async Task AssemblerElement_ModuleCount_SelectsExpectedDensityTier(int moduleCount, ModuleDisplayTier expectedTier) {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            SetAssemblerModuleCount(fx, viewModel, "speed-module", moduleCount);
            var element = new RecipeNodeElement(fx.Context, viewModel);

            Assert.Equal(expectedTier, element.AssemblerElement.ModuleTier);
        }

        [Theory]
        [InlineData(6, ModuleDisplayTier.Icons)]
        [InlineData(7, ModuleDisplayTier.Dots)]
        [InlineData(32, ModuleDisplayTier.Dots)]
        [InlineData(33, ModuleDisplayTier.Tally)]
        public async Task BeaconElement_ModuleCount_SelectsExpectedDensityTier(int moduleCount, ModuleDisplayTier expectedTier) {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            SetBeaconWithModules(fx, viewModel, moduleCount, beaconCount: 1);
            var element = new RecipeNodeElement(fx.Context, viewModel);

            Assert.Equal(expectedTier, element.BeaconElement.ModuleTier);
        }

        //---- LOD Low hides assembler/beacon entirely ----

        [Fact]
        public async Task RecipeNodeElement_LevelOfDetailLow_HidesAssemblerAndBeaconChildren() {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            fx.Context.LevelOfDetail = LevelOfDetail.Low;

            var element = new RecipeNodeElement(fx.Context, viewModel);

            Assert.False(element.AssemblerElement.Visible);
            Assert.False(element.BeaconElement.Visible);
        }

        //---- EnableExtraProductivityForNonMiners (final-review finding 5) ----

        [Fact]
        public async Task LoadDocument_ExtraProdForNonMinersOn_RecipeNodeDrawsExtraProductivityTickAtLODLow() {
            DataCache cache = await GetCacheAsync();
            Fixture sourceFx = NewFixture(cache);
            IRecipeNodeViewModel sourceViewModel = MakeIronGearWheelNode(sourceFx);
            ControllerFor(sourceFx, sourceViewModel).SetExtraProductivityBonus(0.5);

            var saveDocument = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(sourceFx.Graph),
                Ui = new GraphViewerUiSaveData { ExtraProdForNonMiners = true, ViewScale = 1f },
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(saveDocument);

            var viewer = new GraphViewer(new Viewport(2 * Half, 2 * Half), new GridManager());
            viewer.Context.LevelOfDetail = LevelOfDetail.Low;
            GraphLoadResult result = viewer.LoadDocument(cache, json);
            Assert.True(result.Success, result.ErrorMessage);

            var element = (RecipeNodeElement)viewer.NodeElements.Single();
            using SKSurface surface = Render(element);

            //LOD-Low's extra-productivity tick is the first module dot, drawn at the node's own left edge
            //(RecipeNodeElement.DetailsDraw's i==0 slot): SKRect.Create(trans.X - Width/2 - 1, trans.Y -
            //Height/2 + 10, 6, 6). The iron-gear-wheel assembler is EntityType.Assembler, not Miner, so this
            //only draws when Context.EnableExtraProductivityForNonMiners actually reaches the element -
            //the write LoadDocument was missing.
            Point trans = element.LocalToGraph(new Point(0, 0));
            var tickRegion = new Rectangle(trans.X - (element.Width / 2) - 1, trans.Y - (element.Height / 2) + 10, 6, 6);
            Assert.True(RegionHasPixelUnlike(surface, tickRegion, CanvasBackground, RecipeGreenBg));
        }

        //---- productivity-module dots at LOD Low (upstream RecipeNodeElement.cs:102-117) ----

        //Matches RecipeNodeElement.ProductivityPaint (new SKColor(139, 0, 0)) exactly: the dot is drawn last,
        //on top of the border/background fills already there, so its center pixel is the pure paint color
        //rather than a blend - sampling border-adjacent pixels here (the dot sits 1px left of the node's own
        //left border) would pass "unlike background" even with no dot at all, since it overlaps the border fill.
        private static readonly SKColor ProductivityDotColor = new(139, 0, 0);

        private static SKColor DotCenterPixel(SKSurface surface, RecipeNodeElement element, int dotIndex) {
            Point trans = element.LocalToGraph(new Point(0, 0));
            int x = trans.X - (element.Width / 2) - 1 + 3;
            int y = trans.Y - (element.Height / 2) + 10 + (dotIndex * 12) + 3;
            return SamplePixel(surface, x, y);
        }

        [Fact]
        public async Task RecipeNodeElement_ProductivityModulesAtLODLow_DrawsOneDotPerModule() {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            SetAssemblerModuleCount(fx, viewModel, "productivity-module", 2);
            fx.Context.LevelOfDetail = LevelOfDetail.Low;
            var element = new RecipeNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            //RecipeNodeElement.DetailsDraw's LOD-Low branch: one 6x6 dot per productivity module, stacked
            //12px apart down the node's left edge starting at trans.Y - Height/2 + 10 (i == 0, 1, ...).
            Assert.Equal(ProductivityDotColor, DotCenterPixel(surface, element, dotIndex: 0));
            Assert.Equal(ProductivityDotColor, DotCenterPixel(surface, element, dotIndex: 1));
        }

        [Fact]
        public async Task RecipeNodeElement_NoProductivityModulesAtLODLow_DrawsNoDot() {
            Fixture fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            ControllerFor(fx, viewModel).RemoveAssemblerModules(); //auto-selected default modules may include productivity ones
            fx.Context.LevelOfDetail = LevelOfDetail.Low;
            var element = new RecipeNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.NotEqual(ProductivityDotColor, DotCenterPixel(surface, element, dotIndex: 0));
        }

        //---- reference render: recipe node at all three LODs, for the SDD workspace ----

        [Fact]
        public async Task RenderRecipeNodeAtEachLevelOfDetail_WritesReferencePngsToSddWorkspace() {
            DataCache cache = await GetCacheAsync();
            string workspaceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".superpowers", "sdd", "2026-09-01-phase3-canvas-readonly");
            Directory.CreateDirectory(workspaceDir);

            foreach ((LevelOfDetail lod, string suffix) in new[] { (LevelOfDetail.Low, "low"), (LevelOfDetail.Medium, "medium"), (LevelOfDetail.High, "high") }) {
                Fixture fx = NewFixture(cache);
                IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
                SetBeaconWithModules(fx, viewModel, moduleCount: 2, beaconCount: 2);
                fx.Context.LevelOfDetail = lod;
                var element = new RecipeNodeElement(fx.Context, viewModel);

                using SKSurface surface = Render(element);
                using SKImage image = surface.Snapshot();
                using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream file = File.OpenWrite(Path.Combine(workspaceDir, $"task-3-recipe-node-{suffix}.png"));
                data.SaveTo(file);
            }

            Assert.True(File.Exists(Path.Combine(workspaceDir, "task-3-recipe-node-high.png")));
        }
    }
}
