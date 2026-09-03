using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVector = Avalonia.Vector;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises reference §1's DragOperation state machine: entry-threshold gating, MouseDown/Move/Up
    //routing, the selectedNodes/currentSelectionNodes lasso model, and pan/selection coexistence.
    public class SelectionTests {
        private const int Half = 200;

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }

            public ItemPrototype NewItem(string name) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", false);
                Store(Cache).Items[name] = item;
                return item;
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
            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Graph = graph, Session = session, Control = control, Window = window };
        }

        private static SupplierNodeElement AddSupplier(Fixture fx, string itemName, Point location) {
            ItemPrototype item = fx.NewItem(itemName);
            fx.Graph.CreateSupplierNode(new ItemQualityPair(item, fx.Quality), location);
            ISupplierNodeViewModel viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Last();
            var element = new SupplierNodeElement(fx.Control.Viewer.Context, viewModel);
            element.PrePaint();
            fx.Control.NodeElements.Add(element);
            fx.Control.Viewer.NodeElementDictionary.Add(viewModel.Id, element);
            return element;
        }

        //---- entry threshold (reference §1 "Entry conditions") ----

        [AvaloniaFact]
        public void SubThresholdMovement_StaysDragOperationNone() {
            Fixture fx = NewFixture();
            var origin = new AvaloniaPoint(Half, Half);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);

            fx.Window.MouseMove(origin + new AvaloniaPoint(3, 3), RawInputModifiers.None);

            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
        }

        [AvaloniaFact]
        public void PastThresholdMovement_OnEmptySpace_EntersSelection() {
            Fixture fx = NewFixture();
            var origin = new AvaloniaPoint(Half, Half);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);

            fx.Window.MouseMove(origin + new AvaloniaPoint(20, 20), RawInputModifiers.LeftMouseButton);

            Assert.Equal(GraphCanvasControl.DragOperation.Selection, fx.Control.CurrentDragOperation);
        }

        //---- lasso select + empty-space deselect (reference §1 MouseDown/Up) ----

        [AvaloniaFact]
        public void LassoDrag_AroundSingleNode_SelectsItOnCommit() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            AvaloniaPoint center = fx.Control.Viewport.GraphToScreen(Point.Empty);
            var origin = center + new AvaloniaPoint(-80, -80);
            var corner = center + new AvaloniaPoint(80, 80);

            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton); //first move past the threshold only flips the state, matching upstream's non-fallthrough switch
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton); //second move (same point) is what actually recomputes the zone
            fx.Window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);

            Assert.Contains(node, fx.Control.Viewer.SelectedNodes);
            Assert.True(node.Highlighted);
            Assert.Empty(fx.Control.Viewer.CurrentSelectionNodes);
            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
        }

        [AvaloniaFact]
        public void ClickEmptySpace_NoModifiers_ClearsExistingSelection() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);
            Assert.True(node.Highlighted);

            var farPoint = new AvaloniaPoint(10, 10); //well within the window, far from the node's own screen position
            fx.Window.MouseDown(farPoint, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(farPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.DoesNotContain(node, fx.Control.Viewer.SelectedNodes);
            Assert.False(node.Highlighted);
        }

        //---- Cmd = add/toggle, Alt = subtract (reference §1's MouseUp None+node branches) ----

        [AvaloniaFact]
        public void CmdClick_OnUnselectedNode_TogglesItIntoSelection() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.Meta);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.Meta);

            Assert.Contains(node, fx.Control.Viewer.SelectedNodes);
            Assert.True(node.Highlighted);
        }

        [AvaloniaFact]
        public void CmdClick_OnSelectedNode_TogglesItOutOfSelection() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.Meta);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.Meta);

            Assert.DoesNotContain(node, fx.Control.Viewer.SelectedNodes);
            Assert.False(node.Highlighted);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): Ctrl-click
        //on Linux does the same thing Cmd-click does on macOS, via the UseIsMacOs seam.
        [AvaloniaFact]
        public void CtrlClick_OnUnselectedNode_OnLinux_TogglesItIntoSelection() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.Control);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.Control);

            Assert.Contains(node, fx.Control.Viewer.SelectedNodes);
            Assert.True(node.Highlighted);
        }

        [AvaloniaFact]
        public void AltClick_OnSelectedNode_RemovesItFromSelection() {
            Fixture fx = NewFixture();
            SupplierNodeElement node = AddSupplier(fx, "iron-ore", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.Alt);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.Alt);

            Assert.DoesNotContain(node, fx.Control.Viewer.SelectedNodes);
            Assert.False(node.Highlighted);
        }

        //---- keepGroupSelection: a plain click on an already-selected node keeps the whole group ----

        [AvaloniaFact]
        public void KeepGroupSelection_ClickOnAlreadySelectedNode_KeepsGroupSelected() {
            Fixture fx = NewFixture();
            SupplierNodeElement nodeA = AddSupplier(fx, "iron-ore", new Point(-200, 0));
            SupplierNodeElement nodeB = AddSupplier(fx, "copper-ore", new Point(200, 0));
            fx.Control.Viewer.SetSelection([nodeA, nodeB]);
            AvaloniaPoint screenA = fx.Control.Viewport.GraphToScreen(nodeA.Location);

            fx.Window.MouseDown(screenA, MouseButton.Left, RawInputModifiers.None);

            Assert.Contains(nodeA, fx.Control.Viewer.SelectedNodes);
            Assert.Contains(nodeB, fx.Control.Viewer.SelectedNodes);
            Assert.True(nodeB.Highlighted);
        }

        [AvaloniaFact]
        public void ClickOnUnselectedNode_WithoutKeepGroupSelection_ClearsPriorGroup() {
            Fixture fx = NewFixture();
            SupplierNodeElement nodeA = AddSupplier(fx, "iron-ore", new Point(-200, 0));
            SupplierNodeElement nodeB = AddSupplier(fx, "copper-ore", new Point(200, 0));
            fx.Control.Viewer.SetSelection([nodeA]);
            AvaloniaPoint screenB = fx.Control.Viewport.GraphToScreen(nodeB.Location);

            fx.Window.MouseDown(screenB, MouseButton.Left, RawInputModifiers.None);

            Assert.DoesNotContain(nodeA, fx.Control.Viewer.SelectedNodes);
            Assert.False(nodeA.Highlighted);
        }

        //---- lasso contents at derived coordinates across zoom (reference §1's IntersectsWithZone(zone,-20,-20)) ----

        [AvaloniaTheory]
        [InlineData(0.5f)]
        [InlineData(1f)]
        [InlineData(2f)]
        public void LassoDrag_CurrentSelectionNodes_MatchesIntersectingNodesAcrossZoom(float scale) {
            Fixture fx = NewFixture();
            fx.Control.Viewport.ViewScale = scale;
            fx.Control.Viewport.UpdateGraphBounds();
            SupplierNodeElement inside = AddSupplier(fx, "iron-ore", new Point(0, 0));
            SupplierNodeElement outside = AddSupplier(fx, "copper-ore", new Point(5000, 5000));

            AvaloniaPoint origin = fx.Control.Viewport.GraphToScreen(new Point(-60, -60));
            AvaloniaPoint corner = fx.Control.Viewport.GraphToScreen(new Point(60, 60));
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);

            Assert.Contains(inside, fx.Control.Viewer.CurrentSelectionNodes);
            Assert.DoesNotContain(outside, fx.Control.Viewer.CurrentSelectionNodes);
            Assert.True(inside.Highlighted);
        }

        //---- pan/selection coexistence: a middle-button chord mid-lasso-drag still pans the view ----

        [AvaloniaFact]
        public void MiddleButtonChord_DuringSelectionDrag_StillPansTheView() {
            Fixture fx = NewFixture();
            //Keeps fx.Control.Viewer.Graph.Bounds non-empty, away from the lasso's click path, so the plain
            //pan below isn't reset to (0,0) by Task 2's Graph.Bounds clamp (reference §2's carried note).
            //Goes straight through fx.Control.Viewer's own graph/editor, not the AddSupplier helper's
            //separate fx.Graph, since that's the graph Viewport.PanTo now reads bounds from.
            ItemPrototype farItem = fx.NewItem("far-iron-ore");
            fx.Control.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(farItem, fx.Quality), new Point(2000, 2000));
            var origin = new AvaloniaPoint(Half, Half);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(origin + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);
            Assert.Equal(GraphCanvasControl.DragOperation.Selection, fx.Control.CurrentDragOperation);

            System.Drawing.Point offsetBeforePan = fx.Control.Viewport.ViewOffset;
            fx.Window.MouseDown(origin + new AvaloniaPoint(40, 0), MouseButton.Middle, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(origin + new AvaloniaPoint(80, 0), RawInputModifiers.LeftMouseButton | RawInputModifiers.MiddleMouseButton);

            Assert.Equal(GraphCanvasControl.DragOperation.Selection, fx.Control.CurrentDragOperation);
            Assert.NotEqual(offsetBeforePan, fx.Control.Viewport.ViewOffset);
        }

        //---- lasso rectangle rendering (reference §1 Paint's selectionPen: RGB(100,100,200), 2px screen width) ----

        [AvaloniaFact]
        public void LassoDrag_RendersSelectionZoneRectangleInSelectionPenColor() {
            Fixture fx = NewFixture();
            var origin = new AvaloniaPoint(Half - 60, Half - 60);
            var corner = new AvaloniaPoint(Half + 60, Half + 60);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surface.Canvas);

            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            var expected = new SKColor(100, 100, 200);
            bool found = false;
            for (int x = Half - 61; x <= Half - 59 && !found; x++)
                for (int y = Half - 61; y <= Half + 61 && !found; y++)
                    if (pixmap.GetPixelColor(x, y) == expected)
                        found = true;
            Assert.True(found);
        }

        //---- modifier change mid-lasso (upstream ProductionGraphViewer.cs:1125-1128/1151-1154's KeyDown/
        //KeyUp re-preview): CommitLassoSelection reads the modifier fresh at button-up, so a stale preview
        //left over from a modifier that's since changed would show a selection that disagrees with what
        //actually gets committed - a surprise-deletion risk if the user reads the highlight to decide what
        //they're about to affect. Both directions below assert every node's Highlighted flag still agrees
        //with SelectedNodes membership after the commit. ----

        [AvaloniaFact]
        public void LassoDrag_AltReleasedBeforeButtonUp_HighlightMatchesCommittedSelection() {
            Fixture fx = NewFixture();
            SupplierNodeElement nodeA = AddSupplier(fx, "iron-ore", new Point(-5000, -5000));
            SupplierNodeElement nodeB = AddSupplier(fx, "copper-ore", new Point(5000, 5000));
            SupplierNodeElement nodeC = AddSupplier(fx, "coal", Point.Empty);
            fx.Control.Viewer.SetSelection([nodeA, nodeB]);

            AvaloniaPoint center = fx.Control.Viewport.GraphToScreen(Point.Empty);
            var origin = center + new AvaloniaPoint(-80, -80);
            var corner = center + new AvaloniaPoint(80, 80);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.Alt);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            Assert.Equal(GraphCanvasControl.DragOperation.Selection, fx.Control.CurrentDragOperation);
            Assert.Contains(nodeC, fx.Control.Viewer.CurrentSelectionNodes);

            fx.Control.Focus();
            fx.Window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None); //releases Alt without moving the mouse
            fx.Window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);

            Assert.DoesNotContain(nodeA, fx.Control.Viewer.SelectedNodes);
            Assert.DoesNotContain(nodeB, fx.Control.Viewer.SelectedNodes);
            Assert.Contains(nodeC, fx.Control.Viewer.SelectedNodes);
            foreach (SupplierNodeElement node in new[] { nodeA, nodeB, nodeC })
                Assert.Equal(fx.Control.Viewer.SelectedNodes.Contains(node), node.Highlighted);
        }

        [AvaloniaFact]
        public void LassoDrag_CmdPressedBeforeButtonUp_HighlightMatchesCommittedSelection() {
            Fixture fx = NewFixture();
            SupplierNodeElement nodeA = AddSupplier(fx, "iron-ore", new Point(-5000, -5000));
            SupplierNodeElement nodeB = AddSupplier(fx, "coal", Point.Empty);
            fx.Control.Viewer.SetSelection([nodeA]);

            AvaloniaPoint center = fx.Control.Viewport.GraphToScreen(Point.Empty);
            var origin = center + new AvaloniaPoint(-80, -80);
            var corner = center + new AvaloniaPoint(80, 80);
            //Starts held on Alt (not plain), since a plain background press clears the existing selection
            //outright at MouseDown time regardless of any later modifier change (SelectionTests'
            //ClickEmptySpace_NoModifiers_ClearsExistingSelection covers that unconditional clear) - holding
            //a modifier from the start is the only way nodeA survives to the commit below.
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.Alt);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            Assert.Equal(GraphCanvasControl.DragOperation.Selection, fx.Control.CurrentDragOperation);
            Assert.Contains(nodeB, fx.Control.Viewer.CurrentSelectionNodes);

            fx.Control.Focus();
            fx.Window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
            fx.Window.KeyPressQwerty(PhysicalKey.MetaLeft, RawInputModifiers.Meta); //presses Cmd without moving the mouse
            fx.Window.MouseUp(corner, MouseButton.Left, RawInputModifiers.Meta);

            Assert.Contains(nodeA, fx.Control.Viewer.SelectedNodes);
            Assert.Contains(nodeB, fx.Control.Viewer.SelectedNodes);
            foreach (SupplierNodeElement node in new[] { nodeA, nodeB })
                Assert.Equal(fx.Control.Viewer.SelectedNodes.Contains(node), node.Highlighted);
        }

        //---- graph-bounds clamp wired at zoom and post-load fit (final-review item 2's missing call sites) ----

        [AvaloniaFact]
        public void MouseWheelZoom_FarOutsideGraphBounds_ClampsViewBackToTheGraph() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            fx.Control.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(item, fx.Quality), Point.Empty); //Graph.Bounds ~ (-200,-200,400,400)
            fx.Control.Viewport.ViewOffset = new Point(5000, 5000); //bypasses the clamp, unlike every real view-changing call

            fx.Window.MouseWheel(new AvaloniaPoint(Half, Half), new AvaloniaVector(0, -1)); //zoom out

            Point screenCentre = fx.Control.Viewport.ScreenToGraph(new AvaloniaPoint(Half, Half));
            Assert.InRange(screenCentre.X, -210, 210);
            Assert.InRange(screenCentre.Y, -210, 210);
        }

        [AvaloniaFact]
        public void LoadDocument_SavedViewFarOutsideGraphBounds_ClampsViewBackToTheGraph() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-ore");
            var sourceGraph = new ProductionGraph { DefaultAssemblerQuality = fx.Quality };
            sourceGraph.CreateSupplierNode(new ItemQualityPair(item, fx.Quality), Point.Empty); //Graph.Bounds ~ (-200,-200,400,400)

            var saveDocument = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(sourceGraph),
                Ui = new GraphViewerUiSaveData { ViewScale = 1f, ViewOffset = new Point(5000, 5000) }, //stale/mismatched saved view
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(saveDocument);

            var viewer = new GraphViewer(new Viewport(2 * Half, 2 * Half), new GridManager());
            GraphLoadResult result = viewer.LoadDocument(fx.Cache, json);
            Assert.True(result.Success, result.ErrorMessage);

            Point screenCentre = viewer.Viewport.ScreenToGraph(new AvaloniaPoint(Half, Half));
            Assert.InRange(screenCentre.X, -210, 210);
            Assert.InRange(screenCentre.Y, -210, 210);
        }
    }
}
