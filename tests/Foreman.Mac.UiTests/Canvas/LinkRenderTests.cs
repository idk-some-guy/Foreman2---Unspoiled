using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises BaseLinkElement/LinkElement (the three bezier shapes, dynamic width, direction arrows, item
    //color) and PointingArrowRenderer (off-screen guide arrows). Endpoint/control-point coordinates below are
    //hand-derived from BaseLinkElement.UpdateCurve's own arithmetic (reference §5) given each fixture's node
    //placement - see the per-test comments - rather than read back from the element under test.
    public class LinkRenderTests {
        private const int Half = 450;
        private static readonly SKColor CanvasBackground = SKColors.White;

        //---- fixture plumbing, mirroring NodeRenderTests.cs's idiom ----

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required NodeElementContext Context { get; init; }

            public ItemPrototype NewItem(string name) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", isMissing: false);
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

        //Forces deferred UpdateState/UpdateValues to run (see NodeRenderTests.Prime) - LinkElement's endpoint
        //resolution depends on tab ordering, which is a UpdateState side effect.
        private static void Prime(Fixture fx, INodeViewModel viewModel) {
            fx.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController? controller = node is null ? null : fx.Graph.RequestNodeController(node);
            controller?.SetKeyNode(true);
            controller?.SetKeyNode(false);
        }

        private static (BaseNodeElement supplierElement, BaseNodeElement consumerElement, LinkElement link) BuildLink(
            Fixture fx, Point supplierLocation, NodeDirection supplierDirection, Point consumerLocation, NodeDirection consumerDirection, ItemQualityPair item) {
            BaseNode supplierNode = fx.Graph.CreateSupplierNode(item, supplierLocation);
            BaseNode consumerNode = fx.Graph.CreateConsumerNode(item, consumerLocation);
            supplierNode.NodeDirection = supplierDirection;
            consumerNode.NodeDirection = consumerDirection;
            fx.Graph.CreateLink(supplierNode, consumerNode, item);

            ISupplierNodeViewModel supplierViewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
            IConsumerNodeViewModel consumerViewModel = fx.Session.View.Nodes.OfType<IConsumerNodeViewModel>().Single();
            var supplierElement = new SupplierNodeElement(fx.Context, supplierViewModel);
            var consumerElement = new ConsumerNodeElement(fx.Context, consumerViewModel);
            Prime(fx, supplierViewModel);
            Prime(fx, consumerViewModel);
            supplierElement.PrePaint();
            consumerElement.PrePaint();

            INodeLinkViewModel linkViewModel = fx.Session.View.Links.Single();
            var link = new LinkElement(fx.Context, linkViewModel, supplierElement, consumerElement);
            return (supplierElement, consumerElement, link);
        }

        private static SKSurface RenderLink(LinkElement link, NodeDrawingStyle style = NodeDrawingStyle.Regular) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(CanvasBackground);
            surface.Canvas.Translate(Half, Half);
            link.Paint(surface.Canvas, style);
            return surface;
        }

        private static SKColor SamplePixel(SKSurface surface, int graphX, int graphY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(Half + graphX, Half + graphY);
        }

        //---- LineType.Simple: supplier below consumer, both NodeDirection.Up ----
        //Supplier at (0,150), consumer at (0,-150). Output/input tab connection points land 71px outside
        //each node's own center along its facing edge (BaseSimpleHeight/2=48 + tab Height/2=24, tab Height
        //settles at 48 = IconSize(32)+textHeight(10)+Border(3)+3 once UpdateValues runs) -> supplierOrigin
        //(0,79), consumerOrigin (0,-79). Both pull points collapse to the graph origin by construction
        //(max((79-(-79))/2,20) = 79, pulled back the full distance from each origin), so the cubic is exactly
        //symmetric about (0,0) and its parametric midpoint lands there too.

        [Fact]
        public void LinkElement_SimpleShape_PathPassesThroughDerivedEndpointsAndMidpoint() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 200, 40, 40)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 150), NodeDirection.Up, new Point(0, -150), NodeDirection.Up, fx.Pair(item));

            using SKSurface surface = RenderLink(link);

            Assert.Equal(BaseLinkElement.LineType.Simple, link.Type);
            var lineColor = new SKColor(200, 40, 40);
            Assert.Equal(lineColor, SamplePixel(surface, 0, 79));
            Assert.Equal(lineColor, SamplePixel(surface, 0, -79));
            Assert.Equal(lineColor, SamplePixel(surface, 0, 0));
        }

        //---- LineType.UShape: opposite directions, wide-enough X gap to keep the flat top non-degenerate ----
        //Supplier at (0,0) facing Up, consumer at (400,0) facing Down -> supplierOrigin (0,-71), consumerOrigin
        //(400,-71) (Down flips which edge the input tab sits on, landing at the same -71 offset). xOffset caps
        //at circlePull*2=200 (since |Δx|=400 > 200), so midUB=(100,-171) and midUC=(300,-171) stay distinct -
        //that top segment is a straight line (its control points collapse onto its own endpoints), so its true
        //midpoint (200,-171) sits exactly on the path.

        [Fact]
        public void LinkElement_UShape_PathPassesThroughDerivedEndpointsAndFlatTopMidpoint() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("copper-plate");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 40, 120, 200)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 0), NodeDirection.Up, new Point(400, 0), NodeDirection.Down, fx.Pair(item));

            using SKSurface surface = RenderLink(link);

            Assert.Equal(BaseLinkElement.LineType.UShape, link.Type);
            var lineColor = new SKColor(40, 120, 200);
            Assert.Equal(lineColor, SamplePixel(surface, 0, -71));
            Assert.Equal(lineColor, SamplePixel(surface, 400, -71));
            Assert.Equal(lineColor, SamplePixel(surface, 200, -171));
        }

        //---- LineType.NShape: same direction, wrong order, wide X gap ----
        //Supplier at (-300,-200) facing Up, consumer at (300,200) facing Up, so consumerOrigin.Y > supplierOrigin.Y
        //while both face Up - the "wrong order" that forces the N-shaped detour. supplierOrigin (-300,-271),
        //consumerOrigin (300,271); |Δx|=600 > 2*circlePull so midX is the plain X midpoint (0). midNC=(0,-271)
        //and midND=(0,271) are connected by a straight segment (again a collapsed-control-point line), so its
        //midpoint (0,0) sits exactly on the path.

        [Fact]
        public void LinkElement_NShape_PathPassesThroughDerivedEndpointsAndStraightMidSegmentMidpoint() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("steel-plate");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 90, 90, 90)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(-300, -200), NodeDirection.Up, new Point(300, 200), NodeDirection.Up, fx.Pair(item));

            using SKSurface surface = RenderLink(link);

            Assert.Equal(BaseLinkElement.LineType.NShape, link.Type);
            var lineColor = new SKColor(90, 90, 90);
            Assert.Equal(lineColor, SamplePixel(surface, -300, -271));
            Assert.Equal(lineColor, SamplePixel(surface, 300, 271));
            Assert.Equal(lineColor, SamplePixel(surface, 0, 0));
        }

        //---- color: item's average color, independent of link state ----

        [Fact]
        public void LinkElement_Draw_UsesItemAverageColorRegardlessOfLinkGeometry() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("plastic-bar");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 12, 200, 12)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 150), NodeDirection.Up, new Point(0, -150), NodeDirection.Up, fx.Pair(item));

            using SKSurface surface = RenderLink(link);

            Assert.Equal(new SKColor(12, 200, 12), SamplePixel(surface, 0, 79));
        }

        //---- dynamic width: LinkWidth is a plain settable property BaseLinkElement.Draw honors directly;
        //the flow -> width mapping itself lives in the not-yet-ported viewer (reference §5), so this exercises
        //the stroke-thickness contract Task 4 owns: whatever LinkWidth is set to is what gets drawn. ----

        private static int MeasureStrokeThicknessAt(SKSurface surface, int graphX, int graphYCenter, SKColor lineColor) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            int count = 0;
            for (int y = graphYCenter - 25; y <= graphYCenter + 25; y++)
                if (pixmap.GetPixelColor(Half + graphX, Half + y) == lineColor)
                    count++;
            return count;
        }

        [Fact]
        public void LinkElement_LinkWidth_ConstantDefault_Is3PxWideStroke() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("wood");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 150, 90, 30)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 0), NodeDirection.Up, new Point(400, 0), NodeDirection.Down, fx.Pair(item));

            Assert.Equal(3f, link.LinkWidth); //static default, matching upstream's "3px when DynamicLinkWidth is off"

            using SKSurface surface = RenderLink(link);
            int thickness = MeasureStrokeThicknessAt(surface, 200, -171, new SKColor(150, 90, 30)); //flat top of the U, a straight run
            Assert.InRange(thickness, 2, 5);
        }

        [Fact]
        public void LinkElement_LinkWidth_WiderValue_DrawsThickerStroke() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("wood");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 150, 90, 30)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 0), NodeDirection.Up, new Point(400, 0), NodeDirection.Down, fx.Pair(item));
            var lineColor = new SKColor(150, 90, 30);

            using SKSurface thinSurface = RenderLink(link);
            int thinThickness = MeasureStrokeThicknessAt(thinSurface, 200, -171, lineColor);

            link.LinkWidth = 24f; //simulates what the future flow->width mapping (task 12) would assign
            using SKSurface wideSurface = RenderLink(link);
            int wideThickness = MeasureStrokeThicknessAt(wideSurface, 200, -171, lineColor);

            Assert.True(wideThickness > thinThickness + 10, $"expected a visibly thicker stroke: thin={thinThickness}, wide={wideThickness}");
        }

        //---- direction arrows: ArrowsOnLinks && !DynamicLinkWidth && !iconOnlyDraw gates the arrowhead ----
        //Reusing the Simple-shape fixture: consumerOrigin (0,-79), consumerPull (0,0) (its tangent anchor), so
        //the arrowhead triangle for the default LinkWidth=3 has vertices tip(0,-79), (6,-70), (-6,-70) - a
        //point at (3,-73) sits inside that triangle but outside the plain 3px-wide line (|x|<=1.5).

        [Fact]
        public void LinkElement_ArrowsOnLinksTrue_DrawsArrowheadNearConsumerEndpoint() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-gear-wheel");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 220, 60, 60)));
            fx.Context.ArrowsOnLinks = true;
            fx.Context.DynamicLinkWidth = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 150), NodeDirection.Up, new Point(0, -150), NodeDirection.Up, fx.Pair(item));

            using SKSurface surface = RenderLink(link);

            Assert.Equal(new SKColor(220, 60, 60), SamplePixel(surface, 3, -73));
        }

        [Fact]
        public void LinkElement_ArrowsOnLinksFalse_NoArrowheadNearConsumerEndpoint() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-gear-wheel");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 220, 60, 60)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 150), NodeDirection.Up, new Point(0, -150), NodeDirection.Up, fx.Pair(item));

            using SKSurface surface = RenderLink(link);

            Assert.Equal(CanvasBackground, SamplePixel(surface, 3, -73));
        }

        [Fact]
        public void LinkElement_DynamicLinkWidthTrue_SuppressesArrowheadEvenWhenArrowsOnLinksTrue() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-gear-wheel");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 220, 60, 60)));
            fx.Context.ArrowsOnLinks = true;
            fx.Context.DynamicLinkWidth = true;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 150), NodeDirection.Up, new Point(0, -150), NodeDirection.Up, fx.Pair(item));

            using SKSurface surface = RenderLink(link);

            Assert.Equal(CanvasBackground, SamplePixel(surface, 3, -73));
        }

        //---- allocation: BaseLinkElement.Draw reuses a per-instance SKPath instead of building one per call
        //(fix-round item 4, task-2-report.md's fold-in) ----

        [Fact]
        public void LinkElement_RepeatedPaint_KeepsPerDrawAllocationLow() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 200, 40, 40)));
            fx.Context.ArrowsOnLinks = false;
            (_, _, LinkElement link) = BuildLink(fx, new Point(0, 150), NodeDirection.Up, new Point(0, -150), NodeDirection.Up, fx.Pair(item));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));

            const int warmupFrames = 5, measuredFrames = 200;
            for (int i = 0; i < warmupFrames; i++)
                link.Paint(surface.Canvas, NodeDrawingStyle.Regular);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < measuredFrames; i++)
                link.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            long bytesPerDraw = (GC.GetAllocatedBytesForCurrentThread() - before) / measuredFrames;

            //Measured: a per-call `new SKPath()` costs ~136 bytes/draw here; a reused, Reset()-between-calls
            //instance field measures ~56. 100 sits between the two with headroom for run noise on either side.
            Assert.True(bytesPerDraw < 100, $"Expected a reused SKPath to keep per-draw allocation low, measured {bytesPerDraw} bytes/draw.");
        }

        //---- PointingArrowRenderer: screen-space guide arrows toward off-screen nodes, gated per-flag ----

        private static SKSurface RenderArrows(PointingArrowRenderer renderer, ProductionGraph graph, int size) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(size, size));
            surface.Canvas.Clear(CanvasBackground);
            renderer.Paint(surface.Canvas, graph);
            return surface;
        }

        private static SKColor SampleScreenPixel(SKSurface surface, int x, int y) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(x, y);
        }

        //A freshly-created, unlinked ConsumerNode settles into NodeState.MissingLink immediately (ProductionGraph
        //.SetupNodeOfType calls UpdateState() on creation; AllLinksConnected is false with zero InputLinks).
        //Placed far below a 200x200 viewport at graph (0,1000) -> screen (100,1100) (ViewScale=1, ViewOffset=0,0),
        //well past the bottom border (height-Padding=190). IntersectionPoint's horizontal-border formula
        //collapses to x=100 here (both node and viewport center share x=100), so the guide arrow's line runs
        //straight down the vertical centerline from y=158 to the border at y=190.

        [Fact]
        public void PointingArrowRenderer_ShowDisconnectedArrowsOn_DrawsArrowTowardOffscreenNode() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("water");
            fx.Graph.CreateConsumerNode(fx.Pair(item), new Point(0, 1000));
            var renderer = new PointingArrowRenderer(new Viewport(200, 200)) { ShowDisconnectedArrows = true };

            using SKSurface surface = RenderArrows(renderer, fx.Graph, 200);

            Assert.NotEqual(CanvasBackground, SampleScreenPixel(surface, 100, 174));
        }

        [Fact]
        public void PointingArrowRenderer_WrongGate_DoesNotDrawArrow() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("water");
            fx.Graph.CreateConsumerNode(fx.Pair(item), new Point(0, 1000)); //State == MissingLink, not Error
            var renderer = new PointingArrowRenderer(new Viewport(200, 200)) { ShowErrorArrows = true };

            using SKSurface surface = RenderArrows(renderer, fx.Graph, 200);

            Assert.Equal(CanvasBackground, SampleScreenPixel(surface, 100, 174));
        }

        [Fact]
        public void PointingArrowRenderer_AllGatesOff_DrawsNothing() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("water");
            fx.Graph.CreateConsumerNode(fx.Pair(item), new Point(0, 1000));
            var renderer = new PointingArrowRenderer(new Viewport(200, 200));

            using SKSurface surface = RenderArrows(renderer, fx.Graph, 200);

            Assert.Equal(CanvasBackground, SampleScreenPixel(surface, 100, 174));
        }

        //---- reference render: two connected node pairs (one Simple, one UShape) with arrows on, for the SDD workspace ----

        [Fact]
        public void RenderConnectedNodePairs_SimpleAndUShape_WritesReferencePngToSddWorkspace() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate");
            item.SetIconAndColor(new IconColorPair(null, Color.FromArgb(255, 200, 40, 40)));
            fx.Context.ArrowsOnLinks = true;
            fx.Context.DynamicLinkWidth = false;

            (BaseNodeElement simpleSupplier, BaseNodeElement simpleConsumer, LinkElement simpleLink) =
                BuildLink(fx, new Point(-250, 150), NodeDirection.Up, new Point(-250, -150), NodeDirection.Up, fx.Pair(item));

            BaseNode uSupplierNode = fx.Graph.CreateSupplierNode(fx.Pair(item), new Point(150, 0));
            BaseNode uConsumerNode = fx.Graph.CreateConsumerNode(fx.Pair(item), new Point(400, 0));
            uConsumerNode.NodeDirection = NodeDirection.Down;
            fx.Graph.CreateLink(uSupplierNode, uConsumerNode, fx.Pair(item));
            ISupplierNodeViewModel uSupplierViewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single(v => v.Location.X == 150);
            IConsumerNodeViewModel uConsumerViewModel = fx.Session.View.Nodes.OfType<IConsumerNodeViewModel>().Single(v => v.Location.X == 400);
            var uSupplierElement = new SupplierNodeElement(fx.Context, uSupplierViewModel);
            var uConsumerElement = new ConsumerNodeElement(fx.Context, uConsumerViewModel);
            Prime(fx, uSupplierViewModel);
            Prime(fx, uConsumerViewModel);
            uSupplierElement.PrePaint();
            uConsumerElement.PrePaint();
            INodeLinkViewModel uLinkViewModel = fx.Session.View.Links.Single(l => l.SupplierId == uSupplierViewModel.Id);
            var uLink = new LinkElement(fx.Context, uLinkViewModel, uSupplierElement, uConsumerElement);

            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(CanvasBackground);
            surface.Canvas.Translate(Half, Half);
            simpleSupplier.PrePaint();
            simpleConsumer.PrePaint();
            simpleSupplier.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            simpleConsumer.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            simpleLink.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            uSupplierElement.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            uConsumerElement.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            uLink.Paint(surface.Canvas, NodeDrawingStyle.Regular);

            string workspaceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".superpowers", "sdd", "2026-09-01-phase3-canvas-readonly");
            Directory.CreateDirectory(workspaceDir);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream file = File.OpenWrite(Path.Combine(workspaceDir, "task-4-links-simple-and-ushape.png"));
            data.SaveTo(file);
            surface.Dispose();

            Assert.True(File.Exists(Path.Combine(workspaceDir, "task-4-links-simple-and-ushape.png")));
        }
    }
}
