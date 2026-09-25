using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class NodeRenderTests {
        private const int Half = 200;

        private static readonly SKColor ErrorBg = new(255, 127, 80); //Brushes.Coral

        //---- fixture plumbing: builds a minimal DataCache + graph + session, mirroring
        //ForemanTest/Graph/GraphSessionTestHelper's idiom without depending on that test project. Only the
        //DataCache._store field needs reflection; DataCacheStore itself is internal and reachable directly
        //via Foreman.Core's InternalsVisibleTo grant to this assembly.

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required NodeElementContext Context { get; init; }

            public ItemPrototype NewItem(string name, bool isMissing = false) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", isMissing);
                Store(Cache).Items[name] = item;
                return item;
            }

            public ItemQualityPair Pair(IItem item) => new(item, Quality);
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static Fixture NewFixture() {
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var graph = new ProductionGraph { DefaultAssemblerQuality = quality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            var context = new NodeElementContext(session.View, new Viewport(2 * Half, 2 * Half));

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Graph = graph, Session = session, Context = context };
        }

        //Forces an already-constructed element's deferred UpdateState/UpdateValues to run on the next
        //PrePaint by firing a real NodeStateChanged event (KeyNode toggled true then false leaves geometry
        //at its default-assumed state while still tripping the dirty flag) - matches how upstream defers
        //state recomputation from "on change" to "on next paint" (reference §3 point 6).
        private static void Prime(Fixture fx, INodeViewModel viewModel) {
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController? controller = node is null ? null : fx.Graph.RequestNodeController(node);
            controller?.SetKeyNode(true);
            controller?.SetKeyNode(false);
        }

        private static SKSurface Render(BaseNodeElement element, NodeDrawingStyle style = NodeDrawingStyle.Regular) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.White);
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

        //---- node builders ----

        private static ISupplierNodeViewModel MakeSupplier(Fixture fx, ItemQualityPair item) {
            fx.Graph.CreateSupplierNode(item, Point.Empty);
            return fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
        }

        private static IConsumerNodeViewModel MakeConsumer(Fixture fx, ItemQualityPair item) {
            fx.Graph.CreateConsumerNode(item, Point.Empty);
            return fx.Session.View.Nodes.OfType<IConsumerNodeViewModel>().Single();
        }

        private static ISpoilNodeViewModel MakeSpoil(Fixture fx, string inputName, string outputName) {
            ItemPrototype input = fx.NewItem(inputName);
            ItemPrototype output = fx.NewItem(outputName);
            input.SpoilResult = output;
            fx.Graph.CreateSpoilNode(fx.Pair(input), output, Point.Empty);
            return fx.Session.View.Nodes.OfType<ISpoilNodeViewModel>().Single();
        }

        private static IPlantNodeViewModel MakePlant(Fixture fx, string seedName, params string[] productNames) {
            ItemPrototype seed = fx.NewItem(seedName);
            var process = new PlantProcessPrototype(fx.Cache, "plant-" + seedName);
            foreach (string productName in productNames)
                process.InternalOneWayAddProduct(fx.NewItem(productName), 1);
            process.Seed = seed;
            seed.PlantResult = process;
            fx.Graph.CreatePlantNode(process, fx.Quality, Point.Empty);
            return fx.Session.View.Nodes.OfType<IPlantNodeViewModel>().Single();
        }

        private static IPassthroughNodeViewModel MakePassthrough(Fixture fx, ItemQualityPair item) {
            fx.Graph.CreatePassthroughNode(item, Point.Empty);
            return fx.Session.View.Nodes.OfType<IPassthroughNodeViewModel>().Single();
        }

        //---- base fill + border color: SupplierNodeElement ----

        //perf-packaging-reference.md §1b: pins the selection-overlay tint's exact blended
        //pixel so converting its Fill(SelectionOverlayColor) call site to a cached static paint can't drift it.
        //The overlay is (100,100,200) at alpha 100 over the card fill.
        private static int Blend(byte under, int over) => (int)Math.Round(((under * 155) + (over * 100)) / 255.0);

        [AvaloniaFact]
        public void SupplierNodeElement_Highlighted_TintsBaseFillWithSelectionOverlay() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel) { Highlighted = true };

            using SKSurface surface = Render(element);

            SKColor card = TokenBridge.Current.ControlBackground;
            SKColor tinted = SamplePixel(surface, 0, -35);
            Assert.InRange(tinted.Red, Blend(card.Red, 100) - 1, Blend(card.Red, 100) + 1);
            Assert.InRange(tinted.Green, Blend(card.Green, 100) - 1, Blend(card.Green, 100) + 1);
            Assert.InRange(tinted.Blue, Blend(card.Blue, 200) - 1, Blend(card.Blue, 200) + 1);
        }

        [AvaloniaFact]
        public void SupplierNodeElement_ItemMissing_FillsCoralError() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("ghost-item", isMissing: true);
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(ErrorBg, SamplePixel(surface, 0, -35));
        }

        [AvaloniaFact]
        public void SupplierNodeElement_ItemDisabled_ShowsWarningCornerFlagButKeepsCleanFill() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("copper-ore");
            item.Enabled = false;
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(TokenBridge.Current.ControlBackground, SamplePixel(surface, 0, -35));
            Assert.Equal(ErrorBg, SamplePixel(surface, -40, -40));
        }

        [AvaloniaFact]
        public void SupplierNodeElement_ManualNoOutputLinks_BorderIsWarning() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("plastic-bar");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController controller = fx.Graph.RequestNodeController(node!)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(100);
            var element = new SupplierNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(TokenBridge.Current.Warning, SamplePixel(surface, -70, 0));
        }

        //---- base fill: Consumer/Spoil/Plant/Passthrough ----

        [AvaloniaFact]
        public void ConsumerNodeElement_ManualRateNotMet_BorderIsError() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("plate");
            IConsumerNodeViewModel viewModel = MakeConsumer(fx, fx.Pair(item));
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController controller = fx.Graph.RequestNodeController(node!)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(50);
            var element = new ConsumerNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(TokenBridge.Current.Error, SamplePixel(surface, -70, 0));
        }

        //---- passthrough simple-draw mode ----

        [AvaloniaFact]
        public void PassthroughNodeElement_SimpleDrawActive_DrawsLineInItemAverageColor() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("coal");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 10, 20, 30)));
            ItemQualityPair pair = fx.Pair(item);

            BaseNode supplier = fx.Graph.CreateSupplierNode(pair, new Point(0, -140));
            BaseNode passthrough = fx.Graph.CreatePassthroughNode(pair, Point.Empty);
            BaseNode consumer = fx.Graph.CreateConsumerNode(pair, new Point(0, 140));
            fx.Graph.CreateLink(supplier, passthrough, pair);
            fx.Graph.CreateLink(passthrough, consumer, pair);
            if (fx.Graph.RequestNodeController(passthrough) is PassthroughNodeController controller)
                controller.SetSimpleDraw(true);

            IPassthroughNodeViewModel viewModel = fx.Session.View.Nodes.OfType<IPassthroughNodeViewModel>().Single();
            var element = new PassthroughNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(new SKColor(10, 20, 30), SamplePixel(surface, 0, 0));
        }

        //perf-packaging-reference.md §1b: pins the simple-draw highlight stroke's exact
        //blended pixel so converting its per-frame `new SKPaint` at PassthroughNodeElement.cs:71 to a cached,
        //mutate-reset per-instance paint can't drift it.
        [AvaloniaFact]
        public void PassthroughNodeElement_SimpleDrawActive_Highlighted_DrawsOverlayStroke() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("coal");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 10, 20, 30)));
            ItemQualityPair pair = fx.Pair(item);

            BaseNode supplier = fx.Graph.CreateSupplierNode(pair, new Point(0, -140));
            BaseNode passthrough = fx.Graph.CreatePassthroughNode(pair, Point.Empty);
            BaseNode consumer = fx.Graph.CreateConsumerNode(pair, new Point(0, 140));
            fx.Graph.CreateLink(supplier, passthrough, pair);
            fx.Graph.CreateLink(passthrough, consumer, pair);
            if (fx.Graph.RequestNodeController(passthrough) is PassthroughNodeController controller)
                controller.SetSimpleDraw(true);

            IPassthroughNodeViewModel viewModel = fx.Session.View.Nodes.OfType<IPassthroughNodeViewModel>().Single();
            var element = new PassthroughNodeElement(fx.Context, viewModel) { Highlighted = true };
            Prime(fx, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(new SKColor(45, 51, 96), SamplePixel(surface, 0, 30));
        }

        //---- GetToolTips cascade + ContainsPoint ----

        [AvaloniaFact]
        public void GetToolTips_HoverOnOutputTab_ReturnsTabTooltipBeforeNodeHelpText() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);
            element.PrePaint();

            var tooltips = element.GetToolTips(new Point(0, -47));

            Assert.NotEmpty(tooltips);
            Assert.Equal("iron-ore", tooltips[0].Text);
        }

        [AvaloniaFact]
        public void GetToolTips_HoverAwayFromTabsAndBadge_ReturnsNodeExclusiveHelpText() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);
            element.PrePaint();

            var tooltips = element.GetToolTips(new Point(0, 0));

            Assert.Single(tooltips);
            Assert.Contains("iron-ore", tooltips[0].Text);
        }

        [AvaloniaFact]
        public void ContainsPoint_PointOnProtrudingOutputTab_ResolvesTrue() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);
            element.PrePaint();

            //The node's top edge is at y=-48; the output tab overhangs it by 13, up to y=-61.
            Assert.True(element.ContainsPoint(new Point(0, -58)));
        }
    }
}
