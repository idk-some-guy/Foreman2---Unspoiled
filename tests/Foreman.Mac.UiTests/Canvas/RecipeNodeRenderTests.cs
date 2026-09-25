using Avalonia.Headless.XUnit;
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

        //---- reference render: recipe node at all three LODs, for the SDD workspace ----

        [AvaloniaFact]
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
                using FileStream file = File.OpenWrite(Path.Combine(workspaceDir, $"recipe-node-{suffix}.png"));
                data.SaveTo(file);
            }

            Assert.True(File.Exists(Path.Combine(workspaceDir, "recipe-node-high.png")));
        }
    }
}
