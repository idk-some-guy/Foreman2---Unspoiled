using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class NodeRenderTests {
        private const int Half = 200;

        //Reference §4 base-fill ARGB table (cross-checked against each subclass's CleanBgBrush in upstream source).
        private static readonly SKColor SupplierBg = new(231, 214, 224);
        private static readonly SKColor ConsumerBg = new(249, 237, 195);
        private static readonly SKColor RecipeGreenBg = new(190, 217, 212); //shared by Spoil/Plant/Recipe
        private static readonly SKColor PassthroughBg = new(200, 200, 200);
        private static readonly SKColor ErrorBg = new(255, 127, 80); //Brushes.Coral
        private static readonly SKColor EqualFlowBorder = new(0, 100, 0); //Brushes.DarkGreen
        private static readonly SKColor OverproducingBorder = new(184, 134, 11); //Brushes.DarkGoldenrod
        private static readonly SKColor UndersuppliedBorder = new(139, 0, 0); //Brushes.DarkRed

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

        [Fact]
        public void SupplierNodeElement_Clean_CenterShowsBaseFillAndDarkGreenBorder() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            //(0,-35) sits inside the node's own background fill, clear of the title/text slots and of the
            //not-yet-repositioned tab that still sits at the node's raw (0,0) origin (tab ordering is a
            //PrePaint side effect - see Prime()).
            Assert.Equal(SupplierBg, SamplePixel(surface, 0, -35));
            Assert.Equal(EqualFlowBorder, SamplePixel(surface, -70, 0));
        }

        //Phase 7 task 2 (perf-packaging-reference.md §1b): pins the selection-overlay tint's exact blended
        //pixel so converting its Fill(SelectionOverlayColor) call site to a cached static paint can't drift it.
        [Fact]
        public void SupplierNodeElement_Highlighted_TintsBaseFillWithSelectionOverlay() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel) { Highlighted = true };

            using SKSurface surface = Render(element);

            Assert.Equal(new SKColor(180, 169, 215), SamplePixel(surface, 0, -35));
        }

        [Fact]
        public void SupplierNodeElement_ItemMissing_FillsCoralError() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("ghost-item", isMissing: true);
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(ErrorBg, SamplePixel(surface, 0, -35));
        }

        [Fact]
        public void SupplierNodeElement_ItemDisabled_ShowsWarningCornerFlagButKeepsCleanFill() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("copper-ore");
            item.Enabled = false;
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(SupplierBg, SamplePixel(surface, 0, -35));
            Assert.Equal(ErrorBg, SamplePixel(surface, -64, -40));
        }

        [Fact]
        public void SupplierNodeElement_ManualNoOutputLinks_BorderIsOverproducingGoldenrod() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("plastic-bar");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController controller = fx.Graph.RequestNodeController(node!)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(100);
            var element = new SupplierNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(OverproducingBorder, SamplePixel(surface, -70, 0));
        }

        //---- base fill: Consumer/Spoil/Plant/Passthrough ----

        [Fact]
        public void ConsumerNodeElement_Clean_CenterShowsBaseFill() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("plate");
            IConsumerNodeViewModel viewModel = MakeConsumer(fx, fx.Pair(item));
            var element = new ConsumerNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(ConsumerBg, SamplePixel(surface, 0, 35));
        }

        [Fact]
        public void ConsumerNodeElement_ManualRateNotMet_BorderIsUndersuppliedDarkRed() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("plate");
            IConsumerNodeViewModel viewModel = MakeConsumer(fx, fx.Pair(item));
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController controller = fx.Graph.RequestNodeController(node!)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(50);
            var element = new ConsumerNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(UndersuppliedBorder, SamplePixel(surface, -70, 0));
        }

        [Fact]
        public void SpoilNodeElement_Clean_CenterSharesRecipeGreenBaseFill() {
            Fixture fx = NewFixture();
            ISpoilNodeViewModel viewModel = MakeSpoil(fx, "fish", "spoiled-fish");
            var element = new SpoilNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(RecipeGreenBg, SamplePixel(surface, 0, 0));
        }

        [Fact]
        public void PlantNodeElement_Clean_CenterSharesRecipeGreenBaseFill() {
            Fixture fx = NewFixture();
            IPlantNodeViewModel viewModel = MakePlant(fx, "seed", "wheat");
            var element = new PlantNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(RecipeGreenBg, SamplePixel(surface, 0, 0));
        }

        [Fact]
        public void PassthroughNodeElement_NotSimpleDraw_CenterShowsPassthroughGray() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("coal");
            IPassthroughNodeViewModel viewModel = MakePassthrough(fx, fx.Pair(item));
            var element = new PassthroughNodeElement(fx.Context, viewModel);

            using SKSurface surface = Render(element);

            Assert.Equal(PassthroughBg, SamplePixel(surface, 0, 35));
        }

        //---- passthrough simple-draw mode ----

        [Fact]
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

        //Phase 7 task 2 (perf-packaging-reference.md §1b): pins the simple-draw highlight stroke's exact
        //blended pixel so converting its per-frame `new SKPaint` at PassthroughNodeElement.cs:71 to a cached,
        //mutate-reset per-instance paint can't drift it.
        [Fact]
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

        //---- tab position ----

        [Fact]
        public void SupplierNodeElement_SingleOutputTab_CentersAtComputedTopEdgePosition() {
            Fixture fx = NewFixture();
            //ArrowsOnLinks true keeps the tab a plain filled box (ItemTabElement draws a direction wedge
            //there instead when it's off) - orthogonal to what this test checks, so pin it explicitly rather
            //than ride whatever NodeElementContext's own default happens to be.
            fx.Context.ArrowsOnLinks = true;
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);

            using SKSurface surface = Render(element);

            //Tab width 41 -> half node width formula centers a lone tab at graph x=0; NodeDirection.Up
            //(ProductionGraph's default) places output tabs near the top edge, y=-47, with the tab's own
            //top edge at y=-71. The flow-rate text now sits inset 2px from that edge (TextEdgeInset, so it
            //clears the border stroke) and runs local y=[-22,-16] i.e. graph y=[-69,-63]; the icon starts at
            //local y=-11 (graph y=-58). y=-60 sits in the gap between the two - clear of text ink, border,
            //and the icon - so it should show the tab's white fill rather than the node's own background.
            Assert.Equal(SKColors.White, SamplePixel(surface, 0, -60));
        }

        //---- tab text vs box-edge clipping ----

        //The border stroke centers on the tab's own Bounds edge (StrokeWidth = Border = 3), bleeding
        //~1.5px inward. Glyph ink anchored flush against that same edge collides with it, which reads as
        //the numbers clipping against the tab. A thin strip just inside the edge, away from the rounded
        //corners, should show only background or the border's flat mid-gray (105,105,105) - never a
        //near-black text pixel.
        private static bool HasGlyphInkInRow(SKSurface surface, int y, int xFrom, int xTo) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            for (int x = xFrom; x < xTo; x++) {
                SKColor c = pixmap.GetPixelColor(Half + x, Half + y);
                if (c.Red < 80 && c.Green < 80 && c.Blue < 80)
                    return true;
            }
            return false;
        }

        [Fact]
        public void ItemTabElement_WideOutputFlowRateText_InkStaysClearOfTopBorder() {
            Fixture fx = NewFixture();
            fx.Context.ArrowsOnLinks = true;
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var node = new SupplierNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);
            var tab = new ItemTabElement(fx.Pair(item), LinkType.Output, fx.Context, node);
            tab.UpdateValues(4404.8, 4404.8, false);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Translate(Half, Half);
            tab.PrePaint();
            tab.Paint(surface.Canvas, NodeDrawingStyle.Regular);

            Rectangle bounds = tab.Bounds;
            Assert.False(HasGlyphInkInRow(surface, bounds.Top, bounds.Left + 4, bounds.Right - 4));
            Assert.False(HasGlyphInkInRow(surface, bounds.Top + 1, bounds.Left + 4, bounds.Right - 4));
        }

        [Fact]
        public void ItemTabElement_WideInputFlowRateText_InkStaysClearOfBottomBorder() {
            Fixture fx = NewFixture();
            fx.Context.ArrowsOnLinks = true;
            ItemPrototype item = fx.NewItem("iron-ore");
            IConsumerNodeViewModel viewModel = MakeConsumer(fx, fx.Pair(item));
            var node = new ConsumerNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);
            var tab = new ItemTabElement(fx.Pair(item), LinkType.Input, fx.Context, node);
            tab.UpdateValues(4404.8, 0, false);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Translate(Half, Half);
            tab.PrePaint();
            tab.Paint(surface.Canvas, NodeDrawingStyle.Regular);

            Rectangle bounds = tab.Bounds;
            Assert.False(HasGlyphInkInRow(surface, bounds.Bottom - 1, bounds.Left + 4, bounds.Right - 4));
            Assert.False(HasGlyphInkInRow(surface, bounds.Bottom - 2, bounds.Left + 4, bounds.Right - 4));
        }

        //---- LOD ----

        //RegionHasPixelUnlike/RegionDiffers mirror RecipeNodeRenderTests.cs's helper - narrowing a
        //hides/shows assertion to the exact region the LOD switch touches, rather than scanning the whole
        //canvas for "any pixel differs" (which passes even on an unrelated, incidental change elsewhere).
        private static bool RegionDiffers(SKSurface a, SKSurface b, Rectangle graphRegion) {
            using SKImage imageA = a.Snapshot();
            using SKImage imageB = b.Snapshot();
            using SKPixmap pixelsA = imageA.PeekPixels();
            using SKPixmap pixelsB = imageB.PeekPixels();
            for (int x = graphRegion.Left; x < graphRegion.Right; x++)
                for (int y = graphRegion.Top; y < graphRegion.Bottom; y++)
                    if (pixelsA.GetPixelColor(Half + x, Half + y) != pixelsB.GetPixelColor(Half + x, Half + y))
                        return true;
            return false;
        }

        [Fact]
        public void SpoilNodeElement_LevelOfDetailLowVsMedium_LabelRegionRendersDifferentContent() {
            Fixture fx = NewFixture();
            ISpoilNodeViewModel viewModel = MakeSpoil(fx, "fish", "spoiled-fish");

            fx.Context.LevelOfDetail = LevelOfDetail.Low;
            var lowElement = new SpoilNodeElement(fx.Context, viewModel);
            using SKSurface lowSurface = Render(lowElement);

            fx.Context.LevelOfDetail = LevelOfDetail.Medium;
            var mediumElement = new SpoilNodeElement(fx.Context, viewModel);
            using SKSurface mediumSurface = Render(mediumElement);

            //DetailsDraw's textSlot at trans=(0,0), overproducing=false: Rectangle(-Width/2+40, -Height/2+27,
            //Width-50, Height-54) = Rectangle(-32, -21, 94, 42) for SpoilNodeElement's fixed MinWidth/
            //BaseSimpleHeight (144, 96) - Low draws "<input> Spoilage", Medium/High draw "<qty> stacks" into
            //this same box, so a real content difference must land inside it rather than anywhere on canvas.
            var labelRegion = new Rectangle(-32, -21, 94, 42);
            Assert.True(RegionDiffers(lowSurface, mediumSurface, labelRegion));
        }

        //Low-hides/Medium-shows at a named coordinate: RecipeNodeElement is the node type that actually
        //hides chrome at LOD Low (AssemblerElement/BeaconElement), covered end to end in
        //RecipeNodeRenderTests.cs (RecipeNodeElement_LevelOfDetailLow_HidesAssemblerAndBeaconChildren +
        //AssemblerElement_RealRecipe_DrawsAssemblerIconAtLODMedium) since it needs a real assembler
        //prototype this file's synthetic single-item DataCache doesn't build.

        //---- GetToolTips cascade + ContainsPoint ----

        [Fact]
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

        [Fact]
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

        [Fact]
        public void ContainsPoint_PointOnProtrudingOutputTab_ResolvesTrue() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            ISupplierNodeViewModel viewModel = MakeSupplier(fx, fx.Pair(item));
            var element = new SupplierNodeElement(fx.Context, viewModel);
            Prime(fx, viewModel);
            element.PrePaint();

            //Node's own Bounds top edge is at y=-48; the output tab centered at y=-47 extends up to
            //roughly y=-71, well outside the base rectangle.
            Assert.True(element.ContainsPoint(new Point(0, -65)));
        }
    }
}
