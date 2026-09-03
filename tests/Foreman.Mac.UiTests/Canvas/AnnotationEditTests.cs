using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Views;
using Foreman.Models;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;
using InputPointer = Avalonia.Input.Pointer;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises reference §6's annotation editing: 8-handle resize math, drag-move, the parallel annotation
    //selection system (Cmd=add/toggle, Alt=remove, mixed node+annotation lasso), DrawShape rubber-band
    //creation via the background right-click menu, double-click opening the properties dialog, the two
    //properties windows' Cancel-revert/OK-persists-default behavior, and annotation clipboard integration.
    public class AnnotationEditTests {
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
            control.Viewer.Graph.DefaultAssemblerQuality = quality;
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Control = control, Window = window };
        }

        private static ShapeAnnotationElement AddShape(Fixture fx, Point location, int width = 200, int height = 150) {
            var shape = new ShapeAnnotationElement(location, width, height);
            fx.Control.Viewer.AddAnnotationElement(shape);
            return shape;
        }

        //---- 8-handle resize math (reference §6's ApplyResize, derived coords at ViewScale 1) ----

        public static IEnumerable<object[]> HandleResizeCases() {
            // handleX, handleY, dx, dy, expectedX, expectedY, expectedW, expectedH
            yield return new object[] { -100, -75, 20, 10, 10, 5, 180, 140 };   // TopLeft
            yield return new object[] { 0, -75, 20, 10, 0, 5, 200, 140 };       // TopCenter
            yield return new object[] { 100, -75, 20, 10, 10, 5, 220, 140 };    // TopRight
            yield return new object[] { -100, 0, 20, 10, 10, 0, 180, 150 };     // MiddleLeft
            yield return new object[] { 100, 0, 20, 10, 10, 0, 220, 150 };      // MiddleRight
            yield return new object[] { -100, 75, 20, 10, 10, 5, 180, 160 };    // BottomLeft
            yield return new object[] { 0, 75, 20, 10, 0, 5, 200, 160 };        // BottomCenter
            yield return new object[] { 100, 75, 20, 10, 10, 5, 220, 160 };     // BottomRight
        }

        [Theory]
        [MemberData(nameof(HandleResizeCases))]
        public void ApplyResize_EachOfTheEightHandles_MovesOnlyItsOwnEdges(
            int handleX, int handleY, int dx, int dy, int expectedX, int expectedY, int expectedW, int expectedH) {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150) { IsSelected = true };
            var handlePoint = new Point(handleX, handleY);

            shape.MouseDown(handlePoint);
            shape.Dragged(handlePoint); //first call only arms the drag (matches BaseNodeElement.Dragged's pattern)
            shape.Dragged(new Point(handleX + dx, handleY + dy));

            Assert.Equal(new Point(expectedX, expectedY), shape.Location);
            Assert.Equal(expectedW, shape.Width);
            Assert.Equal(expectedH, shape.Height);
        }

        [Fact]
        public void ApplyResize_BelowMinimumSize_ClampsToThirtyGraphUnits() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150) { IsSelected = true };
            var handlePoint = new Point(-100, -75); //TopLeft

            shape.MouseDown(handlePoint);
            shape.Dragged(handlePoint);
            shape.Dragged(new Point(90, 65)); //drags almost across the whole shape

            Assert.Equal(30, shape.Width);
            Assert.Equal(30, shape.Height);
        }

        [Fact]
        public void GetHandleAtPoint_UnselectedAnnotation_NeverActivatesAHandle() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150); //not selected
            var handlePoint = new Point(-100, -75);

            shape.MouseDown(handlePoint);
            shape.Dragged(handlePoint);
            shape.Dragged(new Point(-80, -65));

            //no handle armed -> this was a move, not a resize; bounds unchanged, location shifted by (20,10)
            Assert.Equal(200, shape.Width);
            Assert.Equal(150, shape.Height);
            Assert.Equal(new Point(20, 10), shape.Location);
        }

        //---- drag moves (reference §6's non-resize Dragged branch) ----

        [Fact]
        public void Dragged_CenterClickOnSelectedAnnotation_MovesByRawOffset() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150) { IsSelected = true };
            var center = new Point(0, 0);

            shape.MouseDown(center);
            shape.Dragged(center);
            shape.Dragged(new Point(33, -17));

            Assert.Equal(new Point(33, -17), shape.Location);
            Assert.Equal(200, shape.Width); //pure move, no resize
        }

        [AvaloniaFact]
        public void DraggedAnnotation_LeadsSelection_MovesOtherSelectedAnnotationAndNode_ByTheSameDelta() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(-300, -300));
            ShapeAnnotationElement leader = AddShape(fx, new Point(0, 0), 60, 60);
            ShapeAnnotationElement follower = AddShape(fx, new Point(200, 200), 60, 60);
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.Viewer.SelectedAnnotations.Add(leader);
            fx.Control.Viewer.SelectedAnnotations.Add(follower);
            leader.IsSelected = true;
            follower.IsSelected = true;
            AvaloniaPoint leaderScreen = fx.Control.Viewport.GraphToScreen(leader.Location);

            fx.Window.MouseDown(leaderScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(leaderScreen + new AvaloniaPoint(40, 25), RawInputModifiers.LeftMouseButton); //crosses threshold
            fx.Window.MouseMove(leaderScreen + new AvaloniaPoint(40, 25), RawInputModifiers.LeftMouseButton); //arms AnnotationElement.Dragged
            fx.Window.MouseMove(leaderScreen + new AvaloniaPoint(40, 25), RawInputModifiers.LeftMouseButton); //actually moves + applies the follower delta

            Assert.Equal(new Point(240, 225), follower.Location);
            Assert.Equal(new Point(-260, -275), node.Location);
        }

        //---- parallel annotation selection (reference §1/§6: Cmd=add/toggle, Alt=remove) ----

        [AvaloniaFact]
        public void ClickUnselectedAnnotation_NoModifiers_SelectsIt() {
            Fixture fx = NewFixture();
            ShapeAnnotationElement shape = AddShape(fx, new Point(0, 0));
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Contains(shape, fx.Control.Viewer.SelectedAnnotations);
            Assert.True(shape.IsSelected);
        }

        [AvaloniaFact]
        public void CmdClick_OnUnselectedAnnotation_AddsItWithoutClearingExistingSelection() {
            Fixture fx = NewFixture();
            ShapeAnnotationElement already = AddShape(fx, new Point(-300, -300), 60, 60);
            fx.Control.Viewer.SelectedAnnotations.Add(already);
            already.IsSelected = true;
            ShapeAnnotationElement shape = AddShape(fx, new Point(0, 0));
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.Meta);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.Meta);

            Assert.Contains(shape, fx.Control.Viewer.SelectedAnnotations);
            Assert.Contains(already, fx.Control.Viewer.SelectedAnnotations);
        }

        [AvaloniaFact]
        public void CmdClick_OnSelectedAnnotation_TogglesItOutOfSelection() {
            Fixture fx = NewFixture();
            ShapeAnnotationElement shape = AddShape(fx, new Point(0, 0));
            fx.Control.Viewer.SelectedAnnotations.Add(shape);
            shape.IsSelected = true;
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.Meta);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.Meta);

            Assert.DoesNotContain(shape, fx.Control.Viewer.SelectedAnnotations);
            Assert.False(shape.IsSelected);
        }

        [AvaloniaFact]
        public void AltClick_OnSelectedAnnotation_RemovesItFromSelection() {
            Fixture fx = NewFixture();
            ShapeAnnotationElement shape = AddShape(fx, new Point(0, 0));
            fx.Control.Viewer.SelectedAnnotations.Add(shape);
            shape.IsSelected = true;
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.Alt);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.Alt);

            Assert.DoesNotContain(shape, fx.Control.Viewer.SelectedAnnotations);
            Assert.False(shape.IsSelected);
        }

        [AvaloniaFact]
        public void ClickEmptySpace_NoModifiers_ClearsAnnotationSelectionToo() {
            Fixture fx = NewFixture();
            ShapeAnnotationElement shape = AddShape(fx, new Point(0, 0));
            fx.Control.Viewer.SelectedAnnotations.Add(shape);
            shape.IsSelected = true;
            var farPoint = new AvaloniaPoint(10, 10);

            fx.Window.MouseDown(farPoint, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(farPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Empty(fx.Control.Viewer.SelectedAnnotations);
            Assert.False(shape.IsSelected);
        }

        //---- mixed node+annotation lasso (reference §1's two-parallel-systems lasso commit) ----

        [AvaloniaFact]
        public void LassoDrag_OverBothANodeAndAnAnnotation_SelectsBoth() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", new Point(-100, -100));
            ShapeAnnotationElement shape = AddShape(fx, new Point(100, 100), 60, 60);
            AvaloniaPoint origin = fx.Control.Viewport.GraphToScreen(new Point(-170, -170));
            AvaloniaPoint corner = fx.Control.Viewport.GraphToScreen(new Point(170, 170));

            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);

            Assert.Contains(node, fx.Control.Viewer.SelectedNodes);
            Assert.Contains(shape, fx.Control.Viewer.SelectedAnnotations);
            Assert.True(shape.IsSelected);
        }

        //---- DrawShape rubber-band creation (reference §1/§6, entered via the background right-click menu) ----

        [AvaloniaFact]
        public void RightClickEmptySpace_ShowsAddTextAndAddShapeMenuEntries() {
            Fixture fx = NewFixture();
            var point = new AvaloniaPoint(Half, Half);

            fx.Window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);

            Assert.NotNull(fx.Control.LastContextMenuEntries);
            Assert.Contains(fx.Control.LastContextMenuEntries!, e => e.Caption == "Add Text");
            Assert.Contains(fx.Control.LastContextMenuEntries!, e => e.Caption == "Add Shape");
        }

        [AvaloniaFact]
        public void AddShapeMenuEntry_ThenDragPastMinimumSize_CreatesShapeSizedToTheDrag() {
            Fixture fx = NewFixture();
            var origin = new AvaloniaPoint(Half, Half);
            fx.Window.MouseDown(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Control.LastContextMenuEntries!.Single(e => e.Caption == "Add Shape").Invoke!.Invoke();
            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);

            var corner = origin + new AvaloniaPoint(80, 60);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            Assert.Equal(GraphCanvasControl.DragOperation.DrawShape, fx.Control.CurrentDragOperation);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);

            ShapeAnnotationElement created = Assert.IsType<ShapeAnnotationElement>(Assert.Single(fx.Control.Viewer.Annotations));
            Assert.Equal(80, created.Width);
            Assert.Equal(60, created.Height);
            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
        }

        [AvaloniaFact]
        public void AddShapeMenuEntry_ThenTooSmallDrag_CreatesDefaultSizedShapeAtTheClick() {
            Fixture fx = NewFixture();
            var origin = new AvaloniaPoint(Half, Half);
            fx.Window.MouseDown(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Control.LastContextMenuEntries!.Single(e => e.Caption == "Add Shape").Invoke!.Invoke();

            var tinyCorner = origin + new AvaloniaPoint(15, 10); //past the drag threshold, well under the 30-graph-unit draw minimum on both axes
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(tinyCorner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(tinyCorner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(tinyCorner, MouseButton.Left, RawInputModifiers.None);

            ShapeAnnotationElement created = Assert.IsType<ShapeAnnotationElement>(Assert.Single(fx.Control.Viewer.Annotations));
            Assert.Equal(200, created.Width);
            Assert.Equal(150, created.Height);
        }

        [AvaloniaFact]
        public void Escape_WhileInDrawShapeMode_CancelsWithoutCreatingAnything() {
            Fixture fx = NewFixture();
            var origin = new AvaloniaPoint(Half, Half);
            fx.Window.MouseDown(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Control.LastContextMenuEntries!.Single(e => e.Caption == "Add Shape").Invoke!.Invoke();
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            var corner = origin + new AvaloniaPoint(80, 80);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);

            Assert.Empty(fx.Control.Viewer.Annotations);
        }

        //Regression: the background-menu gate excluded Selection/Item but not DrawShape (upstream :903's
        //own gate excludes all three), so a right-click mid-rubber-band could reopen the background menu -
        //including its own "Add Shape" entry - while a shape was already being drawn.
        //
        //The original version of this test drove the right-click through Window.MouseDown/MouseUp with
        //RawInputModifiers.LeftMouseButton set, meaning to represent a chorded right-click while the left
        //button (drawing the shape) was still held. It was vacuous: Avalonia.Headless's synthetic pointer -
        //like every real desktop backend's shared MouseDevice.ProcessRawEvent, which folds a button
        //transition into a plain PointerMoved instead of a genuine PointerPressed/PointerReleased whenever
        //another button is already held - coalesces that into PointerMoved too, so OnPointerReleased's
        //Right-button case (the gate this test means to exercise) was never reached; the assertion passed
        //because nothing happened at all, not because the gate held. Raises a hand-built
        //PointerReleasedEventArgs directly instead, mirroring ElementMenuTests' documented technique for the
        //same gap on the node-drag right-click-cancel test.
        [AvaloniaFact]
        public void RightClick_MidDrawShapeDrag_DoesNotReopenBackgroundMenu() {
            Fixture fx = NewFixture();
            var origin = new AvaloniaPoint(Half, Half);
            fx.Window.MouseDown(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(origin, MouseButton.Right, RawInputModifiers.None);
            fx.Control.LastContextMenuEntries!.Single(e => e.Caption == "Add Shape").Invoke!.Invoke();
            IReadOnlyList<MenuEntry>? entriesBeforeSecondRightClick = fx.Control.LastContextMenuEntries;

            var corner = origin + new AvaloniaPoint(80, 60);
            fx.Window.MouseDown(origin, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(corner, RawInputModifiers.LeftMouseButton);
            Assert.Equal(GraphCanvasControl.DragOperation.DrawShape, fx.Control.CurrentDragOperation);

            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.RightButtonReleased);
            var releaseArgs = new PointerReleasedEventArgs(fx.Control, pointer, fx.Control, corner, 0, properties, KeyModifiers.None, MouseButton.Right);

            fx.Control.RaiseEvent(releaseArgs);

            Assert.Equal(GraphCanvasControl.DragOperation.DrawShape, fx.Control.CurrentDragOperation);
            Assert.Same(entriesBeforeSecondRightClick, fx.Control.LastContextMenuEntries);
        }

        //---- double-click opens the properties dialog (reference §6, headless-testable via the stub seam) ----

        [AvaloniaFact]
        public void DoubleClick_OnAnAnnotation_InvokesThePropertiesDialogHookExactlyOnce() {
            Fixture fx = NewFixture();
            ShapeAnnotationElement shape = AddShape(fx, new Point(0, 0));
            var invoked = new List<AnnotationElement>();
            fx.Control.AnnotationPropertiesDialogStub = ann => invoked.Add(ann);
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Single(invoked);
            Assert.Same(shape, invoked[0]);
        }

        [AvaloniaFact]
        public void RightClickProperties_OnAnAnnotation_InvokesTheSamePropertiesDialogHook() {
            Fixture fx = NewFixture();
            ShapeAnnotationElement shape = AddShape(fx, new Point(0, 0));
            var invoked = new List<AnnotationElement>();
            fx.Control.AnnotationPropertiesDialogStub = ann => invoked.Add(ann);
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(screenPoint, MouseButton.Right, RawInputModifiers.None);
            fx.Control.LastContextMenuEntries!.Single(e => e.Caption == "Properties").Invoke!.Invoke();

            Assert.Single(invoked);
            Assert.Same(shape, invoked[0]);
        }

        //---- ShapePropertiesWindow / TextPropertiesWindow: live preview, Cancel-revert, OK-persists ----

        [AvaloniaFact]
        public void ShapePropertiesWindow_FieldChange_AppliesLivePreviewImmediately() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150);
            var window = new ShapePropertiesWindow(shape);
            window.Show();

            window.BorderWidthInputControl.Value = 40;

            Assert.Equal(40, shape.BorderWidth);
        }

        [AvaloniaFact]
        public void ShapePropertiesWindow_Cancel_RevertsEveryFieldToTheConstructionSnapshot() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150);
            int originalBorderWidth = shape.BorderWidth;
            var window = new ShapePropertiesWindow(shape);
            window.Show();
            window.BorderWidthInputControl.Value = 40;
            window.FillColorRedInputControl.Value = 250;

            window.ApplyCancel();

            Assert.Equal(originalBorderWidth, shape.BorderWidth);
            Assert.NotEqual((byte)250, shape.FillColor.Red);
        }

        [AvaloniaFact]
        public void ShapePropertiesWindow_Ok_PersistsCurrentValuesToAppSettingsAndStatics() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150);
            var window = new ShapePropertiesWindow(shape);
            window.Show();
            window.BorderWidthInputControl.Value = 17;
            var settings = new Foreman.Mac.Services.AppSettings();
            window.Settings = settings;

            window.ApplyOk();

            Assert.Equal(17, settings.AnnotShapeBorderWidth);
            var freshShape = new ShapeAnnotationElement(new Point(500, 500));
            Assert.Equal(17, freshShape.BorderWidth);
        }

        [AvaloniaFact]
        public void TextPropertiesWindow_FieldChange_AppliesLivePreviewImmediately() {
            var text = new TextAnnotationElement(new Point(0, 0));
            var window = new TextPropertiesWindow(text);
            window.Show();

            window.TextInputControl.Text = "Changed";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("Changed", text.Text);
        }

        [AvaloniaFact]
        public void TextPropertiesWindow_Cancel_RevertsTextAndFontSizeToTheConstructionSnapshot() {
            var text = new TextAnnotationElement(new Point(0, 0)) { Text = "Original" };
            var window = new TextPropertiesWindow(text);
            window.Show();
            window.TextInputControl.Text = "Edited";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); //TextChanged needs a pump in headless tests, see the live-preview test above
            window.FontSizeInputControl.Value = 40;
            Assert.Equal("Edited", text.Text); //guards the revert assertion below against a no-op mutation

            window.ApplyCancel();

            Assert.Equal("Original", text.Text);
            Assert.NotEqual(40f, text.FontSize);
        }

        [AvaloniaFact]
        public void TextPropertiesWindow_Ok_PersistsCurrentValuesToAppSettingsAndStatics() {
            var text = new TextAnnotationElement(new Point(0, 0));
            var window = new TextPropertiesWindow(text);
            window.Show();
            window.FontSizeInputControl.Value = 33;
            var settings = new Foreman.Mac.Services.AppSettings();
            window.Settings = settings;

            window.ApplyOk();

            Assert.Equal("33", settings.AnnotTextFontSize);
            var freshText = new TextAnnotationElement(new Point(500, 500));
            Assert.Equal(33f, freshText.FontSize);
        }

        //---- annotation clipboard integration (reference §5/§9: merged node+annotation payload) ----

        [AvaloniaFact]
        public void Copy_WithSelectedNodeAndAnnotation_MergesAnnotationDataIntoTheNodeFragment() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", Point.Empty);
            ShapeAnnotationElement shape = AddShape(fx, new Point(100, 100), 60, 40);
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.Viewer.SelectedAnnotations.Add(shape);
            shape.IsSelected = true;

            string fragmentJson = NodeClipboard.Copy(fx.Control.Viewer);

            IReadOnlyList<AnnotationSaveData>? annotations = AnnotationClipboardCodec.ReadAnnotations(fragmentJson);
            Assert.NotNull(annotations);
            Assert.Single(annotations!);
            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadGraphPayload(fragmentJson);
            Assert.NotNull(document);
            Assert.Single(document!.Nodes);
        }

        [AvaloniaFact]
        public void Copy_NoAnnotationsSelected_ProducesAPlainNodeOnlyFragment() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", Point.Empty);
            fx.Control.Viewer.SetSelection([node]);

            string fragmentJson = NodeClipboard.Copy(fx.Control.Viewer);

            Assert.Null(AnnotationClipboardCodec.ReadAnnotations(fragmentJson));
        }

        [AvaloniaFact]
        public void Cut_DeletesSelectedAnnotationAlongsideTheNode() {
            Fixture fx = NewFixture();
            BaseNodeElement node = fx.AddSupplier("iron-ore", Point.Empty);
            ShapeAnnotationElement shape = AddShape(fx, new Point(100, 100), 60, 40);
            fx.Control.Viewer.SetSelection([node]);
            fx.Control.Viewer.SelectedAnnotations.Add(shape);
            shape.IsSelected = true;

            NodeClipboard.Cut(fx.Control.Viewer);

            Assert.Empty(fx.Control.Viewer.NodeElements);
            Assert.Empty(fx.Control.Viewer.Annotations);
        }

        [AvaloniaFact]
        public void Paste_MergedFragment_ImportsBothTheNodeAndTheAnnotation() {
            Fixture source = NewFixture();
            BaseNodeElement node = source.AddSupplier("iron-ore", new Point(-30, 0));
            ShapeAnnotationElement shape = AddShape(source, new Point(30, 0), 40, 40);
            source.Control.Viewer.SetSelection([node]);
            source.Control.Viewer.SelectedAnnotations.Add(shape);
            shape.IsSelected = true;
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture();
            var origin = new Point(300, -200);

            NodeClipboard.Paste(target.Control.Viewer, target.Cache, fragmentJson, origin);

            Assert.Single(target.Control.Viewer.NodeElements);
            var pastedAnnotation = Assert.IsType<ShapeAnnotationElement>(Assert.Single(target.Control.Viewer.Annotations));
            Assert.Contains(pastedAnnotation, target.Control.Viewer.SelectedAnnotations);
        }

        [AvaloniaFact]
        public void Paste_AnnotationOnlyFragment_ImportsWithoutRequiringAnyNodes() {
            Fixture source = NewFixture();
            ShapeAnnotationElement shape = AddShape(source, new Point(0, 0), 40, 40);
            source.Control.Viewer.SelectedAnnotations.Add(shape);
            shape.IsSelected = true;
            string fragmentJson = NodeClipboard.Copy(source.Control.Viewer);

            Fixture target = NewFixture();

            NodeClipboard.Paste(target.Control.Viewer, target.Cache, fragmentJson, new Point(0, 0));

            Assert.Empty(target.Control.Viewer.NodeElements);
            Assert.Single(target.Control.Viewer.Annotations);
        }
    }
}
