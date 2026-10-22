using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Foreman.Mac.Kit.Appearance;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;
using AvColor = Avalonia.Media.Color;
using DrawingPoint = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public sealed class NodeGeometryTests : IDisposable {
        private const int Half = 200;
        private static readonly double[] Steps = [1.0, 1.5];

        public void Dispose() {
            WindowScaler.SetScale(1.0);
            Motion.ReduceMotion = false;
            ((App)Application.Current!).ApplyTheme(ThemeMode.System);
        }

        private sealed class HighContrastAppearance : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.High;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        private static SKColor Over(SKColor top, SKColor bottom) {
            float a = top.Alpha / 255f;
            byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
            return new SKColor(Mix(top.Red, bottom.Red), Mix(top.Green, bottom.Green), Mix(top.Blue, bottom.Blue), 255);
        }

        private static void AssertClose(SKColor expected, SKColor actual) =>
            Assert.True(Math.Abs(expected.Red - actual.Red) <= 2 && Math.Abs(expected.Green - actual.Green) <= 2 && Math.Abs(expected.Blue - actual.Blue) <= 2 && Math.Abs(expected.Alpha - actual.Alpha) <= 2,
                $"expected {expected}, got {actual}");

        //---- synthetic fixture: spoil and plant need item data the vanilla preset lacks ----

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required NodeElementContext Context { get; init; }

            public ItemPrototype NewItem(string name) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", false);
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
            var context = new NodeElementContext(session.View, new Viewport(2 * Half, 2 * Half));
            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Graph = graph, Session = session, Context = context };
        }

        private static T Last<T>(Fixture fx) where T : INodeViewModel {
            fx.Graph.UpdateNodeValues();
            return fx.Session.View.Nodes.OfType<T>().Last();
        }

        private static SupplierNodeElement Supplier(Fixture fx, string itemName) {
            fx.Graph.CreateSupplierNode(fx.Pair(fx.NewItem(itemName)), DrawingPoint.Empty);
            return Primed(new SupplierNodeElement(fx.Context, Last<ISupplierNodeViewModel>(fx)));
        }

        private static ConsumerNodeElement Consumer(Fixture fx, string itemName) {
            fx.Graph.CreateConsumerNode(fx.Pair(fx.NewItem(itemName)), DrawingPoint.Empty);
            return Primed(new ConsumerNodeElement(fx.Context, Last<IConsumerNodeViewModel>(fx)));
        }

        private static SpoilNodeElement Spoil(Fixture fx) {
            ItemPrototype input = fx.NewItem("fish");
            ItemPrototype output = fx.NewItem("spoiled-fish");
            input.SpoilResult = output;
            fx.Graph.CreateSpoilNode(fx.Pair(input), output, DrawingPoint.Empty);
            return Primed(new SpoilNodeElement(fx.Context, Last<ISpoilNodeViewModel>(fx)));
        }

        private static PlantNodeElement Plant(Fixture fx) {
            ItemPrototype seed = fx.NewItem("seed");
            var process = new PlantProcessPrototype(fx.Cache, "plant-seed");
            process.InternalOneWayAddProduct(fx.NewItem("wheat"), 1);
            process.Seed = seed;
            seed.PlantResult = process;
            fx.Graph.CreatePlantNode(process, fx.Quality, DrawingPoint.Empty);
            return Primed(new PlantNodeElement(fx.Context, Last<IPlantNodeViewModel>(fx)));
        }

        private static PassthroughNodeElement Passthrough(Fixture fx, string itemName, bool bar) {
            ItemQualityPair pair = fx.Pair(fx.NewItem(itemName));
            BaseNode passthrough = fx.Graph.CreatePassthroughNode(pair, DrawingPoint.Empty);
            if (bar) {
                fx.Graph.CreateLink(fx.Graph.CreateSupplierNode(pair, new DrawingPoint(0, -140)), passthrough, pair);
                fx.Graph.CreateLink(passthrough, fx.Graph.CreateConsumerNode(pair, new DrawingPoint(0, 140)), pair);
                ((PassthroughNodeController)fx.Graph.RequestNodeController(passthrough)!).SetSimpleDraw(true);
            }
            return Primed(new PassthroughNodeElement(fx.Context, Last<IPassthroughNodeViewModel>(fx)));
        }

        private static T Primed<T>(T element) where T : BaseNodeElement {
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }

        private static void SetScale(double step, params BaseNodeElement[] elements) {
            WindowScaler.SetScale(step);
            foreach (BaseNodeElement element in elements)
                Primed(element);
        }

        private static SKSurface Render(BaseNodeElement element) {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Translate(Half, Half);
            element.PrePaint();
            element.Paint(surface.Canvas, NodeDrawingStyle.Regular);
            return surface;
        }

        private static SKColor Pixel(SKSurface surface, float graphX, float graphY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(Half + (int)Math.Floor(graphX), Half + (int)Math.Floor(graphY));
        }

        private static SKColor Resolve(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            var color = (AvColor)value!;
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        private static SKRect GraphBounds(BaseNodeElement node) {
            DrawingPoint centre = node.LocalToGraph(DrawingPoint.Empty);
            return SKRect.Create(centre.X - (node.Width / 2f), centre.Y - (node.Height / 2f), node.Width, node.Height);
        }

        private static SKRect ToGraphRect(BaseNodeElement node, SKRect local) {
            DrawingPoint centre = node.LocalToGraph(DrawingPoint.Empty);
            local.Offset(centre.X, centre.Y);
            return local;
        }

        private static IEnumerable<NodeText> AllTexts(BaseNodeElement node) =>
            node.Texts.Concat(node.SubElements.OfType<ItemTabElement>().Select(tab => tab.RateText));

        //GlyphPaths mirrors the kit's 20 pt master, so a painter glyph and the kit glyph are the same path.
        private static void AssertMirrorsKitMaster(string key) {
            string glyphs = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Foreman.Mac.Kit", "Themes", "Glyphs.axaml");
            XElement master = XDocument.Load(glyphs).Descendants().Single(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key + ".20");
            using SKPath kit = SKPath.ParseSvgPathData(master.Value);
            Assert.Equal(kit.ToSvgPathData(), GlyphPaths.Get(key).ToSvgPathData());
        }

        //---- vanilla recipe helpers ----

        private static RecipeNodeElement IronGearWheel(CanvasFixture fx, DrawingPoint? at = null) {
            var pair = new RecipeQualityPair(fx.Cache.Recipes["iron-gear-wheel"], fx.Cache.DefaultQuality!);
            NodeId id = fx.Viewer.Session.Editor.CreateRecipeNode(pair, at ?? DrawingPoint.Empty);
            return Primed((RecipeNodeElement)fx.Viewer.NodeElementDictionary[id]);
        }

        private static RecipeNodeController Controller(CanvasFixture fx, BaseNodeElement node) =>
            (RecipeNodeController)fx.Viewer.Session.Editor.RequestNodeController(node.ViewModel.Id)!;

        //---- §01 fidelity assertions ----

        [AvaloniaFact]
        public void RecipeNode_OneInOneOut_Is144By144High_144By96Low_AndScales() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = IronGearWheel(fx);
            Assert.Single(recipe.ViewModel.Inputs);
            Assert.Single(recipe.ViewModel.Outputs);

            foreach (double step in Steps) {
                SetScale(step);
                fx.Viewer.Context.LevelOfDetail = LevelOfDetail.High;
                Primed(recipe);
                Assert.Equal((144 * step, 144 * step), ((double)recipe.Width, (double)recipe.Height));

                fx.Viewer.Context.LevelOfDetail = LevelOfDetail.Low;
                Primed(recipe);
                Assert.Equal((144 * step, 96 * step), ((double)recipe.Width, (double)recipe.Height));
            }

            //A unit switch re-derives every width about fixed centres.
            fx.Viewer.Context.LevelOfDetail = LevelOfDetail.High;
            var circuit = new RecipeQualityPair(fx.Cache.Recipes["advanced-circuit"], fx.Cache.DefaultQuality!);
            RecipeNodeElement wide = Primed((RecipeNodeElement)fx.Viewer.NodeElementDictionary[fx.Viewer.Session.Editor.CreateRecipeNode(circuit, new DrawingPoint(400, 0))]);
            RecipeNodeElement[] recipes = [recipe, wide];
            DrawingPoint[] centres = [.. recipes.Select(node => node.LocalToGraph(DrawingPoint.Empty))];
            fx.Viewer.Graph.SelectedRateUnit = ProductionGraph.RateUnit.Per30Min;
            fx.Viewer.Graph.UpdateNodeValues();
            foreach (double step in Steps) {
                float s = (float)step;
                SetScale(step, recipes);
                Assert.Equal(centres, recipes.Select(node => node.LocalToGraph(DrawingPoint.Empty)));
                foreach (RecipeNodeElement node in recipes) {
                    ItemTabElement[] tabs = [.. node.SubElements.OfType<ItemTabElement>()];
                    Assert.All(tabs, tab => Assert.EndsWith("/30\u00A0min", tab.RateText.Text));
                    float Span(LinkType type) {
                        ItemTabElement[] edge = [.. tabs.Where(tab => tab.LinkType == type)];
                        return edge.Sum(tab => (float)tab.Width) + (6 * s * Math.Max(0, edge.Length - 1));
                    }
                    Assert.Equal(Math.Max(Math.Round(144 * step), Math.Ceiling(Math.Max(Span(LinkType.Input), Span(LinkType.Output)) + (24 * s))), node.Width, 0);
                }
                Assert.True(wide.Width > 144 * step);
            }
        }

        [AvaloniaFact]
        public void SupplierConsumerSpoil_Are144By96_PassthroughBarIs72By96() {
            Fixture fx = NewFixture();
            BaseNodeElement[] cards = [Supplier(fx, "ore"), Consumer(fx, "plate"), Spoil(fx)];
            PassthroughNodeElement bar = Passthrough(fx, "coal", bar: true);
            PassthroughNodeElement passthroughCard = Passthrough(fx, "iron", bar: false);
            PassthroughNodeElement limited = Passthrough(fx, "copper", bar: false);
            BaseNodeController limitedController = fx.Controller(limited.ViewModel);
            limitedController.SetRateType(RateType.Manual);
            limitedController.SetDesiredSetValue(1e21);
            fx.Context.RateUnitLookup = () => ProductionGraph.RateUnit.Per30Min;

            foreach (double step in Steps) {
                float s = (float)step;
                SetScale(step, [.. cards, bar, passthroughCard, limited]);
                foreach (BaseNodeElement card in cards)
                    Assert.Equal((144 * step, 96 * step), ((double)card.Width, (double)card.Height));
                Assert.Equal((72 * step, 96 * step), ((double)bar.Width, (double)bar.Height));
                Assert.Equal((144 * step, 96 * step), ((double)passthroughCard.Width, (double)passthroughCard.Height));

                NodeText chip = limited.Texts.Single(text => text.Role == NodeTextRole.Chip);
                Assert.Equal("Limit 1000000000000G/30\u00A0min", chip.Text);
                Assert.True(chip.Rect.Width + (12 * s) > 120 * s);
                Assert.Equal(Math.Ceiling(chip.Rect.Width + (12 * s) + (24 * s)), limited.Width, 0);
                Assert.Equal(DrawingPoint.Empty, limited.LocalToGraph(DrawingPoint.Empty));
            }
            fx.Context.RateUnitLookup = null;
            WindowScaler.SetScale(1.0);

            //A bar that falls back to the card (here by losing its output link) grows about its stored centre.
            using CanvasFixture solved = CanvasFixture.WithVanilla();
            var stone = new ItemQualityPair(solved.Cache.Items["stone"], solved.Cache.DefaultQuality!);
            var centre = new DrawingPoint(300, 40);
            IProductionGraphEditor editor = solved.Viewer.Session.Editor;
            NodeId source = editor.CreateSupplierNode(stone, new DrawingPoint(300, -140));
            NodeId passthroughId = editor.CreatePassthroughNode(stone, centre);
            NodeId sink = editor.CreateConsumerNode(stone, new DrawingPoint(300, 220));
            editor.CreateLink(source, passthroughId, stone);
            LinkId outputLink = editor.CreateLink(passthroughId, sink, stone);
            ((PassthroughNodeController)editor.RequestNodeController(passthroughId)!).SetSimpleDraw(true);
            solved.Viewer.Graph.UpdateNodeValues();
            var falling = Primed((PassthroughNodeElement)solved.Viewer.NodeElementDictionary[passthroughId]);
            Assert.Equal((72, 96), (falling.Width, falling.Height));
            var inspector = new FlowSegmentViewModel(falling.ViewModel, solved.Viewer);
            inspector.SimplifyThroughput = false;
            falling.PrePaint();
            Assert.Equal((144, 96), (falling.Width, falling.Height));
            Assert.False(falling.TypeStripRect().IsEmpty);
            Assert.All(falling.SubElements.OfType<ItemTabElement>(), tab => Assert.False(tab.HideItemTab));
            Assert.Equal(centre, falling.LocalToGraph(DrawingPoint.Empty));
            inspector.SimplifyThroughput = true;
            falling.PrePaint();
            Assert.Equal((72, 96), (falling.Width, falling.Height));
            Assert.Equal(centre, falling.LocalToGraph(DrawingPoint.Empty));
            editor.DeleteLink(outputLink);
            solved.Viewer.Graph.UpdateNodeValues();
            Primed(falling);
            Assert.Equal((144, 96), (falling.Width, falling.Height));
            Assert.Equal(centre, falling.LocalToGraph(DrawingPoint.Empty));

            //Toggling simple draw, per node or as the graph default, moves no centre.
            BaseNodeElement[] all = [.. cards, bar, passthroughCard, limited];
            DrawingPoint[] before = [.. all.Select(node => node.LocalToGraph(DrawingPoint.Empty))];
            foreach (PassthroughNodeElement passthrough in new[] { bar, passthroughCard }) {
                var controller = (PassthroughNodeController)fx.Controller(passthrough.ViewModel);
                controller.SetSimpleDraw(!((IPassthroughNodeViewModel)passthrough.ViewModel).SimpleDraw);
            }
            fx.Graph.DefaultToSimplePassthroughNodes = !fx.Graph.DefaultToSimplePassthroughNodes;
            foreach (BaseNodeElement node in all)
                Primed(node);
            Assert.Equal(before, all.Select(node => node.LocalToGraph(DrawingPoint.Empty)));

            //A save laid out at 72 loads at its stored centres; the wider card is not re-centred or written back.
            using CanvasFixture saved = CanvasFixture.WithVanilla();
            var ore = new ItemQualityPair(saved.Cache.Items["iron-ore"], saved.Cache.DefaultQuality!);
            NodeId savedPassthroughId = saved.Viewer.Session.Editor.CreatePassthroughNode(ore, new DrawingPoint(200, 0));
            saved.Viewer.Session.Editor.CreateSupplierNode(ore, new DrawingPoint(300, 0));
            ((PassthroughNodeController)saved.Viewer.Session.Editor.RequestNodeController(savedPassthroughId)!).SetSimpleDraw(true);
            string json = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(saved.Viewer, saved.Cache));

            using CanvasFixture loaded = CanvasFixture.WithVanilla();
            Assert.True(loaded.Viewer.LoadDocument(loaded.Cache, json).Success);
            foreach (BaseNodeElement node in loaded.Viewer.NodeElementDictionary.Values)
                Primed(node);
            PassthroughNodeElement loadedPassthrough = loaded.Viewer.NodeElementDictionary.Values.OfType<PassthroughNodeElement>().Single();
            Assert.Equal(new DrawingPoint(200, 0), loadedPassthrough.LocalToGraph(DrawingPoint.Empty));
            Assert.Equal(new DrawingPoint(300, 0), loaded.Viewer.NodeElementDictionary.Values.OfType<SupplierNodeElement>().Single().LocalToGraph(DrawingPoint.Empty));
            Assert.Equal(144, loadedPassthrough.Width);
            Assert.Equal(GraphSaveCodec.WriteProductionGraphToString(saved.Viewer.Graph), GraphSaveCodec.WriteProductionGraphToString(loaded.Viewer.Graph));
        }

        [AvaloniaFact]
        public void CardGeometry_IsTheBounds_Radius10_ControlBackground_BorderStrong1_WithCardShadowInLightOnly() {
            App app = (App)Application.Current!;
            Fixture fx = NewFixture();
            SupplierNodeElement supplier = Supplier(fx, "ore");

            foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark }) {
                app.ApplyTheme(mode);
                foreach (double step in Steps) {
                    SetScale(step, supplier);
                    NodeCardGeometry card = supplier.CardGeometry();

                    Assert.Equal(GraphBounds(supplier), card.Rect);
                    Assert.Equal(10f, card.Radius);
                    Assert.Equal(Resolve("Foreman.Color.ControlBackground"), card.Fill);
                    Assert.Equal(1f, card.StrokeWidth);
                    Assert.Equal(Resolve("Foreman.Color.BorderStrong"), card.Stroke);
                    Assert.Equal(mode == ThemeMode.Light, card.HasShadow);

                    using SKSurface surface = Render(supplier);
                    Assert.Equal(card.Fill, Pixel(surface, card.Rect.Right - (40 * (float)step), card.Rect.MidY));
                }
            }

            AppearanceHub? previousHub = AppearanceHub.Current;
            AppearanceHub? hub = null;
            try {
                hub = new AppearanceHub(app, new HighContrastAppearance());
                AppearanceHub.Current = hub;
                hub.Refresh();
                TokenBridge.Refresh();
                Primed(supplier);
                NodeCardGeometry contrast = supplier.CardGeometry();

                Assert.Equal(Resolve("Foreman.Color.NodeTabBorderContrast"), contrast.Stroke);
                Assert.Equal(1f, contrast.StrokeWidth);
            } finally {
                hub?.Dispose();
                AppearanceHub.Current = previousHub;
                TokenBridge.Refresh();
            }
        }

        [AvaloniaFact]
        public void TypeStripRect_IsLeadingEdge_Inset12_ThreeWide_SpoilAndPlantResolveToNodeRecipe() {
            Fixture fx = NewFixture();
            (BaseNodeElement Node, string Key)[] cases = [
                (Supplier(fx, "ore"), "Foreman.Color.NodeSupplier"),
                (Consumer(fx, "plate"), "Foreman.Color.NodeConsumer"),
                (Passthrough(fx, "coal", bar: false), "Foreman.Color.NodePassthrough"),
                (Spoil(fx), "Foreman.Color.NodeRecipe"),
                (Plant(fx), "Foreman.Color.NodeRecipe"),
            ];

            foreach (double step in Steps) {
                foreach ((BaseNodeElement node, string key) in cases) {
                    SetScale(step, node);
                    SKRect bounds = GraphBounds(node);
                    float s = (float)step;

                    Assert.Equal(SKRect.Create(bounds.Left, bounds.Top + (12 * s), 3 * s, bounds.Height - (24 * s)), node.TypeStripRect());
                    Assert.Equal(Resolve(key), node.TypeStripColor);

                    using SKSurface surface = Render(node);
                    Assert.Equal(node.TypeStripColor, Pixel(surface, bounds.Left + (1.5f * s), bounds.MidY));
                }
            }

            PassthroughNodeElement bar = Passthrough(fx, "iron", bar: true);
            Assert.True(bar.TypeStripRect().IsEmpty);
        }

        [AvaloniaFact]
        public void TitleRect_Top18_Height26_NameIsCallout600_DetailIsCanvasFontSize_NothingBelow10() {
            using CanvasFixture vanilla = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = IronGearWheel(vanilla);
            Fixture fx = NewFixture();
            SupplierNodeElement longName = Supplier(fx, "an-item-whose-name-is-far-too-long-for-one-card");
            SpoilNodeElement spoil = Spoil(fx);
            SupplierNodeElement key = Supplier(fx, "ore");
            BaseNodeController keyController = fx.Controller(key.ViewModel);
            keyController.SetKeyNode(true);
            keyController.SetKeyNodeTitle("Blue line");
            BaseNodeElement[] nodes = [recipe, longName, Consumer(fx, "plate"), spoil, Plant(fx), Passthrough(fx, "coal", bar: false), key];

            foreach (double step in Steps) {
                float s = (float)step;
                foreach (BaseNodeElement node in nodes) {
                    SetScale(step, node);
                    SKRect bounds = GraphBounds(node);

                    Assert.Equal(bounds.Top + (18 * s), node.TitleTextRect().Top, 3);
                    Assert.Equal(26 * s, node.TitleTextRect().Height, 3);
                    Assert.Equal(600, node.NameFontWeight);
                    Assert.Equal(12 * s, node.NameFontSize, 3);
                    Assert.Equal(10 * s, node.DetailFontSize, 3);
                    Assert.Contains(node.Texts, text => text.Role == NodeTextRole.Name && text.Size == node.NameFontSize);
                    Assert.Contains(node.DrawnTexts, text => text.Role == NodeTextRole.Name);
                    Assert.All(AllTexts(node), text => Assert.True(text.Size >= (10 * s) - 0.001f, $"'{text.Text}' at {text.Size}"));
                    Assert.All(node.SubElements.OfType<ItemTabElement>(), tab => Assert.True(tab.DrawsRate));
                }

                Assert.Equal(13 * s, recipe.Texts.Single(text => text.Role == NodeTextRole.Count).Size, 2);
                SKRect glyph = spoil.DetailGlyphRect();
                Assert.Equal((12 * s, 12 * s), (glyph.Width, glyph.Height));

                SKRect keyBounds = GraphBounds(key);
                NodeText keyTitle = key.Texts.Single(text => text.Role == NodeTextRole.KeyTitle);
                Assert.Equal("Blue line", keyTitle.Text);
                Assert.Equal(10 * s, keyTitle.Size, 3);
                Assert.Equal(600, keyTitle.Weight);
                Assert.Equal(Resolve("Foreman.Color.AccentText"), keyTitle.Color);
                SKRect keyTitleRect = ToGraphRect(key, keyTitle.Rect);
                Assert.Equal(keyBounds.Top + (18 * s), keyTitleRect.Top, 3);
                Assert.Equal(12 * s, keyTitleRect.Height, 3);
                SKRect keyName = ToGraphRect(key, key.Texts.Single(text => text.Role == NodeTextRole.Name).Rect);
                Assert.Equal(14 * s, keyName.Height, 3);
                Assert.Equal(keyBounds.Top + (44 * s), keyName.Bottom, 3);
            }

            NodeText name = longName.Texts.Single(text => text.Role == NodeTextRole.Name);
            Assert.EndsWith("…", name.Text);
            SKRect title = longName.TitleTextRect();
            List<TooltipInfo> tooltips = longName.GetToolTips(new DrawingPoint((int)title.MidX, (int)title.MidY));
            Assert.Contains(tooltips, tip => tip.Text == "an-item-whose-name-is-far-too-long-for-one-card");
        }

        [AvaloniaFact]
        public void ItemTab_Height22_Radius6_Stroke1Point5_NodeTabBorderOnWell_Overhang13() {
            Fixture fx = NewFixture();
            ItemQualityPair pair = fx.Pair(fx.NewItem("ore"));
            BaseNode supplierNode = fx.Graph.CreateSupplierNode(pair, DrawingPoint.Empty);
            fx.Graph.CreateLink(supplierNode, fx.Graph.CreateConsumerNode(pair, new DrawingPoint(0, 200)), pair);
            fx.Graph.UpdateNodeValues();
            SupplierNodeElement supplier = Primed(new SupplierNodeElement(fx.Context, fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single()));
            ItemTabElement tab = supplier.SubElements.OfType<ItemTabElement>().Single();

            foreach (double step in Steps) {
                float s = (float)step;
                SetScale(step, supplier);
                TabGeometry geometry = tab.TabGeometry();

                Assert.Equal(22 * s, geometry.Rect.Height, 3);
                Assert.True(geometry.Rect.Width >= (44 * s) - 0.001f);
                Assert.Equal(6f, geometry.Radius);
                Assert.Equal(1.5f, geometry.StrokeWidth);
                Assert.Equal(Resolve("Foreman.Color.NodeTabBorder"), geometry.Stroke);
                Assert.Equal(Resolve("Foreman.Color.WellBackground"), geometry.Fill);
                Assert.Equal(13 * s, geometry.OverhangOutside, 3);
                Assert.Equal(GraphBounds(supplier).Top - (13 * s), geometry.Rect.Top, 0);
                Assert.Equal(10 * s, tab.RateText.Size, 3);
                Assert.Equal("0/s", tab.RateText.Text);

                using SKSurface surface = Render(supplier);
                AssertClose(geometry.Fill, Pixel(surface, geometry.Rect.MidX, geometry.Rect.Top + (2.5f * s)));
                AssertClose(Over(geometry.Stroke, geometry.Fill), Pixel(surface, geometry.Rect.MidX, geometry.Rect.Top + 0.75f));
            }

            //Every rate unit draws the canvas number and its suffix; tabs and chips grow to fit and never trim.
            fx.Context.RateUnitLookup = () => fx.Graph.SelectedRateUnit;
            SupplierNodeElement fixedSupplier = Supplier(fx, "coal");
            ConsumerNodeElement fixedConsumer = Consumer(fx, "plate");
            PassthroughNodeElement limited = Passthrough(fx, "stone", bar: false);
            foreach (BaseNodeElement node in new BaseNodeElement[] { fixedSupplier, fixedConsumer, limited }) {
                BaseNodeController controller = fx.Controller(node.ViewModel);
                controller.SetRateType(RateType.Manual);
                controller.SetDesiredSetValue(1.5);
            }
            ItemTabElement fixedTab = fixedSupplier.SubElements.OfType<ItemTabElement>().Single();
            string point = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            (ProductionGraph.RateUnit Unit, string Text)[] units = [
                (ProductionGraph.RateUnit.Per1Sec, "1" + point + "5/s"),
                (ProductionGraph.RateUnit.Per1Min, "90/min"),
                (ProductionGraph.RateUnit.Per5Min, "450/5\u00A0min"),
                (ProductionGraph.RateUnit.Per10Min, "900/10\u00A0min"),
                (ProductionGraph.RateUnit.Per30Min, "2700/30\u00A0min"),
                (ProductionGraph.RateUnit.Per1Hour, "5400/h"),
            ];
            foreach (double step in Steps) {
                float s = (float)step;
                foreach ((ProductionGraph.RateUnit unit, string text) in units) {
                    fx.Graph.SelectedRateUnit = unit;
                    SetScale(step, fixedSupplier, fixedConsumer, limited);

                    Assert.Equal(text, fixedTab.RateText.Text);
                    Assert.Equal(10 * s, fixedTab.RateText.Size, 3);
                    Assert.Equal(Math.Ceiling(Math.Max(44 * s, (29 * s) + NodeCardPainter.Measure(text, 10 * s, 600))), fixedTab.Width, 0);
                    Assert.Equal("Fixed " + text, fixedSupplier.Texts.Single(t => t.Role == NodeTextRole.Chip).Text);
                    Assert.Equal("Fixed " + text, fixedConsumer.Texts.Single(t => t.Role == NodeTextRole.Chip).Text);
                    Assert.Equal("Limit " + text, limited.Texts.Single(t => t.Role == NodeTextRole.Chip).Text);
                    foreach (BaseNodeElement node in new BaseNodeElement[] { fixedSupplier, fixedConsumer, limited })
                        Assert.DoesNotContain(AllTexts(node), t => t.Text.Contains('…'));
                }
            }
            fx.Graph.SelectedRateUnit = ProductionGraph.RateUnit.Per1Sec;
        }

        [AvaloniaFact]
        public void NoDrawnString_IsARetiredUpstreamLabel_AndAManualSupplierDrawsFixedChip() {
            string[] retired = ["Infinite Source:", "Exact Input:", "Infinite Sink:", "Required Output:", "-Limit-"];
            Fixture fx = NewFixture();
            SupplierNodeElement autoSupplier = Supplier(fx, "ore");
            SupplierNodeElement manualSupplier = Supplier(fx, "coal");
            ConsumerNodeElement autoConsumer = Consumer(fx, "plate");
            ConsumerNodeElement manualConsumer = Consumer(fx, "gear");
            PassthroughNodeElement autoPassthrough = Passthrough(fx, "stone", bar: false);
            PassthroughNodeElement manualPassthrough = Passthrough(fx, "brick", bar: false);
            foreach (BaseNodeElement manual in new BaseNodeElement[] { manualSupplier, manualConsumer, manualPassthrough }) {
                BaseNodeController controller = fx.Controller(manual.ViewModel);
                controller.SetRateType(RateType.Manual);
                controller.SetDesiredSetValue(90);
            }
            BaseNodeElement[] nodes = [autoSupplier, manualSupplier, autoConsumer, manualConsumer, autoPassthrough, manualPassthrough, Spoil(fx), Plant(fx)];
            foreach (BaseNodeElement node in nodes)
                Render(node).Dispose();

            foreach (BaseNodeElement node in nodes)
                Assert.DoesNotContain(AllTexts(node), text => retired.Contains(text.Text));

            string Detail(BaseNodeElement node) => node.Texts.Single(text => text.Role is NodeTextRole.Detail or NodeTextRole.Chip).Text;
            (NodeTextRole, string) Chip(BaseNodeElement node) => node.Texts.Where(text => text.Role is NodeTextRole.Detail or NodeTextRole.Chip).Select(text => (text.Role, text.Text)).Single();
            Assert.Equal("Source", Detail(autoSupplier));
            Assert.Equal("Output", Detail(autoConsumer));
            Assert.Equal("Passthrough", Detail(autoPassthrough));
            Assert.Equal((NodeTextRole.Chip, "Fixed 90/s"), Chip(manualSupplier));
            Assert.Equal((NodeTextRole.Chip, "Fixed 90/s"), Chip(manualConsumer));
            Assert.Equal((NodeTextRole.Chip, "Limit 90/s"), Chip(manualPassthrough));
            Assert.StartsWith("Spoil · ×", Detail(nodes[6]));
            Assert.StartsWith("Plant · ×", Detail(nodes[7]));
            Assert.Equal("fish Spoilage", nodes[6].Texts.Single(text => text.Role == NodeTextRole.Name).Text);
            Assert.Equal("seed Planting", nodes[7].Texts.Single(text => text.Role == NodeTextRole.Name).Text);

            using CanvasFixture vanilla = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = IronGearWheel(vanilla);
            RecipeNodeController recipeController = Controller(vanilla, recipe);
            recipeController.SetRateType(RateType.Manual);
            recipeController.SetDesiredSetValue(4);
            Primed(recipe);
            Assert.Equal("Fixed ×4", recipe.Texts.Single(text => text.Role == NodeTextRole.Chip).Text);
            Assert.DoesNotContain(recipe.Texts, text => text.Role == NodeTextRole.Count);

            System.Globalization.NumberFormatInfo format = System.Globalization.CultureInfo.CurrentCulture.NumberFormat;
            (double Value, string Text)[] numbers = [
                (0, "0"), (0.0004, "<0.001"), (0.125, "0.125"), (1.254, "1.25"), (12.54, "12.5"), (125.4, "125"),
                (9999.4, "9999"), (12540, "12.5k"), (324000, "324k"), (999600, "1M"), (1254000, "1.25M"),
            ];
            foreach ((double value, string text) in numbers) {
                string drawn = NodeCardPainter.Number(value);
                Assert.Equal(text.Replace(".", format.NumberDecimalSeparator), drawn);
                Assert.DoesNotContain("e", drawn);
                Assert.DoesNotContain(" ", drawn);
                Assert.DoesNotContain(format.NumberGroupSeparator, drawn);
            }
            AssertMirrorsKitMaster("Kit.Glyph.RadialSpoil");
            AssertMirrorsKitMaster("Kit.Glyph.RadialPlant");
        }

        //---- status row, low detail, motion ----

        //The state badge leads the status row; the rest follow it after a 6 gap.
        private static float StatusRowStart(RecipeNodeElement recipe, SKRect bounds, float s) {
            if (recipe.Badge() is null)
                return bounds.Left + (12 * s);
            SKRect badge = recipe.BadgeRect();
            Assert.Equal(bounds.Left + (12 * s), badge.Left, 3);
            Assert.Equal(bounds.Top + (84 * s), badge.Top, 3);
            return badge.Right + (6 * s);
        }

        [AvaloniaFact]
        public void StatusRow_PipsCapAtSix_BeaconBadgeAndExtraProdCollapseLeftward() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            fx.Viewer.Context.EnableExtraProductivityForNonMiners = true;
            RecipeNodeElement recipe = IronGearWheel(fx);
            RecipeNodeController controller = Controller(fx, recipe);
            IModule productivity = fx.Cache.Modules["productivity-module"];
            controller.SetAssemblerModules(Enumerable.Repeat(new ModuleQualityPair(productivity, fx.Cache.DefaultQuality!), 8), filterModules: false);
            controller.SetExtraProductivityBonus(0.1);

            foreach (double step in Steps) {
                float s = (float)step;
                SetScale(step, recipe);
                SKRect bounds = GraphBounds(recipe);
                IReadOnlyList<StatusItem> row = recipe.StatusRow;
                float start = StatusRowStart(recipe, bounds, s);

                Assert.DoesNotContain(row, item => item.Kind == StatusItemKind.Beacon);
                Assert.Equal(6, row.Count(item => item.Kind == StatusItemKind.Pip));
                Assert.Equal(start, row[0].Rect.Left, 3);
                Assert.Equal(bounds.Top + (84 * s) + (5 * s), row[0].Rect.Top, 3);
                Assert.Equal(6 * s, row[0].Rect.Width, 3);
                Assert.Equal(9 * s, row[1].Rect.Left - row[0].Rect.Left, 3);
                Assert.Equal(StatusItemKind.ExtraProductivity, row[^1].Kind);
                Assert.Equal(row[^2].Rect.Right + (6 * s), row[^1].Rect.Left, 3);
                Assert.Contains(recipe.Texts, text => text.Role == NodeTextRole.ExtraProductivity && text.Text == "+10%");
            }

            controller.SetBeacon(new BeaconQualityPair(fx.Cache.Beacons["beacon"], fx.Cache.DefaultQuality!));
            controller.SetBeaconCount(2);
            foreach (double step in Steps) {
                float s = (float)step;
                SetScale(step, recipe);
                SKRect bounds = GraphBounds(recipe);
                IReadOnlyList<StatusItem> row = recipe.StatusRow;

                Assert.Equal(StatusItemKind.Beacon, row[0].Kind);
                Assert.Equal(SKRect.Create(StatusRowStart(recipe, bounds, s), bounds.Top + (84 * s), 16 * s, 16 * s), row[0].Rect);
                Assert.Equal(row[0].Rect.Right + (6 * s), row[1].Rect.Left, 3);
                DrawingPoint badgeCentre = recipe.BeaconElement.LocalToGraph(DrawingPoint.Empty);
                Rectangle badge = recipe.BeaconElement.Bounds;
                //Element bounds are whole points; the status row after a state badge can start between them.
                SKRect hit = SKRect.Create(badgeCentre.X + badge.X, badgeCentre.Y + badge.Y, badge.Width, badge.Height);
                Assert.Equal(row[0].Rect.Left, hit.Left, 0.5f);
                Assert.Equal(row[0].Rect.Top, hit.Top, 0.5f);
                Assert.Equal(row[0].Rect.Width, hit.Width, 0.5f);
                Assert.Equal(row[0].Rect.Height, hit.Height, 0.5f);
                SKRect beaconGlyph = recipe.BeaconElement.GlyphRect();
                Assert.Equal((12 * s, 12 * s), (beaconGlyph.Width, beaconGlyph.Height));
            }
            AssertMirrorsKitMaster("Kit.Glyph.Beacon");
            AssertMirrorsKitMaster("Kit.Glyph.Beacon.Fill");
        }

        [AvaloniaFact]
        public void LowDetail_DropsTheStatusRow() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = IronGearWheel(fx);
            RecipeNodeController controller = Controller(fx, recipe);
            controller.SetBeacon(new BeaconQualityPair(fx.Cache.Beacons["beacon"], fx.Cache.DefaultQuality!));
            controller.SetBeaconCount(2);
            Primed(recipe);
            Assert.NotEmpty(recipe.StatusRow);

            fx.Viewer.Context.LevelOfDetail = LevelOfDetail.Low;
            Primed(recipe);

            Assert.Empty(recipe.StatusRow);
            Assert.False(recipe.BeaconElement.Visible);
            Assert.False(recipe.AssemblerElement.Visible);
            Assert.Contains(recipe.Texts, text => text.Role == NodeTextRole.Count);
            Assert.DoesNotContain(recipe.Texts, text => text.Role == NodeTextRole.ExtraProductivity);
        }

        [AvaloniaFact]
        public void CountText_CrossFadesOver120ms_AndSwapsUnderReduceMotion() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement recipe = IronGearWheel(fx);
            var gear = new ItemQualityPair(fx.Cache.Items["iron-gear-wheel"], fx.Cache.DefaultQuality!);
            NodeId consumer = fx.Viewer.Session.Editor.CreateConsumerNode(gear, new DrawingPoint(0, 300));
            fx.Viewer.Session.Editor.CreateLink(recipe.ViewModel.Id, consumer, gear);
            BaseNodeController consumerController = fx.Viewer.Session.Editor.RequestNodeController(consumer)!;
            consumerController.SetRateType(RateType.Manual);
            consumerController.SetDesiredSetValue(1);
            fx.Viewer.Graph.UpdateNodeValues();
            AnimationClock.Settle(200);
            Primed(recipe);
            ItemTabElement outputTab = recipe.SubElements.OfType<ItemTabElement>().Single(tab => tab.LinkType == LinkType.Output);
            string before = recipe.CountLabel.Text;
            string rateBefore = outputTab.RateLabel.Text;

            consumerController.SetDesiredSetValue(4);
            fx.Viewer.Graph.UpdateNodeValues();
            recipe.PrePaint();

            Assert.NotEqual(before, recipe.CountLabel.Text);
            Assert.Equal(before, recipe.CountLabel.From);
            Assert.Equal(0, recipe.CountLabel.Progress, 3);
            Assert.NotEqual(rateBefore, outputTab.RateLabel.Text);
            Assert.Equal(rateBefore, outputTab.RateLabel.From);
            Assert.Equal(0, outputTab.RateLabel.Progress, 3);

            AnimationClock.Settle(60);
            Assert.InRange(recipe.CountLabel.Progress, 0.01, 0.99);
            Assert.InRange(outputTab.RateLabel.Progress, 0.01, 0.99);

            AnimationClock.Settle(120);
            Assert.Equal(1, recipe.CountLabel.Progress, 3);
            Assert.Equal(1, outputTab.RateLabel.Progress, 3);

            Motion.ReduceMotion = true;
            string swapped = recipe.CountLabel.Text;
            consumerController.SetDesiredSetValue(8);
            fx.Viewer.Graph.UpdateNodeValues();
            recipe.PrePaint();

            Assert.NotEqual(swapped, recipe.CountLabel.Text);
            Assert.Null(recipe.CountLabel.From);
            Assert.Equal(1, recipe.CountLabel.Progress, 3);
            Assert.Null(outputTab.RateLabel.From);

            consumerController.SetDesiredSetValue(2.5);
            fx.Viewer.Graph.UpdateNodeValues();
            double buildings = recipe.ViewModel.ActualSetValue;
            Assert.NotEqual(0, buildings % 1);
            recipe.PrePaint();
            fx.Viewer.Context.RoundAssemblerCount = true;
            recipe.PrePaint();
            Assert.Equal("×" + Math.Ceiling(buildings).ToString("0", DisplayCulture.Format), recipe.CountLabel.Text);
            fx.Viewer.Context.RoundAssemblerCount = false;
            recipe.PrePaint();
            Assert.Equal("×" + buildings.ToString("0.00", DisplayCulture.Format), recipe.CountLabel.Text);

            string thousands = "12" + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + "3k";
            Assert.Equal(thousands, NodeCardPainter.Count(12345, round: false));
            Assert.Equal(thousands, NodeCardPainter.Count(12345, round: true));
            Assert.Equal("10k", NodeCardPainter.Count(9999.3, round: true));
            Assert.Equal("10k", NodeCardPainter.Count(9999.996, round: false));
        }

        [AvaloniaFact]
        public void Align_TweensOver200ms_AndJumpsUnderReduceMotion() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            fx.Control.Grid.CurrentGridUnit = 64;
            fx.Control.Grid.ShowGrid = true;
            var ore = new ItemQualityPair(fx.Cache.Items["iron-ore"], fx.Cache.DefaultQuality!);
            NodeId id = fx.Viewer.Session.Editor.CreateSupplierNode(ore, new DrawingPoint(30, 30));
            BaseNodeElement node = fx.Viewer.NodeElementDictionary[id];
            DrawingPoint target = fx.Control.Grid.AlignToGrid(new DrawingPoint(30, 30));
            Assert.NotEqual(new DrawingPoint(30, 30), target);
            fx.Viewer.SetSelection([node]);

            fx.Control.AlignSelection();
            Assert.Equal(new DrawingPoint(30, 30), node.Location);

            AnimationClock.Settle(60);
            Assert.NotEqual(new DrawingPoint(30, 30), node.Location);
            Assert.NotEqual(target, node.Location);

            AnimationClock.Settle(200);
            Assert.Equal(target, node.Location);

            fx.Viewer.Session.Editor.SetLocation(id, new DrawingPoint(30, 30));
            fx.Control.AlignSelection();
            AnimationClock.Settle(40);
            fx.ClickAt(new Avalonia.Point(5, 5));
            Assert.Equal(target, node.Location);

            Motion.ReduceMotion = true;
            fx.Viewer.Session.Editor.SetLocation(id, new DrawingPoint(100, 100));
            DrawingPoint jumpTarget = fx.Control.Grid.AlignToGrid(new DrawingPoint(100, 100));
            fx.Viewer.SetSelection([node]);
            fx.Control.AlignSelection();
            Assert.Equal(jumpTarget, node.Location);
        }
    }
}
