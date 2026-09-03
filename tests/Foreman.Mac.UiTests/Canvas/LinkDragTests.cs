using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Models;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;
using InputPointer = Avalonia.Input.Pointer;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises reference §3's DraggedLinkElement lifecycle: starting a drag from a tab, endpoint tracking,
    //EndDrag's three outcomes (connect / new-node chooser stub / cancel), and the Cmd+drag passthrough-bus
    //fan-out (upstream Ctrl, mapped to Cmd per this task's plan).
    public class LinkDragTests {
        private const int Half = 300;

        //IRChooserPanel.startingGroup is process-wide static (docs/panels-reference.md §2, same reset
        //ItemRecipeChooserFlowTests carries) - the multi-origin sub-picker test below is this file's first use
        //of a real group-backed chooser grid, and a stale group reference left over from another test class's
        //DataCache would otherwise select nothing.
        public LinkDragTests() {
            System.Reflection.FieldInfo field = typeof(IRChooserPanel).GetField("startingGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            field.SetValue(null, null);
        }

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }

            private readonly Dictionary<string, ItemPrototype> items = [];

            //Caches by name so two nodes built from the same itemName (a supplier/consumer pair meant to
            //link) share one ItemPrototype instance - ItemQualityPair equality is reference-based on IItem,
            //so two independently-constructed prototypes of the same name would never be considered the same
            //item.
            public ItemQualityPair Pair(string name) {
                if (!items.TryGetValue(name, out ItemPrototype? item)) {
                    item = new ItemPrototype(Cache, name, name, Subgroup, "z", false);
                    Store(Cache).Items[name] = item;
                    items[name] = item;
                }
                return new ItemQualityPair(item, Quality);
            }

            public ItemPrototype NewItem(string name) => (ItemPrototype)Pair(name).Item!;

            //Mirrors NodeDragTests' AddSupplier: routes through Control.Viewer's own graph/editor so
            //GraphViewer's session-event wiring auto-creates the matching element with a correctly wired
            //Context (the tab-hit -> Context.StartLinkDrag seam needs a real GraphCanvasControl behind it).
            public BaseNodeElement AddSupplier(string itemName, Point location) {
                var id = Control.Viewer.Session.Editor.CreateSupplierNode(Pair(itemName), location);
                return Prime(Control.Viewer.NodeElementDictionary[id]);
            }

            public BaseNodeElement AddConsumer(string itemName, Point location) {
                var id = Control.Viewer.Session.Editor.CreateConsumerNode(Pair(itemName), location);
                return Prime(Control.Viewer.NodeElementDictionary[id]);
            }

            public BaseNodeElement AddPassthrough(string itemName, Point location) {
                var id = Control.Viewer.Session.Editor.CreatePassthroughNode(Pair(itemName), location);
                return Prime(Control.Viewer.NodeElementDictionary[id]);
            }

            private static BaseNodeElement Prime(BaseNodeElement element) {
                element.RequestStateUpdate();
                element.PrePaint();
                return element;
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

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Control = control, Window = window };
        }

        private static AvaloniaPoint TabScreen(Fixture fx, BaseNodeElement node, LinkType linkType) {
            ItemTabElement tab = node.SubElements.OfType<ItemTabElement>().First(t => t.LinkType == linkType);
            return fx.Control.Viewport.GraphToScreen(node.LocalToGraph(tab.Location));
        }

        //Arms a link drag from the given tab: MouseDown on the tab, then two MouseMoves (threshold-cross,
        //then Dragged's first post-threshold call - the tab-hit check that fires Context.StartLinkDrag).
        //Mirrors NodeDragTests' own arm sequence, minus the third "actual move" call a link drag never needs.
        private static void StartDragFromTab(Fixture fx, BaseNodeElement node, LinkType linkType) {
            AvaloniaPoint tabScreen = TabScreen(fx, node, linkType);
            fx.Window.MouseDown(tabScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(tabScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(tabScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);
        }

        //---- start-from-tab (reference §3 "Start") ----

        [AvaloniaFact]
        public void Dragged_StartingFromATab_CreatesGhost_AndRedirectsMouseDownElement() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));

            StartDragFromTab(fx, supplier, LinkType.Output);

            Assert.NotNull(fx.Control.DraggedLink);
            Assert.Same(fx.Control.DraggedLink, fx.Control.MouseDownElement);
            Assert.Equal(new Point(0, 0), supplier.Location); //never moved as a node - the tab hit redirected instead of arming
        }

        //---- endpoint tracking across zoom (reference §3 "Ghost rendering"/UpdateEndpoint) ----

        [AvaloniaFact]
        public void MouseMoved_TracksEndpoint_AcrossZoomLevels() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);

            fx.Control.Viewport.ViewScale = 2f;
            var zoomedIn = new AvaloniaPoint(Half + 60, Half - 40);
            fx.Window.MouseMove(zoomedIn, RawInputModifiers.LeftMouseButton);
            Assert.Equal(fx.Control.Viewport.ScreenToGraph(zoomedIn), fx.Control.DraggedLink!.EndpointLocation);

            fx.Control.Viewport.ViewScale = 0.5f;
            var zoomedOut = new AvaloniaPoint(Half - 90, Half + 30);
            fx.Window.MouseMove(zoomedOut, RawInputModifiers.LeftMouseButton);
            Assert.Equal(fx.Control.Viewport.ScreenToGraph(zoomedOut), fx.Control.DraggedLink!.EndpointLocation);
        }

        //---- EndDrag outcome (a): connect (reference §3 "Completion") ----

        [AvaloniaFact]
        public void ReleaseOverCompatibleTab_CreatesRealLink() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            BaseNodeElement consumer = fx.AddConsumer("iron-ore", new Point(0, -300));
            StartDragFromTab(fx, supplier, LinkType.Output);

            AvaloniaPoint consumerScreen = fx.Control.Viewport.GraphToScreen(consumer.Location);
            fx.Window.MouseMove(consumerScreen, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(consumerScreen, MouseButton.Left, RawInputModifiers.None);

            Assert.Null(fx.Control.DraggedLink);
            Assert.Single(fx.Control.Viewer.Session.View.Links);
            INodeLinkViewModel link = fx.Control.Viewer.Session.View.Links.Single();
            Assert.Equal(supplier.ViewModel.Id, link.SupplierId);
            Assert.Equal(consumer.ViewModel.Id, link.ConsumerId);
            Assert.True(fx.Control.Viewer.LinkElementDictionary.ContainsKey(link.Id));
        }

        //---- EndDrag outcome (b): unresolved (reference §3 "Completion", the P5-stub branch) ----

        [AvaloniaFact]
        public void ReleaseOverIncompatibleNode_DoesNotCreateLink_RecordsNewNodeRequest() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            BaseNodeElement otherConsumer = fx.AddConsumer("copper-ore", new Point(0, -300)); //wrong item
            StartDragFromTab(fx, supplier, LinkType.Output);

            AvaloniaPoint consumerScreen = fx.Control.Viewport.GraphToScreen(otherConsumer.Location);
            fx.Window.MouseMove(consumerScreen, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(consumerScreen, MouseButton.Left, RawInputModifiers.None);

            Assert.Null(fx.Control.DraggedLink);
            Assert.Empty(fx.Control.Viewer.Session.View.Links);
            Assert.True(fx.Control.LastNewNodeLinkDragRequest.HasValue);
            Assert.Equal(NewNodeType.Consumer, fx.Control.LastNewNodeLinkDragRequest!.Value.NodeType);
            Assert.Equal(supplier, fx.Control.LastNewNodeLinkDragRequest.Value.OriginElement);
        }

        [AvaloniaFact]
        public void ReleaseOverEmptySpace_DoesNotCreateLink_RecordsNewNodeRequest() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);

            var emptySpace = new AvaloniaPoint(Half + 200, Half + 200);
            fx.Window.MouseMove(emptySpace, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(emptySpace, MouseButton.Left, RawInputModifiers.None);

            Assert.Null(fx.Control.DraggedLink);
            Assert.Empty(fx.Control.Viewer.Session.View.Links);
            Assert.True(fx.Control.LastNewNodeLinkDragRequest.HasValue);
            Assert.Equal(NewNodeType.Consumer, fx.Control.LastNewNodeLinkDragRequest!.Value.NodeType);
        }

        //---- EndDrag outcome (d): Ctrl(->Cmd)-held release bypasses the chooser (reference §3/§10's AddNewNode
        //Control.ModifierKeys check) ----

        //Ports AddNewNode's "(Control.ModifierKeys & Keys.Control) == Keys.Control" branch (reference §10,
        //upstream lines 227-232): Cmd held at release time drops a passthrough node directly, wired to the
        //drag's origin, and never opens the chooser - the same Cmd this port already maps for the slave-link
        //bus fan-out (upstream overloads Ctrl for both purposes off the same DraggedLinkElement).
        [AvaloniaFact]
        public void CmdRelease_OverEmptySpace_CreatesPassthroughNodeDirectly_NoChooserShown() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);

            var emptySpace = new AvaloniaPoint(Half + 200, Half + 200);
            fx.Window.MouseMove(emptySpace, RawInputModifiers.LeftMouseButton | RawInputModifiers.Meta);
            fx.Window.MouseUp(emptySpace, MouseButton.Left, RawInputModifiers.Meta);

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            Assert.False(fx.Control.LastNewNodeLinkDragRequest.HasValue);
            Assert.Null(fx.Control.DraggedLink);

            BaseNodeElement newNode = Assert.Single(fx.Control.Viewer.NodeElementDictionary.Values, n => n != supplier);
            var passthroughViewModel = Assert.IsAssignableFrom<IPassthroughNodeViewModel>(newNode.ViewModel);
            Assert.Equal(fx.Pair("iron-ore"), passthroughViewModel.PassthroughItem);

            INodeLinkViewModel link = Assert.Single(fx.Control.Viewer.Session.View.Links);
            Assert.Equal(supplier.ViewModel.Id, link.SupplierId);
            Assert.Equal(newNode.ViewModel.Id, link.ConsumerId);
        }

        //---- EndDrag outcome (c): cancel (reference §3 "Cancel", plus this port's added Escape affordance) ----

        [AvaloniaFact]
        public void RightClickOnGhost_CancelsAndDisposes() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);

            AvaloniaPoint tabScreen = TabScreen(fx, supplier, LinkType.Output);
            int redrawCountBefore = fx.Control.RedrawRequestCount;
            fx.Window.MouseDown(tabScreen, MouseButton.Right, RawInputModifiers.None);

            Assert.Null(fx.Control.DraggedLink);
            Assert.Null(fx.Control.MouseDownElement);
            Assert.Empty(fx.Control.Viewer.Session.View.Links);
            Assert.Equal(redrawCountBefore + 1, fx.Control.RedrawRequestCount); //OnPointerPressed itself has no trailing invalidate on this path
        }

        //Ports MouseDown's Right-button cancel branch (reference §3) for the way a real chorded right-click
        //actually arrives: Avalonia's shared MouseDevice.ProcessRawEvent (src/Avalonia.Base/Input/
        //MouseDevice.cs) folds a button's down/up transition into a plain PointerMoved - never a genuine
        //PointerPressed/PointerReleased - whenever another button is already held (its ButtonCount(props) > 1
        //check on the down side), on every desktop backend, native macOS included, not just
        //Avalonia.Headless's own synthetic pointer. So a right-click mid-link-drag never reaches
        //OnPointerPressed's MouseDown(Right) cancel branch at all; only PointerUpdateKind on the resulting
        //PointerMoved event names the transition. Raises a hand-built PointerEventArgs for exactly that -
        //Avalonia's own documented escape hatch (the same [Unstable] constructor family ElementMenuTests uses
        //for the analogous release-side gap) - since no synthetic Window.MouseDown/MouseMove sequence
        //produces this shape of event.
        [AvaloniaFact]
        public void RightButtonPress_DuringInProgressLinkDrag_CancelsInsteadOfOpeningChooser() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);
            Assert.NotNull(fx.Control.DraggedLink);

            AvaloniaPoint emptySpace = new(Half + 200, Half + 200);
            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton | RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed);
            var moveArgs = new PointerEventArgs(InputElement.PointerMovedEvent, fx.Control, pointer, fx.Control, emptySpace, 0, properties, KeyModifiers.None);

            fx.Control.RaiseEvent(moveArgs);

            Assert.Null(fx.Control.DraggedLink);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            Assert.False(fx.Control.LastNewNodeLinkDragRequest.HasValue);
            Assert.Empty(fx.Control.Viewer.Session.View.Links);
        }

        [AvaloniaFact]
        public void Escape_CancelsAndDisposesInProgressLinkDrag() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);
            Assert.NotNull(fx.Control.DraggedLink);

            fx.Control.Focus();
            fx.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Null(fx.Control.DraggedLink);
            Assert.Null(fx.Control.MouseDownElement);
            Assert.Empty(fx.Control.Viewer.Session.View.Links);
        }

        //---- Cmd+drag multi-passthrough bus fan-out (reference §3 "Ctrl+drag multi-passthrough bus", upstream
        //Ctrl mapped to Cmd here). Hand-derivation of the expected slave offset from UpdateEndpoint's own
        //arithmetic (DraggedLinkElement.cs):
        //   slave.EndpointLocation = anchor.Location + (masterEndpoint - originElement.Location)
        // where anchor is the slave's own bound end - since the master's StartConnectionType is Output, each
        // slave is also constructed Output-started (Init binds SupplierElement to the slave's own node), so
        // anchor == the slave's node itself (passthrough2), not the master's origin (passthrough1).
        //   passthrough1 (origin) at (0,0), passthrough2 at (300,100), grid off so masterEndpoint is the raw
        //   cursor graph point with no alignment.
        //   masterEndpoint = ScreenToGraph(movePoint); expected = (300,100) + (masterEndpoint - (0,0))
        //                                                        = (300 + masterEndpoint.X, 100 + masterEndpoint.Y) ----

        [AvaloniaFact]
        public void CmdDrag_FromPassthroughTab_WithAllPassthroughSelection_FansOutSlaveWithRigidOffset() {
            Fixture fx = NewFixture();
            BaseNodeElement passthrough1 = fx.AddPassthrough("iron-ore", new Point(0, 0));
            BaseNodeElement passthrough2 = fx.AddPassthrough("copper-ore", new Point(300, 100));
            fx.Control.Viewer.SetSelection([passthrough1, passthrough2]);
            StartDragFromTab(fx, passthrough1, LinkType.Output);

            var movePoint = new AvaloniaPoint(Half + 80, Half - 20);
            fx.Window.MouseMove(movePoint, RawInputModifiers.LeftMouseButton | RawInputModifiers.Meta);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surface.Canvas); //PrePaint pass -> UpdateSlaveLinks, gated on the Cmd flag captured above

            DraggedLinkElement slave = Assert.Single(fx.Control.DraggedLink!.SubElements.OfType<DraggedLinkElement>());
            Point masterEndpoint = fx.Control.DraggedLink.EndpointLocation;
            Point expectedSlaveEndpoint = new(300 + masterEndpoint.X, 100 + masterEndpoint.Y);
            Assert.Equal(expectedSlaveEndpoint, slave.EndpointLocation);
        }

        [AvaloniaFact]
        public void CmdDrag_ReleasingCmd_DisposesSlaves() {
            Fixture fx = NewFixture();
            BaseNodeElement passthrough1 = fx.AddPassthrough("iron-ore", new Point(0, 0));
            BaseNodeElement passthrough2 = fx.AddPassthrough("copper-ore", new Point(300, 100));
            fx.Control.Viewer.SetSelection([passthrough1, passthrough2]);
            StartDragFromTab(fx, passthrough1, LinkType.Output);

            var movePoint = new AvaloniaPoint(Half + 80, Half - 20);
            fx.Window.MouseMove(movePoint, RawInputModifiers.LeftMouseButton | RawInputModifiers.Meta);
            using SKSurface surfaceWithBus = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surfaceWithBus.Canvas);
            Assert.Single(fx.Control.DraggedLink!.SubElements.OfType<DraggedLinkElement>());

            fx.Window.MouseMove(movePoint, RawInputModifiers.LeftMouseButton); //Cmd released
            using SKSurface surfaceReleased = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surfaceReleased.Canvas);

            Assert.Empty(fx.Control.DraggedLink!.SubElements.OfType<DraggedLinkElement>());
        }

        //---- ghost renders (reference §3 "Ghost rendering") ----

        //Samples the ghost's free (cursor-tracking) end, not the tab-bound end: a node's own chrome (the tab
        //border) paints on top of the link at the tab's connection point (nodes paint after links in
        //GetPaintingOrder, matching upstream's z-order), so only the unobstructed free end is safe to sample
        //against the plain background.
        [AvaloniaFact]
        public void InProgressDrag_RendersItemColoredLineToTheFreeEndpoint() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            item.SetIconAndColor(new IconColorPair(null, System.Drawing.Color.FromArgb(255, 210, 90, 30)));
            var supplierId = fx.Control.Viewer.Session.Editor.CreateSupplierNode(fx.Pair("iron-ore"), new Point(0, 0));
            BaseNodeElement supplier = fx.Control.Viewer.NodeElementDictionary[supplierId];
            supplier.RequestStateUpdate();
            supplier.PrePaint();

            StartDragFromTab(fx, supplier, LinkType.Output);
            var farPoint = new AvaloniaPoint(Half, Half + 250); //well clear of the supplier node/tabs
            fx.Window.MouseMove(farPoint, RawInputModifiers.LeftMouseButton);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surface.Canvas);

            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            SKColor pixel = pixmap.GetPixelColor((int)farPoint.X, (int)farPoint.Y);
            Assert.Equal(new SKColor(210, 90, 30), pixel); //the bezier's free endpoint is always exactly on-path
        }

        //---- live bug 2: footer buttons dead after a link-drag opens the chooser ----

        //Ports the real click path (fx.Window.MouseDown/MouseUp at the button's actual on-screen position,
        //through Avalonia's genuine hit-test + press/release pipeline) rather than ItemRecipeChooserFlowTests'
        //Click() helper, which raises a release-only PointerReleasedEventArgs directly on the target control.
        //A synthetic release with no preceding press never sets Avalonia's own Button.IsPressed, so its
        //internal click-conversion logic - which marks a REAL press+release pair Handled once it runs - stays
        //quiet and the bypass tests never surface the bug: RecipeChooserPanel's footer buttons were wired with
        //a plain `+=`, which Avalonia skips once a same-instance class handler (Button's own) has already set
        //Handled, so a genuine mouse click never reached them.
        private static AvaloniaPoint WindowCenterOf(Avalonia.Visual control, AvaloniaWindow window) =>
            Avalonia.VisualExtensions.TranslatePoint(control, new AvaloniaPoint(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        [AvaloniaFact]
        public void FooterButton_AfterLinkDragOpensChooser_RespondsToARealClick_AndLinksTheNewNode() {
            Fixture fx = NewFixture();
            fx.Control.Viewer.Context.DCache = fx.Cache;
            fx.Control.Viewer.Graph.DefaultAssemblerQuality = fx.Quality;
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);

            var emptySpace = new AvaloniaPoint(Half + 200, Half + 200);
            fx.Window.MouseMove(emptySpace, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(emptySpace, MouseButton.Left, RawInputModifiers.None);

            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); //forces the panel's real layout pass so its buttons have real Bounds to click on
            var panel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Button outputButton = panel.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == "Output");
            AvaloniaPoint buttonScreen = WindowCenterOf(outputButton, fx.Window);

            fx.Window.MouseDown(buttonScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(buttonScreen, MouseButton.Left, RawInputModifiers.None);

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            BaseNodeElement newNode = Assert.Single(fx.Control.Viewer.NodeElementDictionary.Values, n => n != supplier);
            Assert.IsAssignableFrom<IConsumerNodeViewModel>(newNode.ViewModel);
            INodeLinkViewModel link = Assert.Single(fx.Control.Viewer.Session.View.Links);
            Assert.Equal(supplier.ViewModel.Id, link.SupplierId);
            Assert.Equal(newNode.ViewModel.Id, link.ConsumerId);
        }

        //Same class of bug (a plain += on a footer Button), same fix (AddHandler with handledEventsToo)
        //covers AddUnspoilButton too - the addendum's flagged sibling to the Output button above. Visibility
        //first (upstream IRChooserPanel.cs:636's asProduct && keyItem.SpoilOrigins.Count > 0 gate, already
        //ported at RecipeChooserPanel.cs:69), then a real click proving the wiring and the resulting node.
        [AvaloniaFact]
        public void UnspoilButton_VisibleOnlyWhenKeyItemIsAProductWithSpoilOrigins() {
            Fixture fx = NewFixture();
            fx.Control.Viewer.Context.DCache = fx.Cache;
            fx.Control.Viewer.Graph.DefaultAssemblerQuality = fx.Quality;
            ItemPrototype fresh = fx.NewItem("fresh-food");
            ItemPrototype spoiled = fx.NewItem("rotten-food");
            fresh.SpoilResult = spoiled;
            spoiled.SpoilOriginsInternal.Add(fresh);
            BaseNodeElement rottenConsumer = fx.AddConsumer("rotten-food", new Point(0, 0));
            BaseNodeElement freshConsumer = fx.AddConsumer("fresh-food", new Point(0, -300));

            //Dragging from an input tab and dropping unresolved yields NewNodeType.Supplier (asProduct) -
            //rotten-food has spoil origins, so Unspoil should show; fresh-food has none, so it should not.
            StartDragFromTab(fx, rottenConsumer, LinkType.Input);
            AvaloniaPoint emptyForRotten = new(Half + 200, Half + 200);
            fx.Window.MouseMove(emptyForRotten, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(emptyForRotten, MouseButton.Left, RawInputModifiers.None);
            var rottenPanel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Assert.Contains(rottenPanel.GetVisualDescendants().OfType<Button>(), b => b.Content is string s && s == "UnSpoil" && b.IsVisible);
            fx.Control.FloatingPanelHost.Close();

            StartDragFromTab(fx, freshConsumer, LinkType.Input);
            AvaloniaPoint emptyForFresh = new(Half - 200, Half + 200);
            fx.Window.MouseMove(emptyForFresh, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(emptyForFresh, MouseButton.Left, RawInputModifiers.None);
            var freshPanel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Assert.DoesNotContain(freshPanel.GetVisualDescendants().OfType<Button>(), b => b.Content is string s && s == "UnSpoil" && b.IsVisible);
        }

        [AvaloniaFact]
        public void UnspoilButton_SingleOrigin_RespondsToARealClick_AndCreatesLinkedSpoilNode() {
            Fixture fx = NewFixture();
            fx.Control.Viewer.Context.DCache = fx.Cache;
            fx.Control.Viewer.Graph.DefaultAssemblerQuality = fx.Quality;
            ItemPrototype fresh = fx.NewItem("fresh-food");
            ItemPrototype spoiled = fx.NewItem("rotten-food");
            fresh.SpoilResult = spoiled;
            spoiled.SpoilOriginsInternal.Add(fresh);
            BaseNodeElement rottenConsumer = fx.AddConsumer("rotten-food", new Point(0, 0));
            StartDragFromTab(fx, rottenConsumer, LinkType.Input);

            var emptySpace = new AvaloniaPoint(Half + 200, Half + 200);
            fx.Window.MouseMove(emptySpace, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(emptySpace, MouseButton.Left, RawInputModifiers.None);

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var panel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Button unspoilButton = panel.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == "UnSpoil");
            AvaloniaPoint buttonScreen = WindowCenterOf(unspoilButton, fx.Window);

            fx.Window.MouseDown(buttonScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(buttonScreen, MouseButton.Left, RawInputModifiers.None);

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            BaseNodeElement newNode = Assert.Single(fx.Control.Viewer.NodeElementDictionary.Values, n => n != rottenConsumer);
            Assert.IsAssignableFrom<ISpoilNodeViewModel>(newNode.ViewModel);
            INodeLinkViewModel link = Assert.Single(fx.Control.Viewer.Session.View.Links);
            Assert.Equal(newNode.ViewModel.Id, link.SupplierId);
            Assert.Equal(rottenConsumer.ViewModel.Id, link.ConsumerId);
        }

        //Multi-origin path (upstream's own item sub-picker hand-off, reference §2/§3): AddUnspoilButtonReleased
        //sets PanelCloseReason to RequiresItemSelection instead of closing outright when SpoilOrigins.Count > 1,
        //and GraphCanvasControl.OpenSpoilOriginPicker swaps in a real ItemChooserPanel over exactly those
        //origins. That sub-picker's cells are IconButtons (the "Released" custom event, not a stock Avalonia
        //Button's routed PointerReleased), so they were never subject to this bug - included here to prove the
        //whole multi-origin hand-off still works end to end once the footer button itself responds.
        [AvaloniaFact]
        public void UnspoilButton_MultipleOrigins_RealClick_OpensSubPicker_AndSelectingOriginCreatesSpoilNode() {
            Fixture fx = NewFixture();
            fx.Control.Viewer.Context.DCache = fx.Cache;
            fx.Control.Viewer.Graph.DefaultAssemblerQuality = fx.Quality;
            //Named "logistics" - IRChooserPanel's own starting-group default only auto-selects a group by
            //that exact name (SetSelectedGroup's own logistics lookup), same as ItemRecipeChooserFlowTests'
            //fixture group.
            var group = new GroupPrototype(fx.Cache, "logistics", "Logistics", "a");
            Store(fx.Cache).Groups[group.Name] = group;
            group.SubgroupsInternal.Add(fx.Subgroup);
            fx.Subgroup.MyGroupInternal = group;

            ItemPrototype freshA = fx.NewItem("fresh-food-a");
            ItemPrototype freshB = fx.NewItem("fresh-food-b");
            ItemPrototype spoiled = fx.NewItem("rotten-food");
            freshA.SpoilResult = spoiled;
            freshB.SpoilResult = spoiled;
            spoiled.SpoilOriginsInternal.Add(freshA);
            spoiled.SpoilOriginsInternal.Add(freshB);

            //ItemChooserPanel's sub-picker only lists items with a live consumption/production recipe
            //(GetSubgroupList's own `visible` gate) - a trivial recipe per origin satisfies that.
            var assembler = new AssemblerPrototype(fx.Cache, "asm", "Assembler", EntityType.Assembler, EnergySource.Electric) { Available = true };
            foreach (ItemPrototype origin in new[] { freshA, freshB }) {
                var recipe = new RecipePrototype(fx.Cache, "produce-" + origin.Name, origin.Name, fx.Subgroup, "a") { Available = true };
                recipe.InternalOneWayAddProduct(origin, 1, 0);
                origin.ProductionRecipesInternal.Add(recipe);
                recipe.AssemblersInternal.Add(assembler);
                assembler.RecipesInternal.Add(recipe);
                Store(fx.Cache).Recipes[recipe.Name] = recipe;
            }

            BaseNodeElement rottenConsumer = fx.AddConsumer("rotten-food", new Point(0, 0));
            StartDragFromTab(fx, rottenConsumer, LinkType.Input);

            var emptySpace = new AvaloniaPoint(Half + 200, Half + 200);
            fx.Window.MouseMove(emptySpace, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(emptySpace, MouseButton.Left, RawInputModifiers.None);

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var panel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Button unspoilButton = panel.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == "UnSpoil");
            AvaloniaPoint buttonScreen = WindowCenterOf(unspoilButton, fx.Window);

            fx.Window.MouseDown(buttonScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(buttonScreen, MouseButton.Left, RawInputModifiers.None);

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var subPicker = Assert.IsType<ItemChooserPanel>(fx.Control.FloatingPanelHost.Content);
            IconButton originCell = subPicker.GetVisualDescendants().OfType<IconButton>().First(b => Equals(b.DataObject, freshA));
            originCell.RaiseEvent(new PointerReleasedEventArgs(originCell, new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
                originCell, default, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            BaseNodeElement newNode = Assert.Single(fx.Control.Viewer.NodeElementDictionary.Values, n => n != rottenConsumer);
            Assert.IsAssignableFrom<ISpoilNodeViewModel>(newNode.ViewModel);
        }
    }
}
