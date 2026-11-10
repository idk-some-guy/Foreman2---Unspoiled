using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using System;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Foreman.Mac.UiTests.Inspector;
using Avalonia.Input;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class CanvasKeyboardTests {
        [AvaloniaFact]
        public void Tab_CyclesThreeNodesInReadingOrder_ShiftTabReverses_CtrlTabLeavesForTheInspector_SelectionUnchanged() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            rig.Canvas.Focus();

            foreach (BaseNodeElement expected in new[] { rig.First, rig.Second, rig.Third, rig.First }) {
                Assert.True(rig.Press(Key.Tab));
                Assert.Same(expected, rig.Canvas.FocusedNode);
                Assert.Equal([expected], rig.Selected);
                Assert.True(expected.ShowsKeyboardFocus);
            }
            foreach (BaseNodeElement expected in new[] { rig.Third, rig.Second, rig.First }) {
                Assert.True(rig.Press(Key.Tab, KeyModifiers.Shift));
                Assert.Same(expected, rig.Canvas.FocusedNode);
                Assert.Equal([expected], rig.Selected);
            }

            Assert.False(rig.Press(Key.Tab, KeyModifiers.Control));
            Assert.False(rig.Press(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift));
            Assert.Equal([rig.First], rig.Selected);
        }

        [AvaloniaFact]
        public void Tab_OnAnEmptyCanvas_IsNotConsumed() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            fx.Control.Focus();

            Assert.False(KeyboardRig.Press(fx.Control, Key.Tab));
            Assert.False(fx.Control.IsTabStop);
        }

        [AvaloniaFact]
        public void Tab_FromAChildOfTheCanvas_IsLeftToTheirFocusLoop() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            bool consumed = false;
            rig.Canvas.AddHandler(InputElement.KeyDownEvent, (_, e) => consumed = e.Handled, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

            rig.Canvas.EmptyState.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab });

            Assert.False(consumed);
            Assert.Empty(rig.Canvas.Viewer.SelectedNodes);
        }

        [AvaloniaFact]
        public void TabIntoTheCanvas_FocusesTheLastFocusedNode_OrTheFirst_WithoutSelecting() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();

            rig.Canvas.Focus(NavigationMethod.Tab);
            Assert.Same(rig.First, rig.Canvas.FocusedNode);
            Assert.True(rig.First.ShowsKeyboardFocus);
            Assert.Empty(rig.Canvas.Viewer.SelectedNodes);

            rig.Canvas.FocusNode(rig.Third);
            rig.Fixture.Window.Focus();
            rig.Canvas.Focus(NavigationMethod.Tab);
            Assert.Same(rig.Third, rig.Canvas.FocusedNode);
            Assert.True(rig.Third.ShowsKeyboardFocus);
        }

        [AvaloniaFact]
        public void OptArrow_SpatialMove_NoCandidateChangesNothing_ShiftOptArrowExtendsTheSelection() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            rig.Canvas.Focus();
            rig.Press(Key.Tab);

            Assert.True(rig.Press(Key.Right, KeyModifiers.Alt));
            Assert.Equal([rig.Second], rig.Selected);
            Assert.Same(rig.Second, rig.Canvas.FocusedNode);

            Assert.True(rig.Press(Key.Right, KeyModifiers.Alt));
            Assert.Equal([rig.Second], rig.Selected);
            Assert.Same(rig.Second, rig.Canvas.FocusedNode);
            Assert.Equal(new Point(200, 0), rig.Second.Location);

            rig.Press(Key.Tab);
            rig.Press(Key.Tab);
            rig.Press(Key.Up, KeyModifiers.Alt);
            Assert.Equal([rig.First], rig.Selected);
            rig.Press(Key.Down, KeyModifiers.Alt);
            Assert.Equal([rig.Third], rig.Selected);

            rig.Press(Key.Tab);
            Assert.Same(rig.First, rig.Canvas.FocusedNode);
            rig.Press(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift);
            Assert.Equal([rig.First, rig.Third], rig.Selected);
            Assert.Same(rig.Third, rig.Canvas.FocusedNode);
        }

        [AvaloniaFact]
        public void Spatial_KeepsToTheQuarterTurnAndWeighsOffAxisDistanceTwice() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            BaseNodeElement near = KeyboardRig.AddSupplier(rig.Fixture, "stone", new Point(100, 140));
            BaseNodeElement far = KeyboardRig.AddSupplier(rig.Fixture, "coal", new Point(300, 0));
            IEnumerable<BaseNodeElement> all = rig.Canvas.NodeElements;

            Assert.Same(far, CanvasTraversal.Spatial(rig.Second, Direction.Right, all));
            Assert.Same(rig.Second, CanvasTraversal.Spatial(rig.First, Direction.Right, all));
            Assert.Null(CanvasTraversal.Spatial(rig.First, Direction.Up, all));
            Assert.Same(rig.Third, CanvasTraversal.Spatial(rig.First, Direction.Down, all));
            Assert.Same(near, CanvasTraversal.Spatial(rig.First, Direction.Down, all.Where(n => !ReferenceEquals(n, rig.Third))));
        }

        [AvaloniaFact]
        public void Arrow_NudgesByTheGridUnit_FocusAndSelectionUnchanged() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            rig.Canvas.Grid.CurrentGridUnit = 32;
            rig.Canvas.Focus();
            rig.Press(Key.Tab);

            Assert.True(rig.Press(Key.Right));

            Assert.Equal(new Point(32, 0), rig.First.Location);
            Assert.Same(rig.First, rig.Canvas.FocusedNode);
            Assert.Equal([rig.First], rig.Selected);
        }

        [AvaloniaFact]
        public void OptTab_StepsOutputsThenInputs_EscapeReturnsToTheNode_DeleteRemovesTheFocusedLink() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            BaseNodeElement feeder = KeyboardRig.AddSupplier(fx, "copper-plate", new Point(0, 0));
            BaseNodeElement hub = KeyboardRig.AddRecipe(fx, "copper-cable", new Point(0, 300));
            BaseNodeElement left = KeyboardRig.AddConsumer(fx, "copper-cable", new Point(-100, 600));
            BaseNodeElement right = KeyboardRig.AddConsumer(fx, "copper-cable", new Point(100, 600));
            ItemQualityPair plate = new(fx.Cache.Items["copper-plate"], fx.Cache.DefaultQuality!);
            ItemQualityPair cable = new(fx.Cache.Items["copper-cable"], fx.Cache.DefaultQuality!);
            fx.Viewer.Session.Editor.CreateLink(feeder.ViewModel.Id, hub.ViewModel.Id, plate);
            fx.Viewer.Session.Editor.CreateLink(hub.ViewModel.Id, left.ViewModel.Id, cable);
            fx.Viewer.Session.Editor.CreateLink(hub.ViewModel.Id, right.ViewModel.Id, cable);
            fx.Control.Focus();
            KeyboardRig.Press(fx.Control, Key.Tab);
            KeyboardRig.Press(fx.Control, Key.Tab);
            Assert.Same(hub, fx.Control.FocusedNode);

            KeyboardRig.Press(fx.Control, Key.Tab, KeyModifiers.Alt);
            LinkElement toLeft = fx.Control.FocusedLink!;
            Assert.Same(left, toLeft.ConsumerElement);
            Assert.True(toLeft.IsFocused);
            Assert.Contains(toLeft, fx.Viewer.SelectedLinks);

            KeyboardRig.Press(fx.Control, Key.Tab, KeyModifiers.Alt);
            LinkElement toRight = fx.Control.FocusedLink!;
            Assert.Same(right, toRight.ConsumerElement);
            Assert.False(toLeft.IsFocused);

            KeyboardRig.Press(fx.Control, Key.Tab, KeyModifiers.Alt);
            Assert.Same(feeder, fx.Control.FocusedLink!.SupplierElement);
            Assert.Same(hub, fx.Control.FocusedLink.ConsumerElement);

            KeyboardRig.Press(fx.Control, Key.Tab, KeyModifiers.Alt | KeyModifiers.Shift);
            Assert.Same(toRight, fx.Control.FocusedLink);

            KeyboardRig.Press(fx.Control, Key.Escape);
            Assert.Null(fx.Control.FocusedLink);
            Assert.Same(hub, fx.Control.FocusedNode);
            Assert.True(hub.ShowsKeyboardFocus);
            Assert.Equal([hub], fx.Viewer.SelectedNodes);
            Assert.Empty(fx.Viewer.SelectedLinks);

            KeyboardRig.Press(fx.Control, Key.Tab, KeyModifiers.Alt);
            KeyboardRig.Press(fx.Control, Key.Tab, KeyModifiers.Alt);
            Assert.Same(toRight, fx.Control.FocusedLink);
            CanvasKeys.PressOnCanvas(fx.Control, Key.Delete);

            Assert.Equal(2, fx.Control.LinkElements.Count);
            Assert.DoesNotContain(toRight, fx.Control.LinkElements);
            Assert.Null(fx.Control.FocusedLink);
            Assert.Same(hub, fx.Control.FocusedNode);
        }

        [AvaloniaFact]
        public void PointerPressOnANode_BecomesTheKeyboardAnchor_ReturnTabAndOptArrowStartFromIt() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            BaseNodeElement? requested = null;
            rig.Canvas.InspectorRequested += (_, node) => requested = node;
            rig.Canvas.Focus();
            rig.Press(Key.Tab);
            Assert.Same(rig.First, rig.Canvas.FocusedNode);

            rig.Fixture.ClickAt(rig.Canvas.Viewport.GraphToScreen(rig.Second.Location));
            Assert.False(rig.First.ShowsKeyboardFocus);
            Assert.False(rig.Second.ShowsKeyboardFocus);
            rig.Press(Key.Return);
            Assert.Same(rig.Second, requested);
            Assert.Equal([rig.Second], rig.Selected);

            rig.Fixture.ClickAt(rig.Canvas.Viewport.GraphToScreen(rig.Second.Location));
            rig.Press(Key.Tab);
            Assert.Equal([rig.Third], rig.Selected);

            rig.Press(Key.Tab);
            rig.Fixture.ClickAt(rig.Canvas.Viewport.GraphToScreen(rig.Second.Location));
            rig.Press(Key.Left, KeyModifiers.Alt);
            Assert.Equal([rig.First], rig.Selected);
        }

        [AvaloniaFact]
        public void Escape_ClearsSelection_FocusRingHidesUntilNextKeyboardMove() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            rig.Canvas.Focus();
            rig.Press(Key.Tab);

            Assert.True(rig.Press(Key.Escape));

            Assert.Empty(rig.Canvas.Viewer.SelectedNodes);
            Assert.False(rig.First.ShowsKeyboardFocus);
            Assert.True(rig.Canvas.IsFocused);

            rig.Press(Key.Tab);
            Assert.True(rig.Second.ShowsKeyboardFocus);
        }

        [AvaloniaFact]
        public void Escape_WithNothingSelectedOrFocused_IsLeftUnhandled() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            rig.Canvas.Focus();

            Assert.False(rig.Press(Key.Escape));
        }

        private static async Task<(MainWindow Window, BaseNodeElement Node)> WindowWithOneNodeAsync() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            var pair = new ItemQualityPair(window.DataCache!.Items["iron-plate"], window.DataCache.DefaultQuality!);
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(0, 0));
            BaseNodeElement node = KeyboardRig.Prime(window.GraphCanvas.Viewer, id);
            Dispatcher.UIThread.RunJobs();
            return (window, node);
        }

        [AvaloniaFact]
        public async Task Return_OpensTheInspectorAndFocusesItsFirstTabStop_EscapeAndCtrlShiftTabReturnToTheSameNode() {
            (MainWindow window, BaseNodeElement node) = await WindowWithOneNodeAsync();
            window.GraphCanvas.Focus();
            KeyboardRig.Press(window.GraphCanvas, Key.Tab);
            window.GraphCanvas.Viewer.ClearSelection();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.ViewModel.IsInspectorVisible);

            KeyboardRig.Press(window.GraphCanvas, Key.Return);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ViewModel.IsInspectorVisible);
            Assert.Equal([node], window.GraphCanvas.Viewer.SelectedNodes);
            InputElement firstStop = window.Inspector.GetVisualDescendants().OfType<InputElement>()
                .First(c => c.Focusable && KeyboardNavigation.GetIsTabStop(c) && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
            Assert.Same(firstStop, window.FocusManager!.GetFocusedElement());
            Assert.False(window.GraphCanvas.IsFocused);

            TopLevelFocus(window).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.GraphCanvas.IsFocused);
            Assert.Same(node, window.GraphCanvas.FocusedNode);
            Assert.True(node.ShowsKeyboardFocus);

            KeyboardRig.Press(window.GraphCanvas, Key.Return);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Inspector.IsKeyboardFocusWithin);

            TopLevelFocus(window).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift });
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.GraphCanvas.IsFocused);
            Assert.Same(node, window.GraphCanvas.FocusedNode);
        }

        [AvaloniaFact]
        public async Task WindowKeyLoop_SearchFieldTabReachesTheCanvasAtItsFirstNode_CtrlTabLeavesForTheInspector_AnEmptyCanvasIsSkipped() {
            (MainWindow window, BaseNodeElement node) = await WindowWithOneNodeAsync();
            window.Toolbar.SearchField.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.GraphCanvas.IsFocused);
            Assert.Same(node, window.GraphCanvas.FocusedNode);
            Assert.True(node.ShowsKeyboardFocus);
            Assert.Empty(window.GraphCanvas.Viewer.SelectedNodes);

            KeyboardRig.Press(window.GraphCanvas, Key.Tab);
            Assert.Equal([node], window.GraphCanvas.Viewer.SelectedNodes);

            window.KeyPress(Key.Tab, RawInputModifiers.Control, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Inspector.IsKeyboardFocusWithin);
            Assert.Equal([node], window.GraphCanvas.Viewer.SelectedNodes);

            MainWindow empty = await InspectorTestSupport.OpenEmptyWindowAsync();
            empty.Toolbar.SearchField.Focus();
            Dispatcher.UIThread.RunJobs();

            empty.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(empty.GraphCanvas.IsTabStop);
            Assert.False(empty.GraphCanvas.IsFocused);
            Assert.NotNull(empty.FocusManager!.GetFocusedElement());
            Assert.NotSame(empty.Toolbar.SearchField, empty.FocusManager.GetFocusedElement());
        }

        [AvaloniaFact]
        public async Task CanvasAnnouncements_ReachTheWindowLiveRegion_AndARepeatedStringRaisesAChangeAgain() {
            (MainWindow window, _) = await WindowWithOneNodeAsync();
            TextBlock announcer = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SearchAnnouncer");
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(announcer);
            var names = new List<string?>();
            using IDisposable subscription = Avalonia.AvaloniaObjectExtensions.GetObservable(announcer, AutomationProperties.NameProperty).Subscribe(new NameObserver(names));
            window.GraphCanvas.Focus();

            KeyboardRig.Press(window.GraphCanvas, Key.Tab);
            Assert.DoesNotContain("Wrapped to first node.", names);
            KeyboardRig.Press(window.GraphCanvas, Key.Tab);
            Assert.Equal("Wrapped to first node.", peer.GetName());
            Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
            KeyboardRig.Press(window.GraphCanvas, Key.Tab);

            Assert.Equal(2, names.Count(n => n == "Wrapped to first node."));
            Assert.Contains("", names);

            KeyboardRig.Press(window.GraphCanvas, Key.Tab, KeyModifiers.Shift);
            Assert.Equal("Wrapped to last node.", peer.GetName());

            Foreman.DataCaching.DataTypes.IItem item = window.DataCache!.Items["copper-plate"];
            var candidate = new Foreman.Mac.Services.AddCandidate(Foreman.Mac.Services.AddKind.Item, item, item, null, item.FriendlyName, "item");
            window.GraphCanvas.PlaceCandidate(candidate, new AddRequest(new Point(400, 0), AddScope.Items));
            Assert.Equal($"{item.FriendlyName} added", peer.GetName());
        }

        private sealed class NameObserver(List<string?> names) : IObserver<string?> {
            public void OnNext(string? value) => names.Add(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        private static Avalonia.Input.InputElement TopLevelFocus(MainWindow window) =>
            (Avalonia.Input.InputElement)window.FocusManager!.GetFocusedElement()!;
    }
}
