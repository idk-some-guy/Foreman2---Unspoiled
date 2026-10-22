using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;
using AvColor = Avalonia.Media.Color;
using AvaloniaPoint = Avalonia.Point;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public sealed class NodeStateTests : IDisposable {
        private const int Half = 200;
        private static readonly double[] Steps = [1.0, 1.5];

        public void Dispose() => WindowScaler.SetScale(1.0);

        private sealed class HighContrastAppearance(AvColor accent) : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => accent;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.High;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required QualityPrototype Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required NodeElementContext Context { get; init; }

            public ItemPrototype NewItem(string name, bool isMissing = false) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", isMissing);
                Store(Cache).Items[name] = item;
                return item;
            }

            public ItemQualityPair Pair(IItem item) => new(item, Quality);

            public BaseNodeController Controller(INodeViewModel viewModel) {
                Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
                return Graph.RequestNodeController(node!)!;
            }
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
            var context = new NodeElementContext(session.View, new Viewport(2 * Half, 2 * Half)) { FlagOUSuppliedNodes = true };
            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Graph = graph, Session = session, Context = context };
        }

        private static T Painted<T>(T element) where T : BaseNodeElement {
            element.PrePaint();
            return element;
        }

        private static SupplierNodeElement Supplier(Fixture fx, ItemPrototype item) {
            fx.Graph.CreateSupplierNode(fx.Pair(item), DrawingPoint.Empty);
            return Painted(new SupplierNodeElement(fx.Context, fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Last()));
        }

        private static ConsumerNodeElement Consumer(Fixture fx, ItemPrototype item) {
            fx.Graph.CreateConsumerNode(fx.Pair(item), DrawingPoint.Empty);
            return Painted(new ConsumerNodeElement(fx.Context, fx.Session.View.Nodes.OfType<IConsumerNodeViewModel>().Last()));
        }

        //A manual supplier with no output link overproduces everything it makes.
        private static SupplierNodeElement Overproducing(Fixture fx) {
            SupplierNodeElement supplier = Supplier(fx, fx.NewItem("plastic-bar"));
            BaseNodeController controller = fx.Controller(supplier.ViewModel);
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(100);
            return Painted(supplier);
        }

        //A manual consumer with no input link is short of its required rate.
        private static ConsumerNodeElement Undersupplied(Fixture fx, ItemPrototype item) {
            ConsumerNodeElement consumer = Consumer(fx, item);
            BaseNodeController controller = fx.Controller(consumer.ViewModel);
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(50);
            return Painted(consumer);
        }

        private static IPassthroughNodeViewModel LinkedPassthrough(Fixture fx, ItemPrototype item) {
            ItemQualityPair pair = fx.Pair(item);
            BaseNode passthrough = fx.Graph.CreatePassthroughNode(pair, DrawingPoint.Empty);
            fx.Graph.CreateLink(fx.Graph.CreateSupplierNode(pair, new DrawingPoint(0, -140)), passthrough, pair);
            fx.Graph.CreateLink(passthrough, fx.Graph.CreateConsumerNode(pair, new DrawingPoint(0, 140)), pair);
            ((PassthroughNodeController)fx.Graph.RequestNodeController(passthrough)!).SetSimpleDraw(true);
            return fx.Session.View.Nodes.OfType<IPassthroughNodeViewModel>().Last();
        }

        private static SpoilNodeElement Spoil(Fixture fx) {
            ItemPrototype input = fx.NewItem("raw-fish");
            ItemPrototype output = fx.NewItem("spoilage");
            input.SpoilResult = output;
            fx.Graph.CreateSpoilNode(fx.Pair(input), output, DrawingPoint.Empty);
            return Painted(new SpoilNodeElement(fx.Context, fx.Session.View.Nodes.OfType<ISpoilNodeViewModel>().Last()));
        }

        private static PlantNodeElement Plant(Fixture fx) {
            ItemPrototype seed = fx.NewItem("yumako-seed");
            var process = new PlantProcessPrototype(fx.Cache, "plant-yumako-seed");
            process.InternalOneWayAddProduct(fx.NewItem("yumako"), 1);
            process.Seed = seed;
            seed.PlantResult = process;
            fx.Graph.CreatePlantNode(process, fx.Quality, DrawingPoint.Empty);
            return Painted(new PlantNodeElement(fx.Context, fx.Session.View.Nodes.OfType<IPlantNodeViewModel>().Last()));
        }

        //Core never puts a passthrough in Warning, and its Error needs an impossible link, so the state is stubbed over a real node.
        private sealed class StatedPassthrough(IPassthroughNodeViewModel inner) : IPassthroughNodeViewModel {
            private NodeState? state;

            public void Set(NodeState? value) {
                state = value;
                NodeStateChanged?.Invoke(this, EventArgs.Empty);
            }

            public NodeState State => state ?? inner.State;
            public List<string> GetErrors() => State == NodeState.Error ? ["> Some links are invalid!"] : [];
            public List<string> GetWarnings() => State == NodeState.Warning ? ["Stubbed warning"] : [];

            public ItemQualityPair PassthroughItem => inner.PassthroughItem;
            public bool SimpleDraw => inner.SimpleDraw;
            public NodeId Id => inner.Id;
            public NodeType NodeType => inner.NodeType;
            public DrawingPoint Location => inner.Location;
            public NodeDirection NodeDirection => inner.NodeDirection;
            public bool KeyNode => inner.KeyNode;
            public string KeyNodeTitle => inner.KeyNodeTitle;
            public RateType RateType => inner.RateType;
            public IEnumerable<ItemQualityPair> Inputs => inner.Inputs;
            public IEnumerable<ItemQualityPair> Outputs => inner.Outputs;
            public IReadOnlyList<INodeLinkViewModel> InputLinks => inner.InputLinks;
            public IReadOnlyList<INodeLinkViewModel> OutputLinks => inner.OutputLinks;
            public double ActualRate => inner.ActualRate;
            public double DesiredRate => inner.DesiredRate;
            public double ActualSetValue => inner.ActualSetValue;
            public double DesiredSetValue => inner.DesiredSetValue;
            public double MaxDesiredSetValue => inner.MaxDesiredSetValue;
            public string SetValueDescription => inner.SetValueDescription;
            public double GetConsumeRate(ItemQualityPair item) => inner.GetConsumeRate(item);
            public double GetSupplyRate(ItemQualityPair item) => inner.GetSupplyRate(item);
            public double GetSupplyUsedRate(ItemQualityPair item) => inner.GetSupplyUsedRate(item);
            public bool IsOverproducing() => inner.IsOverproducing();
            public bool IsOverproducing(ItemQualityPair item) => inner.IsOverproducing(item);
            public bool ManualRateNotMet() => inner.ManualRateNotMet();
            public event EventHandler? NodeStateChanged;
            public event EventHandler? NodeValuesChanged { add => inner.NodeValuesChanged += value; remove => inner.NodeValuesChanged -= value; }
            public event PropertyChangedEventHandler? PropertyChanged { add => inner.PropertyChanged += value; remove => inner.PropertyChanged -= value; }
        }

        private static SKSurface Render(BaseNodeElement element, NodeDrawingStyle style = NodeDrawingStyle.Regular) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Translate(Half, Half);
            element.PrePaint();
            element.Paint(surface.Canvas, style);
            return surface;
        }

        //The tabs sit on the card's border: where a tab crosses the edge the pixel is the tab's own paint, elsewhere the ring's.
        private static void AssertTabCoversTheBorder(BaseNodeElement node, SKSurface surface, SKColor ring) {
            SKRect bounds = GraphBounds(node);
            ItemTabElement tab = node.SubElements.OfType<ItemTabElement>().First();
            SKRect tabRect = tab.TabGeometry().Rect;
            float edge = tabRect.MidY < bounds.MidY ? bounds.Top : bounds.Bottom;
            using SKSurface alone = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            alone.Canvas.Clear(SKColors.Transparent);
            alone.Canvas.Translate(Half, Half);
            tab.Paint(alone.Canvas, NodeDrawingStyle.Regular);
            foreach (float y in new[] { edge - 0.5f, edge + 0.5f }) {
                SKColor own = Pixel(alone, tabRect.MidX, y);
                Assert.Equal(255, own.Alpha);
                Assert.Equal(own, Pixel(surface, tabRect.MidX, y));
            }
            Assert.Contains(ring, Enumerable.Range(0, 16).Select(dx => Pixel(surface, bounds.Left + 14 + dx, edge - 0.5f)));
        }

        private static SKRect IconBox(BaseNodeElement node, NodeElementContext context) {
            DrawingPoint centre = node.LocalToGraph(DrawingPoint.Empty);
            float half = context.IconsDrawSize / 2f;
            return new SKRect(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half);
        }

        private static SKRect Clipped(SKRect rect, SKRect bounds) {
            rect.Intersect(bounds);
            return rect;
        }

        private static SKColor Pixel(SKSurface surface, float graphX, float graphY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(Half + (int)Math.Floor(graphX), Half + (int)Math.Floor(graphY));
        }

        private static SKColor Resolve(string key) {
            AvColor color = ThemeResources.ResolveColor(key, default);
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        private static SKRect GraphBounds(BaseNodeElement node) {
            DrawingPoint centre = node.LocalToGraph(DrawingPoint.Empty);
            return SKRect.Create(centre.X - (node.Width / 2f), centre.Y - (node.Height / 2f), node.Width, node.Height);
        }

        private static void AssertRect(SKRect expected, SKRect actual) {
            Assert.Equal(expected.Left, actual.Left, 3);
            Assert.Equal(expected.Top, actual.Top, 3);
            Assert.Equal(expected.Right, actual.Right, 3);
            Assert.Equal(expected.Bottom, actual.Bottom, 3);
        }

        private static void AssertClose(SKColor expected, SKColor actual, int tolerance = 2) =>
            Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance && Math.Abs(expected.Blue - actual.Blue) <= tolerance && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance,
                $"expected {expected}, got {actual}");

        private static SKColor Over(SKColor top, SKColor bottom) {
            float a = top.Alpha / 255f;
            byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
            return new SKColor(Mix(top.Red, bottom.Red), Mix(top.Green, bottom.Green), Mix(top.Blue, bottom.Blue), 255);
        }

        private static ItemTabElement Tab(BaseNodeElement node, LinkType type) => node.SubElements.OfType<ItemTabElement>().First(tab => tab.LinkType == type);

        private static SKPoint Centre(SKRect rect) => new(rect.MidX, rect.MidY);

        //---- §02 fidelity assertions ----

        [AvaloniaFact]
        public void BorderLane_SelectedIsNodeSelected2_OverproducingIsWarningDash4And3_BothIsNodeSelectedAndStillOver() {
            Fixture fx = NewFixture();
            SupplierNodeElement supplier = Supplier(fx, fx.NewItem("iron-ore"));
            RingGeometry normal = supplier.BorderRing();
            Assert.Equal(1f, normal.StrokeWidth);
            Assert.Equal(Resolve("Foreman.Color.BorderStrong"), normal.Color);

            SupplierNodeElement over = Overproducing(fx);
            Assert.True(over.ViewModel.IsOverproducing());
            var bar = new PassthroughNodeElement(fx.Context, LinkedPassthrough(fx, fx.NewItem("coal"))) { Highlighted = true };
            SKColor selected = Resolve("Foreman.Color.NodeSelected");
            SKColor warning = Resolve("Foreman.Color.Warning");
            supplier.Highlighted = true;
            foreach (double step in Steps) {
                float s = (float)step;
                WindowScaler.SetScale(step);
                Painted(supplier);
                RingGeometry ring = supplier.BorderRing();
                SKRect bounds = GraphBounds(supplier);
                Assert.Equal(2f, ring.StrokeWidth);
                Assert.Equal(selected, ring.Color);
                Assert.Null(ring.Dash);
                Assert.Equal(10f, ring.Radius);
                AssertRect(bounds, ring.Rect);
                using (SKSurface surface = Render(supplier)) {
                    Assert.Equal(selected, Pixel(surface, bounds.Left - 0.5f, bounds.MidY));
                    Assert.Equal(selected, Pixel(surface, bounds.Left + 0.5f, bounds.MidY));
                    AssertTabCoversTheBorder(supplier, surface, selected);
                }

                Painted(over);
                over.Highlighted = false;
                over.PrePaint();
                RingGeometry flow = over.BorderRing();
                Assert.Equal(2f, flow.StrokeWidth);
                Assert.Equal(warning, flow.Color);
                Assert.Equal([4f, 3f], flow.Dash ?? []);
                AssertRect(GraphBounds(over), flow.Rect);
                Assert.Null(over.InnerRing());
                TabGeometry overTab = Tab(over, LinkType.Output).TabGeometry();
                Assert.Equal(warning, overTab.Stroke);
                Assert.Equal([4f, 3f], overTab.Dash ?? []);
                Assert.Equal(new NodeBadge(NodeBadgeKind.Over, "Over"), over.Badge());
                using (SKSurface surface = Render(over))
                    AssertTabCoversTheBorder(over, surface, warning);

                over.Highlighted = true;
                over.PrePaint();
                RingGeometry both = over.BorderRing();
                Assert.Equal(selected, both.Color);
                Assert.Equal(2f, both.StrokeWidth);
                Assert.Null(both.Dash);
                Assert.Equal("Over", over.Badge()?.Text);
                Assert.Equal([4f, 3f], Tab(over, LinkType.Output).TabGeometry().Dash ?? []);

                //The bar has no card; its ring wraps the 72·s × 96·s bounds at radius 10.
                Painted(bar);
                Assert.Equal(72 * s, bar.Width, 0.5f);
                Assert.Equal(96 * s, bar.Height, 0.5f);
                RingGeometry barRing = bar.BorderRing();
                AssertRect(GraphBounds(bar), barRing.Rect);
                Assert.Equal(10f, barRing.Radius);
                Assert.Equal(selected, barRing.Color);

                //Icon view: the ring wraps the 32 box at radius 8, and badges hide.
                fx.Context.IconsOnly = true;
                try {
                    Painted(supplier);
                    RingGeometry icon = supplier.BorderRing();
                    SKRect box = IconBox(supplier, fx.Context);
                    AssertRect(box, icon.Rect);
                    Assert.Equal(8f, icon.Radius);
                    Assert.Equal(2f, icon.StrokeWidth);
                    Assert.Equal(selected, icon.Color);
                    using (SKSurface surface = Render(supplier, NodeDrawingStyle.IconsOnly))
                        Assert.Equal(selected, Pixel(surface, box.Left - 0.5f, box.MidY));
                    Painted(over);
                    Assert.Null(over.Badge());
                    Assert.Null(over.BadgeHitRect());
                } finally {
                    fx.Context.IconsOnly = false;
                }
            }
        }

        [AvaloniaFact]
        public void Undersupplied_BorderIsError1_PlusInnerRingDeflated3() {
            Fixture fx = NewFixture();
            ConsumerNodeElement consumer = Undersupplied(fx, fx.NewItem("plate"));
            Assert.True(consumer.ViewModel.ManualRateNotMet());
            SKColor error = Resolve("Foreman.Color.Error");
            SKRect bounds = GraphBounds(consumer);

            RingGeometry border = consumer.BorderRing();
            Assert.Equal(1f, border.StrokeWidth);
            Assert.Equal(error, border.Color);
            Assert.Null(border.Dash);

            RingGeometry inner = Assert.NotNull(consumer.InnerRing());
            AssertRect(SKRect.Inflate(bounds, -3, -3), inner.Rect);
            Assert.Equal(1f, inner.StrokeWidth);
            Assert.Equal(error, inner.Color);

            ItemTabElement input = Tab(consumer, LinkType.Input);
            Assert.Equal(error, input.TabGeometry().Stroke);
            Assert.Null(input.TabGeometry().Dash);
            Assert.Equal(error, input.RateText.Color);
            Assert.Equal(new NodeBadge(NodeBadgeKind.Undersupplied, "Short"), consumer.Badge());

            //The inner line is centred 3 inside the top edge, so it half-covers the pixel row starting there.
            using SKSurface surface = Render(consumer);
            SKColor card = Resolve("Foreman.Color.ControlBackground");
            AssertClose(error, Pixel(surface, bounds.MidX, bounds.Top + 0.5f), tolerance: 2);
            AssertClose(Over(error.WithAlpha(128), card), Pixel(surface, bounds.MidX, bounds.Top + 3), tolerance: 10);
            AssertClose(card, Pixel(surface, bounds.MidX, bounds.Top + 5), tolerance: 2);
        }

        [AvaloniaFact]
        public void Focus_Inflated7Point5_Stroke3_Radius17Point5_FourUnderIncreaseContrast_AbsentAfterClick_PresentAfterTab() {
            using CanvasFixture fx = CanvasFixture.WithVanillaAndOneNode(out AvaloniaPoint nodeScreenPoint);
            BaseNodeElement node = fx.Viewer.NodeElements.Single();
            Assert.Null(node.FocusRing());

            node.ShowsKeyboardFocus = true;
            RingGeometry ring = Assert.NotNull(node.FocusRing());
            AssertRect(SKRect.Inflate(GraphBounds(node), 7.5f, 7.5f), ring.Rect);
            Assert.Equal(3f, ring.StrokeWidth);
            Assert.Equal(17.5f, ring.Radius);
            Assert.Equal(Resolve("Foreman.Color.FocusRing"), ring.Color);
            Assert.Null(ring.Dash);

            fx.Viewer.IconsOnly = true;
            node.PrePaint();
            RingGeometry icon = Assert.NotNull(node.FocusRing());
            AssertRect(SKRect.Inflate(IconBox(node, fx.Viewer.Context), 7.5f, 7.5f), icon.Rect);
            Assert.Equal(15.5f, icon.Radius);
            fx.Viewer.IconsOnly = false;
            node.PrePaint();

            //Initialize ends in Hub.Refresh; re-running Initialize on the shared App would stack another FluentTheme.
            App app = (App)Application.Current!;
            AvColor accent = AvColor.FromRgb(0x2E, 0x7D, 0x32);
            try {
                app.InstallAppearance(new HighContrastAppearance(accent));
                app.Resources.Remove("Foreman.FocusRing.Width");
                app.Hub.Refresh();
                Assert.Equal(accent, ThemeResources.ResolveColor("Foreman.Color.Accent", default));
                Assert.True(app.Resources.TryGetValue("Foreman.FocusRing.Width", out object? width));
                Assert.Equal(4.0, width);
                Assert.Equal(new SKColor(accent.R, accent.G, accent.B, accent.A), TokenBridge.Current.Accent);
                node.PrePaint();
                RingGeometry contrast = Assert.NotNull(node.FocusRing());
                Assert.Equal(4f, contrast.StrokeWidth);
                Assert.Equal(255, contrast.Color.Alpha);
                AssertRect(ring.Rect, contrast.Rect);
            } finally {
                app.InstallAppearance(null);
                TokenBridge.Refresh();
            }
            node.PrePaint();
            Assert.Equal(3f, node.FocusRing()!.Value.StrokeWidth);

            fx.ClickAt(nodeScreenPoint);
            Assert.False(node.ShowsKeyboardFocus);
            Assert.Null(node.FocusRing());
        }

        [AvaloniaFact]
        public void Unavailable_ContentOpacityIs0Point6_BadgeOpaque_NoColorFilterOnAnyPaint() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("copper-ore");
            SupplierNodeElement available = Supplier(fx, fx.NewItem("iron-ore"));
            Assert.Equal(1f, available.ContentOpacity);
            Assert.Null(available.Badge());

            item.Enabled = false;
            SupplierNodeElement supplier = Supplier(fx, item);
            Assert.Equal(0.6f, supplier.ContentOpacity, 0.001f);
            Assert.Equal(new NodeBadge(NodeBadgeKind.Unavailable, "Unavailable"), supplier.Badge());

            using SKSurface surface = Render(supplier);
            SKColor strip = Resolve("Foreman.Color.NodeSupplier");
            SKRect stripRect = supplier.TypeStripRect();
            SKColor stripPixel = Pixel(surface, stripRect.MidX, stripRect.MidY);
            AssertClose(strip.WithAlpha((byte)Math.Round(strip.Alpha * 0.6)), stripPixel);

            SKRect badge = supplier.BadgeRect();
            SKColor well = Resolve("Foreman.Color.WellBackground");
            AssertClose(well, Pixel(surface, badge.Left + 3, badge.MidY));
            AssertClose(Over(Resolve("Foreman.Color.BorderStrong"), well), Pixel(surface, badge.MidX, badge.Top + 0.5f));

            supplier.Highlighted = true;
            using SKSurface selectedSurface = Render(supplier);
            SKRect bounds = GraphBounds(supplier);
            Assert.Equal(Resolve("Foreman.Color.NodeSelected"), Pixel(selectedSurface, bounds.Left - 0.5f, bounds.MidY));
            Assert.Equal(Resolve("Foreman.Color.NodeSelected"), Pixel(selectedSurface, bounds.Left + 0.5f, bounds.MidY));
        }

        [AvaloniaFact]
        public void BadgePriority_UnavailableAndShort_ShowsOnlyUnavailable_AndFlagOffHidesOverButKeepsTheDash() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("plate");
            item.Enabled = false;
            ConsumerNodeElement consumer = Undersupplied(fx, item);
            Assert.True(consumer.ViewModel.ManualRateNotMet());
            Assert.Equal(new NodeBadge(NodeBadgeKind.Unavailable, "Unavailable"), consumer.Badge());
            Assert.NotNull(consumer.InnerRing());

            SupplierNodeElement supplier = Overproducing(fx);
            Assert.Equal("Over", supplier.Badge()?.Text);

            fx.Context.FlagOUSuppliedNodes = false;
            supplier.PrePaint();
            Assert.Null(supplier.Badge());
            Assert.Equal([4f, 3f], supplier.BorderRing().Dash ?? []);
            Assert.Equal(Resolve("Foreman.Color.Warning"), supplier.BorderRing().Color);

            fx.Context.FlagOUSuppliedNodes = true;
            supplier.PrePaint();
            Assert.Equal("Over", supplier.Badge()?.Text);
        }

        [AvaloniaFact]
        public void Armed_OnlyTheTabUnderTheDraggedEnd_IsAccent2OnAccentSoft() {
            using CanvasFixture fx = CanvasFixture.WithVanillaAndOneSupplierOf("iron-plate", out AvaloniaPoint outputTab);
            BaseNodeElement supplier = fx.Viewer.NodeElements.Single();
            NodeId targetId = fx.Viewer.Session.Editor.CreateConsumerNode(new ItemQualityPair(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!), new DrawingPoint(0, 250));
            NodeId otherId = fx.Viewer.Session.Editor.CreateConsumerNode(new ItemQualityPair(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!), new DrawingPoint(300, 250));
            BaseNodeElement target = Painted(fx.Viewer.NodeElementDictionary[targetId]);
            BaseNodeElement other = Painted(fx.Viewer.NodeElementDictionary[otherId]);
            ItemTabElement targetTab = Tab(target, LinkType.Input);
            AvaloniaPoint targetTabScreen = fx.Control.Viewport.GraphToScreen(targetTab.LocalToGraph(DrawingPoint.Empty));

            LinkDrag drag = fx.DragFrom(outputTab).To(targetTabScreen);

            TabGeometry armed = targetTab.TabGeometry();
            Assert.Equal(2f, armed.StrokeWidth);
            Assert.Equal(Resolve("Foreman.Color.Accent"), armed.Stroke);
            Assert.Equal(Resolve("Foreman.Color.AccentSoft"), armed.Fill);
            SKColor tabBorder = Resolve("Foreman.Color.NodeTabBorder");
            foreach (ItemTabElement tab in new[] { Tab(supplier, LinkType.Output), Tab(other, LinkType.Input) }) {
                Assert.Equal(tabBorder, tab.TabGeometry().Stroke);
                Assert.Equal(1.5f, tab.TabGeometry().StrokeWidth);
            }

            drag.Release();
            fx.MoveTo(fx.Control.Viewport.GraphToScreen(new DrawingPoint(150, 150)));
            Painted(target);
            TabGeometry released = targetTab.TabGeometry();
            Assert.Equal(1.5f, released.StrokeWidth);
            Assert.Equal(tabBorder, released.Stroke);
            Assert.Equal(Resolve("Foreman.Color.WellBackground"), released.Fill);
        }

        [AvaloniaFact]
        public void StaleAfterFailedSolve_TabTextIsTextSecondaryItalic_ValuesUnchanged() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            var pair = new RecipeQualityPair(fx.Cache.Recipes["iron-gear-wheel"], fx.Cache.DefaultQuality!);
            NodeId id = fx.Viewer.Session.Editor.CreateRecipeNode(pair, DrawingPoint.Empty);
            fx.Viewer.Graph.UpdateNodeValues();
            BaseNodeElement recipe = Painted(fx.Viewer.NodeElementDictionary[id]);
            ItemTabElement[] tabs = [.. recipe.SubElements.OfType<ItemTabElement>()];
            string[] rates = [.. tabs.Select(tab => tab.RateText.Text)];
            NodeText Count() => recipe.Texts.Single(text => text.Role == NodeTextRole.Count);
            string count = Count().Text;
            SKColor text = Resolve("Foreman.Color.Text");
            SKColor secondary = Resolve("Foreman.Color.TextSecondary");
            Assert.All(tabs, tab => Assert.False(tab.RateText.Italic));

            recipe.StaleOverride = NodeSolveState.Stale;
            recipe.PrePaint();
            Assert.Equal(rates, tabs.Select(tab => tab.RateText.Text));
            Assert.Equal(count, Count().Text);
            foreach (NodeText rate in tabs.Select(tab => tab.RateText).Append(Count())) {
                Assert.Equal(secondary, rate.Color);
                Assert.True(rate.Italic);
                Assert.NotEqual(SKFontStyleSlant.Upright, NodeCardPainter.Typeface(rate.Weight, rate.Italic).FontStyle.Slant);
            }

            recipe.StaleOverride = NodeSolveState.NeverSolved;
            recipe.PrePaint();
            Assert.All(tabs, tab => Assert.Equal("—", tab.RateText.Text));
            Assert.Equal("—", Count().Text);

            recipe.StaleOverride = null;
            recipe.PrePaint();
            Assert.Equal(rates, tabs.Select(tab => tab.RateText.Text));
            Assert.Equal(count, Count().Text);
            Assert.All(tabs, tab => Assert.Equal(text, tab.RateText.Color));
            Assert.False(Count().Italic);

            Fixture small = NewFixture();
            foreach ((BaseNodeElement node, string word) in new (BaseNodeElement, string)[] { (Spoil(small), "Spoil"), (Plant(small), "Plant") }) {
                NodeText Sub() => node.Texts.Single(t => t.Role == NodeTextRole.Detail);
                string before = Sub().Text;
                Assert.StartsWith(word + " · ×", before);
                Assert.False(Sub().Italic);

                node.StaleOverride = NodeSolveState.Stale;
                node.PrePaint();
                Assert.Equal(before, Sub().Text);
                Assert.True(Sub().Italic);
                Assert.Equal(secondary, Sub().Color);

                node.StaleOverride = NodeSolveState.NeverSolved;
                node.PrePaint();
                Assert.Equal(word + " · —", Sub().Text);

                node.StaleOverride = null;
                node.PrePaint();
                Assert.Equal(before, Sub().Text);
                Assert.False(Sub().Italic);
            }
        }

        [AvaloniaFact]
        public void Badge_SitsInsideTheCard_NeverOverhangsTheCorner() {
            Fixture fx = NewFixture();
            SupplierNodeElement error = Supplier(fx, fx.NewItem("ghost-item", isMissing: true));
            Assert.Equal(NodeState.Error, error.ViewModel.State);
            Assert.Equal(new NodeBadge(NodeBadgeKind.Error, "Error"), error.Badge());
            Assert.Equal(1f, error.BorderRing().StrokeWidth);
            Assert.Equal(Resolve("Foreman.Color.Error"), error.BorderRing().Color);
            Assert.Equal(Resolve("Foreman.Color.ControlBackground"), error.CardGeometry().Fill);

            foreach (double step in Steps) {
                float s = (float)step;
                WindowScaler.SetScale(step);
                Painted(error);
                SKRect bounds = GraphBounds(error);
                SKRect badge = error.BadgeRect();
                CanvasTokens tokens = TokenBridge.Current;

                Assert.True(bounds.Contains(badge), $"{badge} outside {bounds}");
                Assert.Equal(16 * s, badge.Height, 3);
                Assert.Equal(bounds.Right - (12 * s), badge.Right, 3);
                Assert.Equal(bounds.Top + (52 * s), badge.Top, 3);
                float word = NodeCardPainter.Measure("Error", tokens.CanvasFontSize, tokens.CanvasEmphasizedWeight);
                Assert.Equal((6 + 10 + 3 + 6) * s + word, badge.Width, 2);
                Assert.True(error.HasSubElementAt(new DrawingPoint((int)badge.MidX, (int)badge.MidY)));
                Assert.False(error.HasSubElementAt(new DrawingPoint((int)bounds.Left + 1, (int)bounds.Top + 1)));
                AssertRect(Clipped(SKRect.Inflate(badge, 2 * s, 2 * s), bounds), Assert.NotNull(error.BadgeHitRect()));
            }
            WindowScaler.SetScale(1.0);

            //A passthrough that would carry a badge draws as the card; a drawn bar never has one.
            Fixture piped = NewFixture();
            var stated = new StatedPassthrough(LinkedPassthrough(piped, piped.NewItem("coal")));
            var passthrough = new PassthroughNodeElement(piped.Context, stated);
            int edits = 0;
            piped.Context.EditNode = _ => edits++;
            foreach (double step in Steps) {
                float s = (float)step;
                WindowScaler.SetScale(step);
                stated.Set(null);
                Painted(passthrough);
                DrawingPoint centre = passthrough.LocalToGraph(DrawingPoint.Empty);
                Assert.Equal(72 * s, passthrough.Width, 0.5f);
                Assert.Null(passthrough.Badge());
                Assert.Null(passthrough.BadgeHitRect());

                foreach ((NodeState state, NodeBadgeKind kind, string word) in new[] { (NodeState.Error, NodeBadgeKind.Error, "Error"), (NodeState.Warning, NodeBadgeKind.Warning, "Warning") }) {
                    stated.Set(state);
                    Painted(passthrough);
                    CanvasTokens tokens = TokenBridge.Current;
                    float badgeWidth = (2 * tokens.Space15) + tokens.Glyph10 + tokens.NodeGapTight + NodeCardPainter.Measure(word, tokens.CanvasFontSize, tokens.CanvasEmphasizedWeight);
                    float content = NodeCardPainter.Measure("Passthrough", tokens.CanvasFontSize, 400) + (6 * s) + badgeWidth + (24 * s);
                    Assert.Equal(Math.Max(144 * s, MathF.Ceiling(content)), passthrough.Width, 0.5f);
                    Assert.Equal(96 * s, passthrough.Height, 0.5f);
                    Assert.Equal(centre, passthrough.LocalToGraph(DrawingPoint.Empty));
                    Assert.Equal(new NodeBadge(kind, word), passthrough.Badge());

                    SKRect bounds = GraphBounds(passthrough);
                    SKRect badge = passthrough.BadgeRect();
                    Assert.Equal(bounds.Top + (60 * s), badge.MidY, 3);
                    Assert.Equal(bounds.Right - (12 * s), badge.Right, 3);
                    NodeText sub = passthrough.Texts.Single(text => text.Role == NodeTextRole.Detail);
                    Assert.Equal("Passthrough", sub.Text);
                    SKRect subRect = sub.Rect;
                    subRect.Offset(centre.X, centre.Y);
                    Assert.False(subRect.IntersectsWith(badge), $"{subRect} meets {badge}");

                    SKRect hit = Assert.NotNull(passthrough.BadgeHitRect());
                    AssertRect(Clipped(SKRect.Inflate(badge, 2 * s, 2 * s), bounds), hit);
                    Assert.Equal(20 * s, hit.Height, 3);
                    var inside = new DrawingPoint((int)hit.MidX, (int)hit.MidY);
                    Assert.True(passthrough.HasSubElementAt(inside));
                    passthrough.MouseUpLeft(inside);
                    Assert.Equal(0, edits);
                    var outside = new DrawingPoint((int)Math.Floor(hit.Left - s), (int)hit.MidY);
                    Assert.False(passthrough.HasSubElementAt(outside));
                    passthrough.MouseUpLeft(outside);
                    Assert.Equal(1, edits);
                    edits = 0;
                }

                stated.Set(null);
                Painted(passthrough);
                Assert.Equal(72 * s, passthrough.Width, 0.5f);
                Assert.Equal(96 * s, passthrough.Height, 0.5f);
                Assert.Equal(centre, passthrough.LocalToGraph(DrawingPoint.Empty));
                Assert.Null(passthrough.Badge());
                Assert.Null(passthrough.BadgeHitRect());
            }
            WindowScaler.SetScale(1.0);

            Fixture warned = NewFixture();
            warned.Quality.Enabled = false;
            SupplierNodeElement warning = Supplier(warned, warned.NewItem("iron-ore"));
            Assert.Equal(NodeState.Warning, warning.ViewModel.State);
            Assert.Equal(new NodeBadge(NodeBadgeKind.Warning, "Warning"), warning.Badge());
            Assert.True(GraphBounds(warning).Contains(warning.BadgeRect()));
            Assert.True(warning.HasSubElementAt(new DrawingPoint((int)warning.BadgeRect().MidX, (int)warning.BadgeRect().MidY)));

            using CanvasFixture canvas = CanvasFixture.WithVanilla();
            IRecipe gear = canvas.Cache.Recipes["iron-gear-wheel"];
            NodeId id = canvas.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(gear, canvas.Cache.DefaultQuality!), DrawingPoint.Empty);
            BaseNodeElement recipe = Painted(canvas.Viewer.NodeElementDictionary[id]);
            try {
                gear.Enabled = false;
                canvas.Viewer.Context.LevelOfDetail = LevelOfDetail.High;
                recipe.PrePaint();
                Assert.Equal("Unavailable", recipe.Badge()?.Text);
                SKRect bounds = GraphBounds(recipe);
                SKRect badge = recipe.BadgeRect();
                Assert.Equal(bounds.Left + 12, badge.Left, 3);
                Assert.Equal(bounds.Top + 84, badge.Top, 3);

                canvas.Viewer.Context.LevelOfDetail = LevelOfDetail.Low;
                recipe.PrePaint();
                bounds = GraphBounds(recipe);
                badge = recipe.BadgeRect();
                Assert.True(bounds.Contains(badge));
                Assert.Equal(bounds.Right - 12, badge.Right, 3);
                Assert.Equal(bounds.Top + 52 + 2, badge.Top, 3);
            } finally {
                gear.Enabled = true;
                canvas.Viewer.Context.LevelOfDetail = LevelOfDetail.Medium;
            }
        }

        [AvaloniaFact]
        public void HoveredTab_TakesControlBackgroundHover_NodeUnchanged() {
            using CanvasFixture fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            BaseNodeElement node = fx.Viewer.NodeElements.Single();
            ItemTabElement tab = Tab(node, LinkType.Output);
            NodeCardGeometry card = node.CardGeometry();
            RingGeometry border = node.BorderRing();
            Assert.Equal(Resolve("Foreman.Color.WellBackground"), tab.TabGeometry().Fill);

            string tabTooltip = tab.Item.FriendlyName!;

            fx.MoveTo(outputTab);
            Assert.Equal(Resolve("Foreman.Color.ControlBackgroundHover"), tab.TabGeometry().Fill);
            Assert.Equal(card, node.CardGeometry());
            Assert.Equal(border, node.BorderRing());
            Assert.Empty(fx.Control.HoverTooltips());
            AnimationClock.Settle(400);
            Assert.Empty(fx.Control.HoverTooltips());
            AnimationClock.Settle(600);
            Assert.Contains(fx.Control.HoverTooltips(), tooltip => tooltip.Text == tabTooltip);

            fx.MoveTo(fx.Control.Viewport.GraphToScreen(new DrawingPoint(0, 0)));
            Assert.Equal(Resolve("Foreman.Color.WellBackground"), tab.TabGeometry().Fill);
            Assert.Empty(fx.Control.HoverTooltips());
            AnimationClock.Settle(400);
            Assert.Empty(fx.Control.HoverTooltips());
            AnimationClock.Settle(600);
            Assert.NotEmpty(fx.Control.HoverTooltips());
            Assert.DoesNotContain(fx.Control.HoverTooltips(), tooltip => tooltip.Text == tabTooltip);
        }
    }
}
