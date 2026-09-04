using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVector = Avalonia.Vector;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises the Task 1 host primitive (docs/panels-reference.md §7/§9 step 1): the ported
    //EditPanelScreenLayout/EditPanelViewportLayout clamp math wired to a real GraphCanvasControl overlay,
    //upstream's ClearFloatingControls-on-every-click/one-panel-at-a-time/SubwindowOpen semantics, and
    //Escape-closes - all validated with a trivial placeholder panel (a plain Border) per the task brief.
    public class FloatingPanelHostTests {
        private const int ViewerW = 400;
        private const int ViewerH = 300;
        private const int Margin = 25; //EditPanelScreenLayout.DefaultMargin

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }
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
            control.Viewport.SetSize(ViewerW, ViewerH);
            var window = new AvaloniaWindow { Content = control, Width = ViewerW, Height = ViewerH };
            window.Show();

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Graph = graph, Session = session, Control = control, Window = window };
        }

        private static SupplierNodeElement AddSupplier(Fixture fx, string itemName, Point location) {
            var item = new ItemPrototype(fx.Cache, itemName, itemName, fx.Subgroup, "z", false);
            Store(fx.Cache).Items[itemName] = item;
            fx.Graph.CreateSupplierNode(new ItemQualityPair(item, fx.Quality), location);
            ISupplierNodeViewModel viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Last();
            var element = new SupplierNodeElement(fx.Control.Viewer.Context, viewModel);
            element.PrePaint();
            fx.Control.NodeElements.Add(element);
            fx.Control.Viewer.NodeElementDictionary.Add(viewModel.Id, element);
            return element;
        }

        //MinWidth/MinHeight rather than Width/Height: EditPanelViewportLayout.MeasureNaturalSize resets
        //Width/Height to auto before every remeasure (so a later, smaller viewport can't permanently pin a
        //panel to an earlier clamp - see EditPanelViewportLayout.cs), so a fixed natural size needs to
        //survive that reset the way real panel content's own intrinsic size would.
        private static Border TrivialPanel(double width = 100, double height = 60) =>
            new() { MinWidth = width, MinHeight = height, Focusable = true };

        //---- position math: anchor near each screen edge, across zoom levels (hand-derived, see each case) ----

        [AvaloniaFact]
        public void Show_AnchorNearTopLeft_ZoomAt1_ClampsToMargin() {
            Fixture fx = NewFixture();
            //Zoom 1, offset (0,0): screen = graph + (200,150). graph(-300,-300) -> screen(-100,-150),
            //off both edges -> clamps to (Margin, Margin).
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(-300, -300));

            Rectangle bounds = fx.Control.FloatingPanelHost.Bounds;
            Assert.Equal(Margin, bounds.X);
            Assert.Equal(Margin, bounds.Y);
        }

        [AvaloniaFact]
        public void Show_AnchorNearBottomRight_ZoomAt1_ClampsToOppositeMargin() {
            Fixture fx = NewFixture();
            //Zoom 1: graph(300,300) -> screen(500,450). maxX = 400-25-100 = 275, maxY = 300-25-60 = 215.
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(300, 300));

            Rectangle bounds = fx.Control.FloatingPanelHost.Bounds;
            Assert.Equal(ViewerW - Margin - 100, bounds.X);
            Assert.Equal(ViewerH - Margin - 60, bounds.Y);
        }

        [AvaloniaFact]
        public void Show_AnchorPartlyOffscreen_ZoomAt2_ClampsOnlyTheOverflowingAxis() {
            Fixture fx = NewFixture();
            fx.Control.Viewport.ViewScale = 2f;
            fx.Control.Viewport.UpdateGraphBounds();
            //Zoom 2: graph(0,300) -> screen(0*2+200, 300*2+150) = (200,750). X fits unclamped; Y clamps to 215.
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(0, 300));

            Rectangle bounds = fx.Control.FloatingPanelHost.Bounds;
            Assert.Equal(200, bounds.X);
            Assert.Equal(ViewerH - Margin - 60, bounds.Y);
        }

        [AvaloniaFact]
        public void Show_AnchorNearTopLeft_ZoomAt0Point5_ClampsToMargin() {
            Fixture fx = NewFixture();
            fx.Control.Viewport.ViewScale = 0.5f;
            fx.Control.Viewport.UpdateGraphBounds();
            //Zoom 0.5: graph(-500,-500) -> screen(-500*0.5+200, -500*0.5+150) = (-50,-100), clamps to (Margin, Margin).
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(-500, -500));

            Rectangle bounds = fx.Control.FloatingPanelHost.Bounds;
            Assert.Equal(Margin, bounds.X);
            Assert.Equal(Margin, bounds.Y);
        }

        //---- click-outside-closes / click-inside-doesn't (reference §7's swallow semantics) ----

        //A plain click's node-selection effect (reference §4) routes to MouseUpLeft/EditNode rather than
        //SelectedNodes, so these two use OnPointerPressed's own unconditional "clear selection unless
        //keeping a group" branch (matching upstream's ActiveControl=null/deselect-on-click-away, already
        //covered for the no-panel case by SelectionTests.ClickEmptySpace_NoModifiers_ClearsExistingSelection)
        //as the canvas-side-effect proof instead.
        [AvaloniaFact]
        public void ClickOutsidePanel_ClosesItAndStillActsOnCanvas() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);
            //Panel clamps to the top-left corner (see Show_AnchorNearTopLeft_ZoomAt1_ClampsToMargin); the
            //node sits at the viewport centre, well outside it.
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(-300, -300));

            //Empty canvas space: away from both the panel (top-left corner) and the node's own screen
            //rect (roughly (128,102)-(272,198)) - a click there would instead hit the "keepGroupSelection"
            //branch and leave the selection untouched.
            fx.Window.MouseDown(new AvaloniaPoint(350, 250), MouseButton.Left, RawInputModifiers.None);

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            Assert.DoesNotContain(node, fx.Control.Viewer.SelectedNodes);
        }

        [AvaloniaFact]
        public void ClickInsidePanel_DoesNotCloseAndDoesNotActOnCanvas() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", new Point(-150, -100));
            fx.Control.Viewer.SetSelection([node]);
            //Panel clamps to the top-left corner; (50,50) sits well inside its (25,25)-(125,85) bounds.
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(-300, -300));

            fx.Window.MouseDown(new AvaloniaPoint(50, 50), MouseButton.Left, RawInputModifiers.None);

            Assert.True(fx.Control.FloatingPanelHost.IsOpen);
            Assert.Contains(node, fx.Control.Viewer.SelectedNodes);
        }

        //CRITICAL 1(b) (final fix wave): only OnPointerPressed guarded panel-chrome clicks - a MouseUp
        //landing inside the panel still reached OnPointerReleased's own hit-testing, opening a node's
        //context menu (or acting on whatever selection state) right underneath the panel, since the press
        //itself never claimed MouseDownElement/CurrentDragOperation for it to gate on. The node sits at the
        //same screen point the panel covers (see ClickInsidePanel_DoesNotCloseAndDoesNotActOnCanvas above),
        //so an unguarded release would both find and act on it.
        [AvaloniaFact]
        public void MouseUpInsidePanel_ChangesNoSelectionAndOpensNoMenuUnderneath() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", new Point(-150, -100));
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(-300, -300));

            fx.Window.MouseDown(new AvaloniaPoint(50, 50), MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(new AvaloniaPoint(50, 50), MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseDown(new AvaloniaPoint(50, 50), MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(new AvaloniaPoint(50, 50), MouseButton.Right, RawInputModifiers.None);

            Assert.True(fx.Control.FloatingPanelHost.IsOpen);
            Assert.Contains(node, fx.Control.Viewer.SelectedNodes);
            Assert.Null(fx.Control.LastContextMenuEntries);
        }

        //---- one panel at a time ----

        [AvaloniaFact]
        public void Show_SecondPanel_ClosesFirst() {
            Fixture fx = NewFixture();
            Border first = TrivialPanel();
            Border second = TrivialPanel();
            fx.Control.FloatingPanelHost.Show(first, new Point(0, 0));

            fx.Control.FloatingPanelHost.Show(second, new Point(50, 50));

            Assert.Same(second, fx.Control.FloatingPanelHost.Content);
            Assert.True(fx.Control.FloatingPanelHost.IsOpen);
        }

        //---- Escape closes ----

        [AvaloniaFact]
        public void Escape_ClosesPanel() {
            Fixture fx = NewFixture();
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(0, 0));

            CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        //IMPORTANT 3 (final fix wave): Close() never restored focus to the canvas, so an Escape-close (or a
        //chooser-selection close) left canvas keyboard shortcuts dead until the user clicked - the
        //click-outside path already worked, since OnPointerPressed calls Focus() unconditionally right after
        //closing. A selected node's arrow-key move is the same canvas-shortcut proof
        //ArrowKey_InertWhilePanelFocused below uses, just checked for the opposite (now-restored) outcome.
        [AvaloniaFact]
        public void Escape_ClosesPanel_RestoresCanvasFocus_SoArrowKeyMovesSelectedNode() {
            Fixture fx = NewFixture();
            //Node created straight through fx.Control.Viewer's own session/editor (not the fixture's
            //separate fx.Graph/fx.Session, which the other tests in this file use only to prove selection/
            //menu side effects) - SetLocation's edit command only resolves against the id the node's own
            //Context.Editor actually knows about, matching NodeDragTests.Fixture.AddSupplier's same note.
            var item = new ItemPrototype(fx.Cache, "iron-ore", "iron-ore", fx.Subgroup, "z", false);
            Store(fx.Cache).Items[item.Name] = item;
            NodeId id = fx.Control.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(item, fx.Quality), new Point(40, 40));
            BaseNodeElement node = fx.Control.Viewer.NodeElementDictionary[id];
            node.RequestStateUpdate();
            node.PrePaint();
            fx.Control.Viewer.SetSelection([node]);
            int x = node.X;
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(0, 0));

            CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);
            CanvasKeys.PressOnCanvas(fx.Control, Key.Right);

            Assert.NotEqual(x, node.X);
        }

        //---- pan/zoom tracking: the panel repositions whenever the Viewport changes underneath it ----

        //CRITICAL 1(a) (final fix wave): upstream suppresses MouseWheel entirely while a hosted panel holds
        //focus (ProductionGraphViewer_MouseWheel's `if (ContainsFocus && !this.Focused) return;`, ahead of
        //its own zoom/ClearFloatingControls) - this used to zoom the canvas (and drag the panel along with
        //it) right underneath an open chooser/edit panel. FloatingPanelHost.IsOpen stands in for that guard
        //here (Show() always focuses its content), so the panel must now stay exactly where it was and the
        //canvas must not zoom, regardless of whether the wheel point lands on the panel's own chrome.
        [AvaloniaFact]
        public void WheelZoom_WhilePanelOpen_IsFullySuppressed_NeitherZoomsNorMovesPanel() {
            Fixture fx = NewFixture();
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(50, 50));
            Rectangle before = fx.Control.FloatingPanelHost.Bounds;
            float scaleBefore = fx.Control.Viewport.ViewScale;

            fx.Window.MouseWheel(new AvaloniaPoint(ViewerW / 2.0, ViewerH / 2.0), new AvaloniaVector(0, 1), RawInputModifiers.None);

            Assert.Equal(scaleBefore, fx.Control.Viewport.ViewScale);
            Assert.Equal(before.Location, fx.Control.FloatingPanelHost.Bounds.Location);
        }

        [AvaloniaFact]
        public void Resize_RepositionsOpenPanel() {
            Fixture fx = NewFixture();
            //Anchor clamps to the bottom-right corner (275,215) in the 400x300 viewport.
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(300, 300));

            fx.Control.Measure(new global::Avalonia.Size(200, 150));
            fx.Control.Arrange(new global::Avalonia.Rect(0, 0, 200, 150));

            Rectangle after = fx.Control.FloatingPanelHost.Bounds;
            Assert.Equal(200 - Margin - after.Width, after.X);
            Assert.Equal(150 - Margin - after.Height, after.Y);
        }

        //---- canvas shortcuts suspended while the panel has focus (reference §7's SubwindowOpen gate) ----

        [AvaloniaFact]
        public void ArrowKey_InertWhilePanelFocused() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", new Point(40, 40));
            fx.Control.Viewer.SetSelection([node]);
            int x = node.X;
            int y = node.Y;
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(0, 0));

            CanvasKeys.PressOnCanvas(fx.Control, Key.Right);

            Assert.Equal(x, node.X);
            Assert.Equal(y, node.Y);
        }

        [AvaloniaFact]
        public void DeleteKey_InertWhilePanelFocused() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", new Point(40, 40));
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.FloatingPanelHost.Show(TrivialPanel(), new Point(0, 0));

            CanvasKeys.PressOnCanvas(fx.Control, Key.Delete);

            Assert.Contains(node, fx.Control.Viewer.SelectedNodes);
            Assert.Contains(node, fx.Control.NodeElements);
        }

    }
}
