using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises GraphCanvasControl.HitTest's node/annotation picking precedence (reference §6), the
    //PointerMoved -> InvalidateVisual hover wiring (risk 5), and RecipeNodeElement's ShowRecipeToolTip-gated
    //recipe tooltip against a real vanilla recipe (risk 4, RecipePainter).
    public class HitTestAndTooltipTests {
        private const int Half = 200;

        //---- synthetic fixture (mirrors NodeRenderTests.cs's minimal DataCache/graph/session harness) ----

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required NodeElementContext Context { get; init; }

            public ItemPrototype NewItem(string name) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", false);
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
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            var context = new NodeElementContext(session.View, control.Viewport);

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Graph = graph, Session = session, Control = control, Context = context };
        }

        private static SupplierNodeElement AddSupplier(Fixture fx, string itemName, Point location) {
            ItemPrototype item = fx.NewItem(itemName);
            fx.Graph.CreateSupplierNode(fx.Pair(item), location);
            ISupplierNodeViewModel viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Last();
            var element = new SupplierNodeElement(fx.Context, viewModel);

            //Tab layout is computed lazily by UpdateState on the next PrePaint after a state-change event
            //(reference §3 point 6) rather than in the constructor, matching upstream; prime it here so
            //tests reading tab positions don't need to know that quirk.
            Prime(fx, viewModel);
            element.PrePaint();

            fx.Control.NodeElements.Add(element);
            return element;
        }

        private static void Prime(Fixture fx, INodeViewModel viewModel) {
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController? controller = node is null ? null : fx.Graph.RequestNodeController(node);
            controller?.SetKeyNode(true);
            controller?.SetKeyNode(false);
        }

        //---- hit-test across zoom levels (regression-guards the Viewport-lockstep risk) ----

        [AvaloniaTheory]
        [InlineData(0.5f)]
        [InlineData(1f)]
        [InlineData(2f)]
        public void HitTest_ReturnsPlacedNode_AtComputedScreenPointAcrossZoomLevels(float scale) {
            Fixture fx = NewFixture();
            SupplierNodeElement nodeA = AddSupplier(fx, "iron-ore", new Point(-300, 0));
            SupplierNodeElement nodeB = AddSupplier(fx, "copper-ore", new Point(300, 0));
            fx.Control.Viewport.ViewScale = scale;
            fx.Control.Viewport.UpdateGraphBounds();

            AvaloniaPoint screenA = fx.Control.Viewport.GraphToScreen(nodeA.Location);
            AvaloniaPoint screenB = fx.Control.Viewport.GraphToScreen(nodeB.Location);

            Assert.Same(nodeA, fx.Control.HitTest(screenA));
            Assert.Same(nodeB, fx.Control.HitTest(screenB));
        }

        [AvaloniaFact]
        public void HitTest_PointFarFromEveryElement_ReturnsNull() {
            Fixture fx = NewFixture();
            AddSupplier(fx, "iron-ore", new Point(-300, 0));

            Assert.Null(fx.Control.HitTest(fx.Control.Viewport.GraphToScreen(new Point(5000, 5000))));
        }

        //---- precedence: protruding tab still resolves to its node; nodes win over annotations ----

        [AvaloniaFact]
        public void HitTest_PointOnProtrudingTab_ResolvesToOwningNode() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            ItemTabElement tab = node.SubElements.OfType<ItemTabElement>().Single();
            int outward = tab.Location.Y < 0 ? -1 : 1; //tabs sit flush against the node's top or bottom edge and protrude past it
            Point tabOuterEdge = tab.LocalToGraph(new Point(0, outward * ((tab.Height / 2) - 1)));

            //Sanity: this point sits outside the node's own Bounds rect - otherwise this test wouldn't
            //exercise anything BaseNodeElement.ContainsPoint's plain Bounds check doesn't already cover on
            //its own (reference §6's "a click on a protruding tab still resolves to the node").
            Assert.False(node.Bounds.Contains(node.GraphToLocal(tabOuterEdge)));

            Assert.Same(node, fx.Control.HitTest(fx.Control.Viewport.GraphToScreen(tabOuterEdge)));
        }

        [AvaloniaFact]
        public void HitTest_NodeOverlapsAnnotation_NodeWins() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            fx.Control.Annotations.Add(NewRectangleAnnotation(Point.Empty, 400, 400));

            Assert.Same(node, fx.Control.HitTest(fx.Control.Viewport.GraphToScreen(Point.Empty)));
        }

        [AvaloniaFact]
        public void HitTest_AnnotationAloneUnderPoint_ReturnsAnnotation() {
            Fixture fx = NewFixture();
            AddSupplier(fx, "iron-ore", new Point(5000, 5000)); //far away, never overlaps the probe point
            ShapeAnnotationElement annotation = NewRectangleAnnotation(Point.Empty, 100, 100);
            fx.Control.Annotations.Add(annotation);

            Assert.Same(annotation, fx.Control.HitTest(fx.Control.Viewport.GraphToScreen(Point.Empty)));
        }

        //Task 7 carried requirement: once GraphViewer's real GetPaintingOrder exists, HitTest's reverse
        //iteration over NodeElements must still pick the topmost (last-painted) node on overlap - this
        //drives the nodes through the control's own live Graph/session sync (not manually-added elements
        //like the fixture above) so the regression actually exercises GetPaintingOrder's node segment.
        [AvaloniaFact]
        public void HitTest_TwoOverlappingNodesAddedThroughRealSessionSync_ResolvesToTheOneGetPaintingOrderPaintsLast() {
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            control.Viewer.Graph.DefaultAssemblerQuality = quality;

            var itemA = new ItemPrototype(cache, "iron-ore", "iron-ore", subgroup, "z", false);
            store.Items[itemA.Name] = itemA;
            var itemB = new ItemPrototype(cache, "copper-ore", "copper-ore", subgroup, "z", false);
            store.Items[itemB.Name] = itemB;

            control.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(itemA, quality), Point.Empty);
            control.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(itemB, quality), Point.Empty); //same location - fully overlapping

            Assert.Equal(2, control.NodeElements.Count);
            BaseNodeElement topmost = control.Viewer.GetPaintingOrder().OfType<BaseNodeElement>().Last();
            Assert.Same(control.NodeElements[^1], topmost);

            GraphElement? hit = control.HitTest(control.Viewport.GraphToScreen(Point.Empty));
            Assert.Same(topmost, hit);
        }

        private static ShapeAnnotationElement NewRectangleAnnotation(Point location, int width, int height) =>
            ShapeAnnotationElement.FromSaveData(new ShapeAnnotationSaveData {
                X = location.X, Y = location.Y, Width = width, Height = height,
                ShapeType = "Rectangle", FillColor = new ColorSaveData(255, 1, 2, 3), BorderColor = new ColorSaveData(255, 4, 5, 6), BorderWidth = 1
            });

        //---- pointer-move -> InvalidateVisual wiring (reference risk 5): tooltip is a pure function of the
        //current hover point, recomputed every Render call ----

        [AvaloniaFact]
        public void PointerMove_OverNode_DrawsHoverTooltipBubble_MovingAwayClearsIt() {
            Fixture fx = NewFixture();
            AddSupplier(fx, "iron-ore", Point.Empty);
            var window = new AvaloniaWindow { Content = fx.Control, Width = 2 * Half, Height = 2 * Half };
            window.Show();

            //SupplierNodeElement's only tooltip is the exclusive help text, pinned at the literal screen
            //corner (10, 10) upstream's ExclusiveHelpTooltip always uses - not near the cursor itself.
            window.MouseMove(new AvaloniaPoint(Half, Half));
            Assert.True(BubbleVisibleNear(fx.Control, 10, 10));

            window.MouseMove(new AvaloniaPoint(Half - 150, Half - 150));
            Assert.False(BubbleVisibleNear(fx.Control, 10, 10));
        }

        private static bool BubbleVisibleNear(GraphCanvasControl control, int screenX, int screenY) {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            control.Render(surface.Canvas);
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            var bubbleColor = new SKColor(65, 65, 65);
            for (int dx = -3; dx <= 20; dx++)
                for (int dy = -3; dy <= 20; dy++)
                    if (pixmap.GetPixelColor(screenX + dx, screenY + dy) == bubbleColor)
                        return true;
            return false;
        }

        //---- recipe hover tooltip content + RecipePainter (real vanilla preset - reference risk 4) ----

        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private static readonly SemaphoreSlim RecipeCacheGate = new(1, 1);
        private static DataCache? sharedRecipeCache;

        private static async Task<DataCache> GetRecipeCacheAsync() {
            if (sharedRecipeCache is not null)
                return sharedRecipeCache;
            await RecipeCacheGate.WaitAsync();
            try {
                if (sharedRecipeCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedRecipeCache = cache;
                }
            } finally {
                RecipeCacheGate.Release();
            }
            return sharedRecipeCache;
        }

        private sealed class RecipeFixture {
            public required DataCache Cache { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required NodeElementContext Context { get; init; }
        }

        private static RecipeFixture NewRecipeFixture(DataCache cache, int width = 2 * Half, int height = 2 * Half) {
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(width, height);
            var context = new NodeElementContext(session.View, control.Viewport);
            return new RecipeFixture { Cache = cache, Graph = graph, Session = session, Control = control, Context = context };
        }

        private static IRecipeNodeViewModel MakeIronGearWheelNode(RecipeFixture fx) {
            IRecipe recipe = fx.Cache.Recipes["iron-gear-wheel"];
            var pair = new RecipeQualityPair(recipe, fx.Cache.DefaultQuality!);
            fx.Graph.CreateRecipeNode(pair, Point.Empty);
            return fx.Session.View.Nodes.OfType<IRecipeNodeViewModel>().Single();
        }

        [AvaloniaFact]
        public async Task GetToolTips_ShowRecipeToolTipEnabled_MatchesUpstreamTooltipStructure() {
            RecipeFixture fx = NewRecipeFixture(await GetRecipeCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            fx.Context.ShowRecipeToolTip = true;
            var element = new RecipeNodeElement(fx.Context, viewModel);

            //Far outside every subelement (tabs/assembler/beacon), so the cascade falls through to
            //RecipeNodeElement's own tooltips instead of routing into a child's - GetMyToolTips's gating
            //text otherwise depends on which subelement happened to be under the probe point.
            List<TooltipInfo> tooltips = element.GetToolTips(new Point(5000, 5000));

            Assert.Equal(2, tooltips.Count);

            TooltipInfo recipeTooltip = tooltips[0];
            Assert.Null(recipeTooltip.Text);
            Assert.Equal(Direction.Left, recipeTooltip.Direction);
            Assert.NotNull(recipeTooltip.CustomDraw);
            IRecipe recipe = fx.Cache.Recipes["iron-gear-wheel"];
            Size expectedSize = RecipePainter.GetSize([recipe], fx.Context.AbbreviateSciPacks);
            Assert.Equal(expectedSize.Width, recipeTooltip.ScreenSize!.Value.Width);
            Assert.Equal(expectedSize.Height, recipeTooltip.ScreenSize!.Value.Height);

            string entityName = viewModel.SelectedAssembler.Assembler is IAssembler helpAssembler ? helpAssembler.GetEntityTypeName(false).ToLowerInvariant() : "assembler";
            TooltipInfo helpTooltip = tooltips[1];
            Assert.Equal($"Left click on this node to edit its {entityName}, modules, beacon, etc.\nRight click for options.", helpTooltip.Text);
            Assert.Equal(Direction.None, helpTooltip.Direction);
            Assert.Null(helpTooltip.CustomDraw);
        }

        [AvaloniaFact]
        public async Task GetToolTips_ShowRecipeToolTipDisabled_OmitsRecipeTooltip() {
            RecipeFixture fx = NewRecipeFixture(await GetRecipeCacheAsync());
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            fx.Context.ShowRecipeToolTip = false;
            var element = new RecipeNodeElement(fx.Context, viewModel);

            List<TooltipInfo> tooltips = element.GetToolTips(new Point(5000, 5000));

            TooltipInfo tooltip = Assert.Single(tooltips);
            Assert.NotNull(tooltip.Text);
            Assert.Null(tooltip.CustomDraw);
        }

        [Fact]
        public async Task RecipePainter_Paint_RealVanillaRecipe_DrawsTitleAndIngredientIconRows() {
            DataCache cache = await GetRecipeCacheAsync();
            IRecipe recipe = cache.Recipes["iron-gear-wheel"];
            Size size = RecipePainter.GetSize([recipe], abbreviateSciPacks: true);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height));
            surface.Canvas.Clear(SKColors.White);

            RecipePainter.Paint([recipe], surface.Canvas, Point.Empty, abbreviateSciPacks: true);

            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            SKColor[] backgrounds = [SKColors.White, new SKColor(65, 65, 65), new SKColor(40, 40, 40)];

            Assert.True(RegionHasPixelUnlike(pixmap, new Rectangle(4, 4, 32, 32), backgrounds)); //recipe title icon
            Assert.True(RegionHasPixelUnlike(pixmap, new Rectangle(14, 70, 32, 32), backgrounds)); //first ingredient icon row
        }

        //Pins RecipePainter's literal "0.##" quantity format against GraphicsStuff.DoubleToString's
        //magnitude-branched precision (fewer decimals above 100, none above 10000, a different formatter
        //meant for other UI text) - a fractional or 3+ digit quantity would silently render wrong text if
        //the wrong formatter ever crept back in.
        [Theory]
        [InlineData(0.25, "0.25x")]
        [InlineData(2.5, "2.5x")]
        [InlineData(150, "150x")] //not "150.0x"
        [InlineData(150.55, "150.55x")] //DoubleToString's >=100 branch would round this to "150.6x"
        public void RecipePainter_FormatQuantity_MatchesUpstreamLiteralFormat(double quantity, string expected) {
            Assert.Equal(expected, RecipePainter.FormatQuantity(quantity));
        }

        private static bool RegionHasPixelUnlike(SKPixmap pixmap, Rectangle region, params SKColor[] backgroundColors) {
            for (int x = region.Left; x < region.Right; x++)
                for (int y = region.Top; y < region.Bottom; y++)
                    if (!backgroundColors.Contains(pixmap.GetPixelColor(x, y)))
                        return true;
            return false;
        }

        //---- reference render: a real recipe node hovered, with its full recipe tooltip visible ----

        [AvaloniaFact]
        public async Task RenderRecipeNodeWithHoverTooltip_WritesReferencePngToSddWorkspace() {
            const int width = 1100, height = 500;
            RecipeFixture fx = NewRecipeFixture(await GetRecipeCacheAsync(), width, height);
            fx.Context.ShowRecipeToolTip = true;
            IRecipeNodeViewModel viewModel = MakeIronGearWheelNode(fx);
            var element = new RecipeNodeElement(fx.Context, viewModel);
            fx.Control.NodeElements.Add(element);

            var window = new AvaloniaWindow { Content = fx.Control, Width = width, Height = height };
            window.Show();
            window.MouseMove(fx.Control.Viewport.GraphToScreen(Point.Empty));

            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
            fx.Control.Render(surface.Canvas);

            string workspaceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".superpowers", "sdd", "2026-09-01-phase3-canvas-readonly");
            Directory.CreateDirectory(workspaceDir);
            string pngPath = Path.Combine(workspaceDir, "task-6-recipe-node-hover-tooltip.png");
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using (FileStream file = File.OpenWrite(pngPath))
                data.SaveTo(file);

            Assert.True(File.Exists(pngPath));
        }
    }
}
