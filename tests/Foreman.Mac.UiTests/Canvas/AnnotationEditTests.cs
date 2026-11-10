using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
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

        [AvaloniaFact]
        public async Task AddingTextAnnotation_RaisesOneGraphMutated_RemovingItRaisesOne() {
            Fixture fx = NewFixture();
            int mutatedCount = 0;
            fx.Control.Viewer.Session.GraphMutated += (_, _) => mutatedCount++;
            fx.Control.TextPropertiesDialogStub = _ => Task.FromResult(true);

            await fx.Control.AddTextAnnotationAsync(new Point(0, 0));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, mutatedCount);
            AnnotationElement added = fx.Control.Viewer.Annotations.Single();

            mutatedCount = 0;
            fx.Control.Viewer.RemoveAnnotationElement(added);

            Assert.Equal(1, mutatedCount);
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

            CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);

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

        private static void ClickSwatch(AvaloniaWindow window, ColorWell well, int cell) {
            well.IsOpen = true;
            AnimationClock.UntilPopoverOpen(well);
            PickCell target = Avalonia.Controls.TopLevel.GetTopLevel(well)!.GetVisualDescendants().OfType<PickCell>()
                .Where(c => c.FindAncestorOfType<PopoverFrame>()?.Anchor == well).ElementAt(cell);
            SheetFixture.Click(window, target);
        }

        private static Avalonia.Input.IInputElement? Focused(Avalonia.Controls.Control inside) =>
            Avalonia.Controls.TopLevel.GetTopLevel(inside)!.FocusManager!.GetFocusedElement();

        private static AutomationPeer FocusedPeer(AvaloniaWindow window) =>
            ((WindowBaseAutomationPeer)ControlAutomationPeer.CreatePeerForElement(window)).GetFocus()!;

        private static void AssertTabVisits(Avalonia.Controls.Control inside, params Avalonia.Controls.Control[] order) {
            foreach (Avalonia.Controls.Control expected in order) {
                CanvasKeys.KeyDownOn((Avalonia.Controls.Control)Focused(inside)!, Key.Tab);
                Assert.True(expected.IsKeyboardFocusWithin, $"Tab should reach {expected.Name ?? expected.GetType().Name}, reached {Focused(inside)}");
            }
        }

        private static void AssertRowsHugTheirControls(FormRow[] rows) {
            foreach (FormRow row in rows) {
                double control = ((Avalonia.Controls.Control)row.Content!).Bounds.Height;
                double label = row.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().First(t => t.Name == "PART_Label").Bounds.Height;
                Assert.Equal(Math.Max(Math.Max(control, label), 22), row.Bounds.Height, 0.5);
            }
        }

        [AvaloniaFact]
        public void ShapeProperties_Body_IsFiveFormRows_WithWellsInPlaceOfTheRgbInputsAndNoFillCheckbox() {
            var sheet = new ShapePropertiesSheet(new ShapeAnnotationElement(new Point(0, 0), 200, 150));
            (_, SheetHost host, _) = SheetFixture.Present(sheet);

            FormRow[] rows = [.. sheet.GetVisualDescendants().OfType<FormRow>()];
            Assert.Equal(["Shape", "Fill", "Opacity", "Border", "Border width"], rows.Select(r => r.Label));
            Assert.All(rows, r => Assert.Equal(FormRowLabelColumn.Window, r.LabelColumn));
            Assert.Empty(sheet.GetVisualDescendants().OfType<Avalonia.Controls.NumericUpDown>());
            Assert.Empty(sheet.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>());
            Assert.Empty(sheet.GetVisualDescendants().OfType<Avalonia.Controls.ComboBox>());
            Assert.Equal(["Rectangle", "Ellipse"], sheet.ShapeSegmentsControl.Items);
            Assert.True(sheet.FillWellControl.AllowsNone);
            Assert.True(sheet.FillWellControl.AllowsAlpha);
            Assert.False(sheet.BorderWellControl.AllowsNone);
            Assert.False(sheet.BorderWellControl.AllowsAlpha);
            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Shape properties", SheetFixture.FrameOf(host, sheet).Title);
            Assert.Equal([sheet.CancelButtonControl, sheet.OKButtonControl], sheet.Buttons!.Items.Cast<Avalonia.Controls.Button>());
            Assert.Same(sheet.OKButtonControl, sheet.Buttons.ResolveDefaultButton());
            AssertRowsHugTheirControls(rows);
            Assert.Equal(SheetFixture.Token("Foreman.Size.SliderHeight"), rows[2].Bounds.Height, 0.5);
            Slider slider = sheet.OpacitySliderControl;
            Assert.Equal(sheet.Bounds.Width - 144, slider.Bounds.Width, 0.5);
            Assert.Equal(slider.Bounds.Width - 44, slider.GetVisualDescendants().OfType<Avalonia.Controls.Border>().Single(b => b.Name == "PART_Track").Bounds.Width, 0.5);
            Assert.Equal(rows.Sum(r => r.Bounds.Height) + 4 * 8, sheet.Bounds.Height, 0.5);
            Avalonia.Controls.Control[] controls = [sheet.ShapeSegmentsControl, sheet.FillWellControl, sheet.OpacitySliderControl, sheet.BorderWellControl, sheet.BorderWidthStepperControl];
            for (int i = 0; i < controls.Length; i++) {
                Assert.Equal(144, Avalonia.VisualExtensions.TranslatePoint(controls[i], new AvaloniaPoint(0, 0), sheet)!.Value.X, 0.5);
                Assert.Equal(rows[i].Label, Avalonia.Automation.AutomationProperties.GetName(controls[i]));
            }
        }

        [AvaloniaFact]
        public void ShapeProperties_Fields_ApplyLive_OpacityIsTheFillAlphaInPercent() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150) {
                CurrentShapeType = ShapeAnnotationElement.ShapeType.Rectangle,
                FillColor = new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 102),
                BorderWidth = 2,
            };
            var sheet = new ShapePropertiesSheet(shape);
            (AvaloniaWindow window, _, _) = SheetFixture.Present(sheet);

            Assert.Equal(0, sheet.ShapeSegmentsControl.SelectedIndex);
            AutomationPeer shapePeer = ControlAutomationPeer.CreatePeerForElement(sheet.ShapeSegmentsControl);
            Assert.Equal("Shape", shapePeer.GetName());
            AutomationPeer rectangle = shapePeer.GetChildren()[0];
            Assert.Equal((AutomationControlType.RadioButton, "Rectangle, 1 of 2", true),
                (rectangle.GetAutomationControlType(), rectangle.GetName(), rectangle.GetProvider<ISelectionItemProvider>()!.IsSelected));
            AutomationPeer opacityPeer = ControlAutomationPeer.CreatePeerForElement(sheet.OpacitySliderControl);
            Assert.Equal((AutomationControlType.Slider, "Opacity", "40 percent"),
                (opacityPeer.GetAutomationControlType(), opacityPeer.GetName(), opacityPeer.GetProvider<IValueProvider>()!.Value));
            AutomationPeer widthPeer = ControlAutomationPeer.CreatePeerForElement(sheet.BorderWidthStepperControl);
            Assert.Equal(("stepper", "Border width", "2 pixels"),
                (widthPeer.GetLocalizedControlType(), widthPeer.GetName(), widthPeer.GetProvider<IValueProvider>()!.Value));
            sheet.ShapeSegmentsControl.SelectedIndex = 1;
            Assert.Equal(ShapeAnnotationElement.ShapeType.Ellipse, shape.CurrentShapeType);

            Slider slider = sheet.OpacitySliderControl;
            Assert.Equal((0d, 100d, 1d, 10d), (slider.Minimum, slider.Maximum, slider.Step, slider.PageStep));
            Assert.True(slider.IsEnabled);
            Assert.Equal("40\u202F%", slider.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(t => t.Name == "PART_ValueLabel").Text);
            slider.Value = 75;
            Assert.Equal(new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 191), shape.FillColor);
            slider.Value = 0;
            Assert.Equal(new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 0), shape.FillColor);
            Assert.Equal("None", ((Avalonia.Automation.Provider.IValueProvider)Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(sheet.FillWellControl)).Value);
            slider.Value = 100;
            Assert.Equal(new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 255), shape.FillColor);

            ClickSwatch(window, sheet.BorderWellControl, 13);
            Assert.Equal(new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 255), shape.BorderColor);

            Stepper stepper = sheet.BorderWidthStepperControl;
            Assert.Equal((0d, 50d, 1d, 0), (stepper.Minimum, stepper.Maximum, stepper.Increment, stepper.Decimals));
            AssertUnitFourAfter(sheet, stepper, "px");
            stepper.Value = 40;
            Assert.Equal(40, shape.BorderWidth);
        }

        [AvaloniaFact]
        public void ShapeProperties_ChoosingNoneInTheFillPopover_SetsAlphaZero_KeepsRgb_AndZeroesTheSlider() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150) { FillColor = new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 102) };
            var sheet = new ShapePropertiesSheet(shape);
            (AvaloniaWindow window, _, Task<bool> result) = SheetFixture.Present(sheet);

            ClickSwatch(window, sheet.FillWellControl, 0);

            Assert.Equal(new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 0), shape.FillColor);
            Assert.Equal(0, sheet.OpacitySliderControl.Value);
            Assert.False(sheet.FillWellControl.IsOpen);
            Assert.False(result.IsCompleted);
        }

        private static void AssertUnitFourAfter(Avalonia.Controls.Control sheet, Avalonia.Controls.Control control, string unit) {
            Avalonia.Controls.TextBlock label = ((Avalonia.Controls.Panel)control.Parent!).Children.OfType<Avalonia.Controls.TextBlock>().Single();
            Assert.Equal(unit, label.Text);
            double controlRight = Avalonia.VisualExtensions.TranslatePoint(control, new AvaloniaPoint(control.Bounds.Width, 0), sheet)!.Value.X;
            Assert.Equal(controlRight + 4, Avalonia.VisualExtensions.TranslatePoint(label, new AvaloniaPoint(0, 0), sheet)!.Value.X, 0.5);
            Assert.Contains("secondary", label.Classes);
            Assert.Contains("callout", label.Classes);
        }

        [AvaloniaFact]
        public void ShapeProperties_Cancel_RestoresTheSnapshotExactly() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150) {
                CurrentShapeType = ShapeAnnotationElement.ShapeType.Rectangle,
                FillColor = new SkiaSharp.SKColor(10, 20, 30, 40),
                BorderColor = new SkiaSharp.SKColor(50, 60, 70, 255),
                BorderWidth = 3,
            };
            var sheet = new ShapePropertiesSheet(shape);
            (AvaloniaWindow window, _, _) = SheetFixture.Present(sheet);
            sheet.ShapeSegmentsControl.SelectedIndex = 1;
            ClickSwatch(window, sheet.FillWellControl, 9);
            sheet.OpacitySliderControl.Value = 90;
            ClickSwatch(window, sheet.BorderWellControl, 13);
            sheet.BorderWidthStepperControl.Value = 40;

            sheet.Cancel();

            Assert.Equal(ShapeAnnotationElement.ShapeType.Rectangle, shape.CurrentShapeType);
            Assert.Equal(new SkiaSharp.SKColor(10, 20, 30, 40), shape.FillColor);
            Assert.Equal(new SkiaSharp.SKColor(50, 60, 70, 255), shape.BorderColor);
            Assert.Equal(3, shape.BorderWidth);
        }

        [AvaloniaFact]
        public void ShapeProperties_Ok_PersistsCurrentValuesToAppSettingsAndStatics() {
            var shape = new ShapeAnnotationElement(new Point(0, 0), 200, 150);
            var sheet = new ShapePropertiesSheet(shape);
            SheetFixture.Present(sheet);
            sheet.BorderWidthStepperControl.Value = 17;
            var settings = new Foreman.Mac.Services.AppSettings();
            sheet.Settings = settings;

            sheet.ApplyOk();

            Assert.Equal(17, settings.AnnotShapeBorderWidth);
            var freshShape = new ShapeAnnotationElement(new Point(500, 500));
            Assert.Equal(17, freshShape.BorderWidth);
        }

        [AvaloniaFact]
        public void ShapeProperties_Keyboard_StartsOnShape_TabWalksTheRows_AndAnOpenPopoverKeepsReturnAndEscape() {
            var sheet = new ShapePropertiesSheet(new ShapeAnnotationElement(new Point(0, 0), 200, 150) {
                CurrentShapeType = ShapeAnnotationElement.ShapeType.Rectangle,
                FillColor = new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 102),
                BorderWidth = 2,
            });
            (AvaloniaWindow window, _, Task<bool> result) = SheetFixture.Present(sheet);
            Assert.True(sheet.ShapeSegmentsControl.IsKeyboardFocusWithin);
            AssertTabVisits(sheet, sheet.FillWellControl, sheet.OpacitySliderControl);
            AutomationPeer opacity = FocusedPeer(window);
            Assert.Equal((AutomationControlType.Slider, "Opacity", 40d, "40 percent"),
                (opacity.GetAutomationControlType(), opacity.GetName(), opacity.GetProvider<IRangeValueProvider>()!.Value, opacity.GetProvider<IValueProvider>()!.Value));
            AssertTabVisits(sheet, sheet.BorderWellControl, sheet.BorderWidthStepperControl);
            AutomationPeer width = FocusedPeer(window);
            Assert.Equal(("stepper", "Border width", 2d, "2 pixels"),
                (width.GetLocalizedControlType(), width.GetName(), width.GetProvider<IRangeValueProvider>()!.Value, width.GetProvider<IValueProvider>()!.Value));
            AssertTabVisits(sheet, sheet.CancelButtonControl, sheet.OKButtonControl, sheet.ShapeSegmentsControl);
            AutomationPeer shape = FocusedPeer(window);
            Assert.Equal(("Shape", "Rectangle, 1 of 2"), (shape.GetName(), shape.GetProvider<IValueProvider>()!.Value));

            foreach (Key key in new[] { Key.Enter, Key.Escape }) {
                sheet.FillWellControl.IsOpen = true;
                AnimationClock.Settle(300);
                PickGrid grid = Avalonia.Controls.TopLevel.GetTopLevel(sheet)!.GetVisualDescendants().OfType<PickGrid>().Single(g => g.IsChoiceMode);
                CanvasKeys.KeyDownOn(grid, key);
                Dispatcher.UIThread.RunJobs();

                Assert.False(sheet.FillWellControl.IsOpen);
                Assert.False(result.IsCompleted);
            }
        }

        [AvaloniaFact]
        public void TextProperties_Body_IsTheFullWidthTextField_ThenSixFormRows_WithNoPreviewTransparentCheckboxOrRgbInputs() {
            var sheet = new TextPropertiesSheet(new TextAnnotationElement(new Point(0, 0)) { FontFamily = "Menlo" });
            (_, SheetHost host, _) = SheetFixture.Present(sheet);

            FormRow[] rows = [.. sheet.GetVisualDescendants().OfType<FormRow>()];
            Assert.Equal(["Font", "Size", "Style", "Alignment", "Color", "Background"], rows.Select(r => r.Label));
            Assert.Empty(sheet.GetVisualDescendants().OfType<Avalonia.Controls.NumericUpDown>());
            Assert.Empty(sheet.GetVisualDescendants().OfType<Avalonia.Controls.RadioButton>());
            Assert.DoesNotContain(sheet.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>(), t => t.Text == "AaBbYyZz");
            Assert.DoesNotContain(sheet.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>(), c => Equals(c.Content, "Transparent background"));
            Avalonia.Controls.TextBox text = sheet.TextInputControl;
            Assert.Equal(sheet.Bounds.Width, text.Bounds.Width, 0.5);
            Assert.Equal(66, text.Bounds.Height, 0.5);
            Assert.Equal(16, Avalonia.VisualExtensions.TranslatePoint(rows[0], new AvaloniaPoint(0, 0), sheet)!.Value.Y - text.Bounds.Bottom, 0.5);
            Assert.Equal("Text", Avalonia.Automation.AutomationProperties.GetName(text));
            Assert.True(text.AcceptsReturn);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, text.TextWrapping);
            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Text properties", SheetFixture.FrameOf(host, sheet).Title);
            Assert.Same(sheet.OKButtonControl, sheet.Buttons!.ResolveDefaultButton());
            AssertRowsHugTheirControls(rows);
            Assert.Equal(66 + 16 + rows.Sum(r => r.Bounds.Height) + 5 * 8, sheet.Bounds.Height, 0.5);
            Assert.Equal(["Segoe UI", "Helvetica Neue", "Arial", "Times New Roman", "Courier New", "Menlo"], sheet.FontPopUpControl.Items);
            Assert.Equal("Menlo", sheet.FontPopUpControl.SelectedItem);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, sheet.FontPopUpControl.HorizontalAlignment);
            Assert.Equal(sheet.Bounds.Width - 144, sheet.FontPopUpControl.Bounds.Width, 0.5);
            sheet.FontPopUpControl.IsOpen = true;
            AnimationClock.Settle(300);
            Avalonia.Controls.TextBlock[] fontRows = [.. sheet.FontPopUpControl.Menu.GetVisualDescendants().OfType<Avalonia.Controls.StackPanel>().Single(p => p.Name == "PART_Items")
                .Children.Select(row => row.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single())];
            Assert.Equal(6, fontRows.Length);
            Assert.All(fontRows, row => Assert.Equal(row.Text, row.FontFamily.Name));
            sheet.FontPopUpControl.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(6, sheet.FontSizeStepperControl.Minimum);
            Assert.Equal(288, sheet.FontSizeStepperControl.Maximum);
            AssertUnitFourAfter(sheet, sheet.FontSizeStepperControl, "pt");
            Assert.Equal("Bold", sheet.BoldCheckBoxControl.Content);
            Assert.Equal("Italic", sheet.ItalicCheckBoxControl.Content);
            double boldRight = Avalonia.VisualExtensions.TranslatePoint(sheet.BoldCheckBoxControl, new AvaloniaPoint(sheet.BoldCheckBoxControl.Bounds.Width, 0), sheet)!.Value.X;
            Assert.Equal(boldRight + 16, Avalonia.VisualExtensions.TranslatePoint(sheet.ItalicCheckBoxControl, new AvaloniaPoint(0, 0), sheet)!.Value.X, 0.5);
            Assert.Equal(["Left", "Center", "Right"], sheet.AlignmentSegmentsControl.Items);
            Assert.False(sheet.TextColorWellControl.AllowsNone);
            Assert.False(sheet.TextColorWellControl.AllowsAlpha);
            Assert.True(sheet.BackColorWellControl.AllowsNone);
            Assert.False(sheet.BackColorWellControl.AllowsAlpha);
            Avalonia.Controls.Control[] controls = [sheet.FontPopUpControl, sheet.FontSizeStepperControl, sheet.BoldCheckBoxControl, sheet.AlignmentSegmentsControl, sheet.TextColorWellControl, sheet.BackColorWellControl];
            for (int i = 0; i < controls.Length; i++) {
                Assert.Equal(144, Avalonia.VisualExtensions.TranslatePoint(controls[i], new AvaloniaPoint(0, 0), sheet)!.Value.X, 0.5);
                if (controls[i] != sheet.BoldCheckBoxControl)
                    Assert.Equal(rows[i].Label, Avalonia.Automation.AutomationProperties.GetName(controls[i]));
            }
        }

        [AvaloniaFact]
        public void TextProperties_FieldChanges_ApplyLive() {
            var text = new TextAnnotationElement(new Point(0, 0));
            var sheet = new TextPropertiesSheet(text);
            (AvaloniaWindow window, _, _) = SheetFixture.Present(sheet);

            sheet.TextInputControl.Text = "Changed";
            Dispatcher.UIThread.RunJobs();
            sheet.BoldCheckBoxControl.IsChecked = true;
            sheet.ItalicCheckBoxControl.IsChecked = true;
            sheet.AlignmentSegmentsControl.SelectedIndex = 2;
            sheet.FontSizeStepperControl.Value = 30;
            ClickSwatch(window, sheet.TextColorWellControl, 8);

            Assert.Equal("Changed", text.Text);
            Assert.Equal(3, text.FontStyleFlags);
            Assert.Equal(2, text.TextAlign);
            Assert.Equal(30f, text.FontSize);
            Assert.Equal(new SkiaSharp.SKColor(0xFF, 0x3B, 0x30, 255), text.TextColor);
        }

        [AvaloniaFact]
        public void TextProperties_BackgroundNone_IsAlphaZero_AndACellIsOpaque() {
            var text = new TextAnnotationElement(new Point(0, 0)) { BackColor = new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 255) };
            var sheet = new TextPropertiesSheet(text);
            (AvaloniaWindow window, _, _) = SheetFixture.Present(sheet);

            ClickSwatch(window, sheet.BackColorWellControl, 0);
            Assert.Equal(new SkiaSharp.SKColor(0x00, 0x7A, 0xFF, 0), text.BackColor);

            ClickSwatch(window, sheet.BackColorWellControl, 9);
            Assert.Equal(new SkiaSharp.SKColor(0xFF, 0x95, 0x00, 255), text.BackColor);
        }

        [AvaloniaFact]
        public async Task TextProperties_Keyboard_OpensWithTheTextSelected_TabWalksTheRows_ReturnIsANewline_CommandReturnFiresOk() {
            var text = new TextAnnotationElement(new Point(0, 0)) { Text = "Iron" };
            var sheet = new TextPropertiesSheet(text);
            (_, _, Task<bool> result) = SheetFixture.Present(sheet);
            Avalonia.Controls.TextBox field = sheet.TextInputControl;
            Assert.True(field.IsFocused);
            Assert.Equal((0, 4), (Math.Min(field.SelectionStart, field.SelectionEnd), Math.Max(field.SelectionStart, field.SelectionEnd)));
            AssertTabVisits(sheet, sheet.FontPopUpControl, sheet.FontSizeStepperControl, sheet.BoldCheckBoxControl, sheet.ItalicCheckBoxControl,
                sheet.AlignmentSegmentsControl, sheet.TextColorWellControl, sheet.BackColorWellControl, sheet.CancelButtonControl, sheet.OKButtonControl, field);
            Assert.Equal("Iron", field.Text);
            field.CaretIndex = field.Text!.Length;
            field.SelectionStart = field.SelectionEnd = field.CaretIndex;

            CanvasKeys.KeyDownOn(field, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.False(result.IsCompleted);
            Assert.Contains("\n", field.Text);

            CanvasKeys.KeyDownOn(field, Key.Enter, KeyModifiers.Meta);
            Assert.True(await result.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        [AvaloniaFact]
        public void TextProperties_Cancel_RevertsEveryFieldToTheConstructionSnapshot() {
            var text = new TextAnnotationElement(new Point(0, 0)) { Text = "Original", BackColor = new SkiaSharp.SKColor(1, 2, 3, 255) };
            var sheet = new TextPropertiesSheet(text);
            (AvaloniaWindow window, _, _) = SheetFixture.Present(sheet);
            sheet.TextInputControl.Text = "Edited";
            Dispatcher.UIThread.RunJobs();
            sheet.FontSizeStepperControl.Value = 40;
            ClickSwatch(window, sheet.BackColorWellControl, 0);
            Assert.Equal("Edited", text.Text);

            sheet.Cancel();

            Assert.Equal("Original", text.Text);
            Assert.NotEqual(40f, text.FontSize);
            Assert.Equal(new SkiaSharp.SKColor(1, 2, 3, 255), text.BackColor);
        }

        [AvaloniaFact]
        public void TextProperties_Ok_PersistsCurrentValuesToAppSettingsAndStatics() {
            var text = new TextAnnotationElement(new Point(0, 0));
            var sheet = new TextPropertiesSheet(text);
            SheetFixture.Present(sheet);
            sheet.FontSizeStepperControl.Value = 33;
            var settings = new Foreman.Mac.Services.AppSettings();
            sheet.Settings = settings;

            sheet.ApplyOk();

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
