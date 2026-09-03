using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises reference §2's node-dragging semantics: leader grid-snap/axis-lock, raw-delta followers
    //(nodes and annotations), tab-vs-node disambiguation, arrow-key steps, and the carried Viewport.PanTo
    //hard-limit seam from Task 1's review.
    public class NodeDragTests {
        private const int Half = 200;

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }

            public ItemPrototype NewItem(string name) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", false);
                Store(Cache).Items[name] = item;
                return item;
            }

            //Creates the node straight through Control.Viewer's own graph/editor (not a separate fixture
            //graph), so GraphViewer's session-event wiring auto-creates the matching BaseNodeElement with a
            //correctly wired Context - the id->element lookup SetLocation needs actually resolves.
            public BaseNodeElement AddSupplier(string itemName, Point location) {
                ItemPrototype item = NewItem(itemName);
                var id = Control.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(item, Quality), location);
                BaseNodeElement element = Control.Viewer.NodeElementDictionary[id];
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

        //---- leader grid-snap, follower raw delta (reference §2 "Multi-selection group drag") ----

        [AvaloniaFact]
        public void LeaderDrag_SnapsToGrid_WhileFollowerPreservesRawOffset() {
            Fixture fx = NewFixture();
            fx.Control.Grid.ShowGrid = true;
            fx.Control.Grid.CurrentGridUnit = 20;

            BaseNodeElement leader = fx.AddSupplier("iron-ore", new Point(0, 0));
            BaseNodeElement follower = fx.AddSupplier("copper-ore", new Point(105, 205)); //deliberately off-grid
            fx.Control.Viewer.SetSelection([leader, follower]);

            AvaloniaPoint leaderScreen = fx.Control.Viewport.GraphToScreen(leader.Location);
            AvaloniaPoint dragTo = leaderScreen + new AvaloniaPoint(33, 7); //33/7 don't land on the grid on their own
            fx.Window.MouseDown(leaderScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton); //crosses the drag threshold
            fx.Window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton); //arms BaseNodeElement.Dragged
            fx.Window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton); //actually moves + applies the follower delta

            Assert.Equal(new Point(40, 0), leader.Location); //AlignToGrid(0+33, 0+7) with unit 20
            Assert.Equal(new Point(145, 205), follower.Location); //105+40, 205+0 - raw delta, not independently re-snapped
        }

        //---- Shift axis-lock (reference §2 "Shift = axis lock") ----

        [AvaloniaFact]
        public void ShiftAxisLock_PinsWhicheverAxisIsCloserToDragOrigin_AndFlipsOnOvershoot() {
            Fixture fx = NewFixture();
            fx.Control.Grid.ShowGrid = true;
            fx.Control.Grid.CurrentGridUnit = 10;

            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            AvaloniaPoint start = fx.Control.Viewport.GraphToScreen(node.Location);

            fx.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton); //threshold
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton); //arm
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton); //move to (30,0)
            Assert.Equal(new Point(30, 0), node.Location);

            fx.Control.Focus();
            fx.Window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
            Assert.True(fx.Control.Grid.LockDragToAxis);
            Assert.Equal(new Point(30, 0), fx.Control.Grid.DragOrigin); //re-anchored to the node's current, aligned location

            fx.Window.MouseMove(start + new AvaloniaPoint(55, 15), RawInputModifiers.LeftMouseButton | RawInputModifiers.None);
            Assert.Equal(new Point(60, 0), node.Location); //|dx|>|dy| from DragOrigin -> Y pinned, X free

            fx.Window.MouseMove(start + new AvaloniaPoint(35, 40), RawInputModifiers.LeftMouseButton | RawInputModifiers.None);
            Assert.Equal(new Point(30, 40), node.Location); //overshoot flips it: |dy|>|dx| now -> X pinned back to DragOrigin, Y free
        }

        //---- locked-axis line rendering (GridManager.Paint's draggedNodeActive gate, reference §2/§8.12's
        //Grid.Paint call) - GraphViewer.Paint now threads GraphCanvasControl's own drag state through so
        //this can ever draw at all ----

        private static readonly SKColor LockedAxisColor = new(180, 80, 80);

        private static bool ContainsColor(SKSurface surface, SKColor color) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            for (int y = 0; y < pixmap.Height; y++)
                for (int x = 0; x < pixmap.Width; x++)
                    if (pixmap.GetPixelColor(x, y) == color)
                        return true;
            return false;
        }

        [AvaloniaFact]
        public void ShiftAxisLock_DuringItemDrag_RendersLockedAxisLine() {
            Fixture fx = NewFixture();
            fx.Control.Grid.ShowGrid = true;
            fx.Control.Grid.CurrentGridUnit = 10;

            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            AvaloniaPoint start = fx.Control.Viewport.GraphToScreen(node.Location);

            fx.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton); //threshold
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton); //arm
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton); //move to (30,0)

            fx.Control.Focus();
            fx.Window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
            Assert.True(fx.Control.Grid.LockDragToAxis);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surface.Canvas);

            Assert.True(ContainsColor(surface, LockedAxisColor));
        }

        [AvaloniaFact]
        public void LockedAxisLine_AbsentWhenNoDragIsActive() {
            Fixture fx = NewFixture();
            fx.Control.Grid.ShowGrid = true;
            fx.Control.Grid.LockDragToAxis = true; //a stale lock flag with no drag in progress
            fx.Control.Grid.DragOrigin = new Point(30, 0);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surface.Canvas);

            Assert.False(ContainsColor(surface, LockedAxisColor));
        }

        [AvaloniaFact]
        public void LockedAxisLine_AbsentWhenDraggingButNotLocked() {
            Fixture fx = NewFixture();
            fx.Control.Grid.ShowGrid = true;
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            AvaloniaPoint start = fx.Control.Viewport.GraphToScreen(node.Location);

            fx.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(start + new AvaloniaPoint(30, 0), RawInputModifiers.LeftMouseButton);
            Assert.Equal(GraphCanvasControl.DragOperation.Item, fx.Control.CurrentDragOperation);
            Assert.False(fx.Control.Grid.LockDragToAxis);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surface.Canvas);

            Assert.False(ContainsColor(surface, LockedAxisColor));
        }

        //---- sub-threshold press-move-release (reference §1 "Entry conditions") ----

        [AvaloniaFact]
        public void SubThresholdDrag_OnNode_MovesNothing() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            AvaloniaPoint start = fx.Control.Viewport.GraphToScreen(node.Location);

            fx.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(start + new AvaloniaPoint(3, 3), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(start + new AvaloniaPoint(3, 3), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
            Assert.Equal(new Point(0, 0), node.Location);
        }

        //---- tab-vs-node disambiguation (reference §2) ----

        [AvaloniaFact]
        public void Dragged_StartingFromATab_NeverMovesTheNode() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0)); //a supplier's own item is its one output tab
            ItemTabElement tab = node.SubElements.OfType<ItemTabElement>().First();
            Point tabGraphPoint = node.LocalToGraph(tab.Location);

            node.MouseDown(tabGraphPoint);
            node.Dragged(tabGraphPoint + new Size(20, 0), fx.Control.Grid); //first call off a tab hit: never arms (would redirect to a link drag once Task 3+ exists)
            node.Dragged(tabGraphPoint + new Size(40, 0), fx.Control.Grid); //tabHit re-checks the fixed MouseDown location every call, so this still doesn't arm

            Assert.Equal(new Point(0, 0), node.Location);
        }

        //---- arrow-key movement (reference §2/§7) ----

        [AvaloniaFact]
        public void ArrowKeys_MoveSelectedNode_ByGridUnitStep() {
            Fixture fx = NewFixture();
            fx.Control.Grid.CurrentGridUnit = 15;
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(100, 100));
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Assert.Equal(new Point(115, 100), node.Location);

            fx.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal(new Point(115, 115), node.Location);

            fx.Window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            Assert.Equal(new Point(100, 115), node.Location);

            fx.Window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.Equal(new Point(100, 100), node.Location);
        }

        [AvaloniaFact]
        public void ArrowKeys_DefaultToSixUnits_WhenNoGridUnitIsSet() {
            Fixture fx = NewFixture(); //Grid.CurrentGridUnit defaults to 0
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

            Assert.Equal(new Point(6, 0), node.Location);
        }

        [AvaloniaFact]
        public void ArrowKeys_WithShift_UseQuadrupleStep_WhenNoMajorGridUnitIsSet() {
            Fixture fx = NewFixture();
            fx.Control.Grid.CurrentGridUnit = 10; //CurrentMajorGridUnit stays 0, below CurrentGridUnit
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);

            Assert.Equal(new Point(40, 0), node.Location); //10*4
        }

        [AvaloniaFact]
        public void ArrowKeys_WithShift_UseMajorGridUnit_WhenLarger() {
            Fixture fx = NewFixture();
            fx.Control.Grid.CurrentGridUnit = 10;
            fx.Control.Grid.CurrentMajorGridUnit = 50;
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Shift);

            Assert.Equal(new Point(0, 50), node.Location);
        }

        //---- WASD panning (reference §2/§7) ----

        [AvaloniaFact]
        public void WasdKeys_PanTheViewByPanUnit_InEachDirection() {
            Fixture fx = NewFixture();
            fx.AddSupplier("iron-ore", new Point(0, 0)); //Graph.Bounds = (-200,-200,400,400), well clear of these small pans
            fx.Control.Focus();
            Point origin = fx.Control.Viewport.ViewOffset;

            fx.Window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Assert.Equal(new Point(origin.X, origin.Y + 10), fx.Control.Viewport.ViewOffset);

            fx.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            Assert.Equal(new Point(origin.X + 10, origin.Y + 10), fx.Control.Viewport.ViewOffset);

            fx.Window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
            Assert.Equal(new Point(origin.X + 10, origin.Y), fx.Control.Viewport.ViewOffset);

            fx.Window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
            Assert.Equal(origin, fx.Control.Viewport.ViewOffset);
        }

        [AvaloniaFact]
        public void WasdKeys_WithShift_PanFiveTimesFurther() {
            Fixture fx = NewFixture();
            fx.AddSupplier("iron-ore", new Point(0, 0));
            fx.Control.Focus();
            Point origin = fx.Control.Viewport.ViewOffset;

            fx.Window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Shift);

            Assert.Equal(new Point(origin.X, origin.Y + 50), fx.Control.Viewport.ViewOffset); //10*5
        }

        //---- Viewport.PanTo's hard-limit seam (reference §2's carried note from Task 1's review) ----

        [AvaloniaFact]
        public void PlainPan_NearCanvasEdge_ClampsViewOffsetToGraphBounds() {
            Fixture fx = NewFixture();
            fx.AddSupplier("iron-ore", new Point(0, 0)); //Graph.Bounds = (-200,-200,400,400) from the 200-unit XBorder/YBorder

            var origin = new AvaloniaPoint(Half, Half);
            var farPoint = origin + new AvaloniaPoint(5000, 5000);
            fx.Window.MouseDown(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseMove(farPoint, RawInputModifiers.RightMouseButton);

            Point screenCentre = fx.Control.Viewport.ScreenToGraph(new AvaloniaPoint(Half, Half));
            Assert.InRange(screenCentre.X, -210, 210); //clamped to Graph.Bounds, nowhere near the raw ~5000 the pan itself moved
            Assert.InRange(screenCentre.Y, -210, 210);
        }

        [AvaloniaFact]
        public void DragNearCanvasEdge_WhileDraggingNode_DoesNotHardLimitViewport() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(0, 0));
            AvaloniaPoint nodeScreen = fx.Control.Viewport.GraphToScreen(node.Location);

            fx.Window.MouseDown(nodeScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(nodeScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton); //crosses threshold -> Item
            Assert.Equal(GraphCanvasControl.DragOperation.Item, fx.Control.CurrentDragOperation);

            //chords a pan onto the live node drag - since the same node being dragged also defines
            //Graph.Bounds, a bounds range assertion here would be self-referential (the "clamp region"
            //would just chase the dragged node wherever it goes); asserting the exact raw pan formula
            //instead proves no clamp adjustment happened at all, regardless of what Graph.Bounds is.
            //Note for whoever writes the next chorded-button test: in this Avalonia Headless setup, a
            //MouseDown for a second button while the pointer is already captured by an earlier button's
            //press doesn't raise a fresh PointerPressed - it surfaces as a PointerMoved at the same screen
            //point carrying the updated button state instead (confirmed by tracing OnPointerPressed/
            //OnPointerMoved call order here). Not a port behavior issue, just a harness quirk to know about.
            fx.Window.MouseDown(nodeScreen, MouseButton.Middle, RawInputModifiers.LeftMouseButton);
            var farPoint = nodeScreen + new AvaloniaPoint(5000, 5000);
            fx.Window.MouseMove(farPoint, RawInputModifiers.LeftMouseButton | RawInputModifiers.MiddleMouseButton);

            Assert.Equal(new Point(5000, 5000), fx.Control.Viewport.ViewOffset);
        }
    }
}
