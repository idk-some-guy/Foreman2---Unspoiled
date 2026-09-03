using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Serialization;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;
using InputPointer = Avalonia.Input.Pointer;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises reference §4's menu infrastructure: BaseNodeElement.MouseUpAction's base items (§4b),
    //ItemTabElement's delete-connection item (§4d), ErrorNoticeElement's autoresolve/resolution menu (§4e),
    //and the hit-test precedence that routes a right click to the right one of those three.
    public class ElementMenuTests {
        private const int Half = 200;

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }

            public ItemPrototype NewItem(string name, bool isMissing = false) {
                var item = new ItemPrototype(Cache, name, name, Subgroup, "z", isMissing);
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

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Control = control, Window = window };
        }

        //A freshly-created node's error/warning state is already baked in by the time its BaseNodeElement
        //subscribes to NodeStateChanged, so nodeStateRequiresUpdate never flips true on its own and PrePaint
        //never runs UpdateState (ErrorNotice placement, tab ordering) - same gotcha NodeRenderTests.Prime
        //works around. Toggling KeyNode true/false fires a real NodeStateChanged after the subscription
        //exists, tripping the dirty flag without changing any geometry PrePaint would otherwise recompute.
        private static void Prime(Fixture fx, INodeViewModel viewModel) {
            fx.Control.Viewer.Session.TryGetDomainNode(viewModel.Id, out BaseNode? node);
            BaseNodeController? controller = node is null ? null : fx.Control.Viewer.Graph.RequestNodeController(node);
            controller?.SetKeyNode(true);
            controller?.SetKeyNode(false);
        }

        private static SupplierNodeElement AddSupplier(Fixture fx, ItemQualityPair item, Point location) {
            fx.Control.Viewer.Graph.CreateSupplierNode(item, location);
            ISupplierNodeViewModel viewModel = fx.Control.Viewer.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Last();
            var element = (SupplierNodeElement)fx.Control.Viewer.NodeElementDictionary[viewModel.Id];
            Prime(fx, viewModel);
            element.PrePaint();
            return element;
        }

        private static ConsumerNodeElement AddConsumer(Fixture fx, ItemQualityPair item, Point location) {
            fx.Control.Viewer.Graph.CreateConsumerNode(item, location);
            IConsumerNodeViewModel viewModel = fx.Control.Viewer.Session.View.Nodes.OfType<IConsumerNodeViewModel>().Last();
            var element = (ConsumerNodeElement)fx.Control.Viewer.NodeElementDictionary[viewModel.Id];
            Prime(fx, viewModel);
            element.PrePaint();
            return element;
        }

        private static LinkId Connect(Fixture fx, BaseNodeElement supplier, BaseNodeElement consumer, ItemQualityPair item) {
            LinkId id = fx.Control.Viewer.Session.Editor.CreateLink(supplier.ViewModel.Id, consumer.ViewModel.Id, item);
            supplier.PrePaint();
            consumer.PrePaint();
            return id;
        }

        //---- base menu content (reference §4b): captions/enabled per selection ----

        [AvaloniaFact]
        public void BaseMenu_NoSelection_ListsDeleteFlipCopyOnly() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);

            var entries = node.BuildRightClickMenu();

            Assert.Equal(["Delete node", null, "Flip node", null, "Copy key node status"], entries.Select(e => e.Caption));
            Assert.All(entries.Where(e => !e.IsDivider), e => Assert.True(e.Enabled));
        }

        [AvaloniaFact]
        public void BaseMenu_NodeInMultiSelection_AddsBulkDeleteFlipAndClearSelection() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement nodeA = AddSupplier(fx, item, new Point(-300, 0));
            SupplierNodeElement nodeB = AddSupplier(fx, item, new Point(300, 0));
            fx.Control.Viewer.SetSelection([nodeA, nodeB]);

            var entries = nodeA.BuildRightClickMenu();

            Assert.Equal(
                ["Delete node", "Delete selected nodes", null, "Flip node", "Flip selected nodes", null, "Clear selection", null, "Copy key node status"],
                entries.Select(e => e.Caption));
        }

        [AvaloniaFact]
        public void BaseMenu_OtherNodesSelected_ThisNodeExcluded_OnlyClearSelectionAdded() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement selectedA = AddSupplier(fx, item, new Point(-300, 0));
            SupplierNodeElement selectedB = AddSupplier(fx, item, new Point(300, 0));
            SupplierNodeElement unselected = AddSupplier(fx, item, new Point(0, 300));
            fx.Control.Viewer.SetSelection([selectedA, selectedB]);

            var entries = unselected.BuildRightClickMenu();

            Assert.Equal(
                ["Delete node", null, "Flip node", null, "Clear selection", null, "Copy key node status"],
                entries.Select(e => e.Caption));
        }

        [AvaloniaFact]
        public void BaseMenu_ValidClipboard_NodeInScope_AddsPasteKeyNodeStatus() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            fx.Control.Viewer.Context.GetClipboardText = () => GraphSaveCodec.WriteKeyNodeClipboardToString(true, "My Key Node");

            var entries = node.BuildRightClickMenu();

            Assert.Equal("Paste key node status", entries[^1].Caption);
        }

        [AvaloniaTheory]
        [InlineData("not json at all")]
        [InlineData("")]
        public void BaseMenu_UnparsableClipboard_NoPasteItem_NoCrash(string clipboardText) {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            fx.Control.Viewer.Context.GetClipboardText = () => clipboardText;

            var entries = node.BuildRightClickMenu();

            Assert.DoesNotContain(entries, e => e.Caption == "Paste key node status");
        }

        [AvaloniaFact]
        public void BaseMenu_OtherNodesSelected_ThisNodeExcluded_NoPasteEvenWithValidClipboard() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement selected = AddSupplier(fx, item, new Point(-300, 0));
            SupplierNodeElement unselected = AddSupplier(fx, item, new Point(300, 0));
            fx.Control.Viewer.SetSelection([selected]);
            fx.Control.Viewer.Context.GetClipboardText = () => GraphSaveCodec.WriteKeyNodeClipboardToString(true, "My Key Node");

            var entries = unselected.BuildRightClickMenu();

            Assert.DoesNotContain(entries, e => e.Caption == "Paste key node status");
        }

        //---- Copy/Paste key node status semantics ----

        [AvaloniaFact]
        public void CopyKeyNodeStatus_WritesClipboardText() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            string? written = null;
            fx.Control.Viewer.Context.SetClipboardText = text => written = text;

            node.BuildRightClickMenu().Single(e => e.Caption == "Copy key node status").Invoke!.Invoke();

            Assert.Equal(GraphSaveCodec.WriteKeyNodeClipboardToString(false, "", writeIndented: false), written);
        }

        [AvaloniaFact]
        public void PasteKeyNodeStatus_AppliesKeyNodeAndTitleToTargetNode() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            fx.Control.Viewer.Context.GetClipboardText = () => GraphSaveCodec.WriteKeyNodeClipboardToString(true, "Pasted Title");

            node.BuildRightClickMenu().Single(e => e.Caption == "Paste key node status").Invoke!.Invoke();

            Assert.True(node.ViewModel.KeyNode);
            Assert.Equal("Pasted Title", node.ViewModel.KeyNodeTitle);
        }

        //---- Delete node removes it (and its links) from graph AND canvas elements ----

        [AvaloniaFact]
        public void DeleteNode_RemovesNodeAndLinksFromGraphAndCanvasElements() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement supplier = AddSupplier(fx, item, new Point(-100, 0));
            ConsumerNodeElement consumer = AddConsumer(fx, item, new Point(100, 0));
            LinkId link = Connect(fx, supplier, consumer, item);
            NodeId supplierId = supplier.ViewModel.Id;

            supplier.BuildRightClickMenu().Single(e => e.Caption == "Delete node").Invoke!.Invoke();

            Assert.False(fx.Control.Viewer.NodeElementDictionary.ContainsKey(supplierId));
            Assert.DoesNotContain(fx.Control.NodeElements, n => n.ViewModel.Id == supplierId);
            Assert.False(fx.Control.Viewer.Session.TryGetDomainLink(link, out _));
            Assert.False(fx.Control.Viewer.LinkElementDictionary.ContainsKey(link));
        }

        [AvaloniaFact]
        public void DeleteNode_RemovesItFromSelection() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            fx.Control.Viewer.SetSelection([node]);

            node.BuildRightClickMenu().Single(e => e.Caption == "Delete node").Invoke!.Invoke();

            Assert.DoesNotContain(node, fx.Control.Viewer.SelectedNodes);
        }

        //---- Flip flips ----

        [AvaloniaFact]
        public void FlipNode_TogglesNodeDirection() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            Assert.Equal(NodeDirection.Up, node.ViewModel.NodeDirection);

            node.BuildRightClickMenu().Single(e => e.Caption == "Flip node").Invoke!.Invoke();

            Assert.Equal(NodeDirection.Down, node.ViewModel.NodeDirection);
        }

        [AvaloniaFact]
        public void FlipSelectedNodes_TogglesEverySelectedNodesDirection() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement nodeA = AddSupplier(fx, item, new Point(-300, 0));
            SupplierNodeElement nodeB = AddSupplier(fx, item, new Point(300, 0));
            fx.Control.Viewer.SetSelection([nodeA, nodeB]);

            nodeA.BuildRightClickMenu().Single(e => e.Caption == "Flip selected nodes").Invoke!.Invoke();

            Assert.Equal(NodeDirection.Down, nodeA.ViewModel.NodeDirection);
            Assert.Equal(NodeDirection.Down, nodeB.ViewModel.NodeDirection);
        }

        //---- Clear selection ----

        [AvaloniaFact]
        public void ClearSelection_MenuItem_ClearsSelectionAndHighlight() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement nodeA = AddSupplier(fx, item, new Point(-300, 0));
            SupplierNodeElement nodeB = AddSupplier(fx, item, new Point(300, 0));
            fx.Control.Viewer.SetSelection([nodeA, nodeB]);

            nodeA.BuildRightClickMenu().Single(e => e.Caption == "Clear selection").Invoke!.Invoke();

            Assert.Empty(fx.Control.Viewer.SelectedNodes);
            Assert.False(nodeA.Highlighted);
            Assert.False(nodeB.Highlighted);
        }

        //---- tab delete-connection (reference §4d) ----

        [AvaloniaFact]
        public void TabMenu_WithLinks_DeleteConnectionsEnabled_RemovesTheLink() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement supplier = AddSupplier(fx, item, new Point(-100, 0));
            ConsumerNodeElement consumer = AddConsumer(fx, item, new Point(100, 0));
            LinkId link = Connect(fx, supplier, consumer, item);
            ItemTabElement tab = consumer.GetInputLineItemTab(item);

            var entries = tab.BuildRightClickMenu();

            MenuEntry deleteConnections = Assert.Single(entries);
            Assert.Equal("Delete connections", deleteConnections.Caption);
            Assert.True(deleteConnections.Enabled);

            deleteConnections.Invoke!.Invoke();

            Assert.False(fx.Control.Viewer.Session.TryGetDomainLink(link, out _));
            Assert.False(fx.Control.Viewer.LinkElementDictionary.ContainsKey(link));
        }

        [AvaloniaFact]
        public void TabMenu_NoLinks_DeleteConnectionsDisabled() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement supplier = AddSupplier(fx, item, Point.Empty);
            ItemTabElement tab = supplier.GetOutputLineItemTab(item);

            MenuEntry deleteConnections = Assert.Single(tab.BuildRightClickMenu());

            Assert.False(deleteConnections.Enabled);
        }

        //---- error notice: left-click autoresolve invokes the real fix path (reference §4e) ----

        [AvaloniaFact]
        public void ErrorNotice_LeftClickAutoresolve_InvokesRealFixPath_DeletesNode() {
            Fixture fx = NewFixture();
            ItemPrototype ghost = fx.NewItem("ghost-item", isMissing: true);
            SupplierNodeElement node = AddSupplier(fx, fx.Pair(ghost), Point.Empty);
            NodeId nodeId = node.ViewModel.Id;
            Assert.Equal(NodeState.Error, node.ViewModel.State);

            var errorPoint = new Point(node.X - (node.Width / 2), node.Y - (node.Height / 2));
            node.MouseUpLeft(errorPoint);

            Assert.False(fx.Control.Viewer.NodeElementDictionary.ContainsKey(nodeId));
            Assert.DoesNotContain(fx.Control.Viewer.Session.View.Nodes, n => n.Id == nodeId);
        }

        [AvaloniaFact]
        public void ErrorNotice_LeftClick_OnCleanNode_DoesNothing() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            NodeId nodeId = node.ViewModel.Id;

            var errorPoint = new Point(node.X - (node.Width / 2), node.Y - (node.Height / 2));
            node.MouseUpLeft(errorPoint);

            Assert.True(fx.Control.Viewer.NodeElementDictionary.ContainsKey(nodeId));
        }

        //---- hit-test priority (reference §4, upstream lines 258-269): tab beats node, error badge beats node ----

        [AvaloniaFact]
        public void RightClickOnTab_ReturnsTabMenu_NotNodeMenu() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            ItemTabElement tab = node.GetOutputLineItemTab(item);
            Point tabPoint = tab.LocalToGraph(Point.Empty);

            var entries = node.MouseUpRight(tabPoint);

            Assert.Equal(["Delete connections"], entries.Select(e => e.Caption));
        }

        [AvaloniaFact]
        public void RightClickOnErrorBadge_ReturnsResolutionMenu_NotNodeMenu() {
            Fixture fx = NewFixture();
            ItemPrototype ghost = fx.NewItem("ghost-item", isMissing: true);
            SupplierNodeElement node = AddSupplier(fx, fx.Pair(ghost), Point.Empty);
            var errorPoint = new Point(node.X - (node.Width / 2), node.Y - (node.Height / 2));

            var entries = node.MouseUpRight(errorPoint);

            Assert.Equal(["Delete node"], entries.Select(e => e.Caption));
        }

        [AvaloniaFact]
        public void RightClickOnNodeBody_ReturnsBaseNodeMenu() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);

            var entries = node.MouseUpRight(Point.Empty);

            Assert.Contains(entries, e => e.Caption == "Delete node");
        }

        //---- GraphCanvasControl wiring: a real right-click on a node opens the built menu ----

        [AvaloniaFact]
        public void RealRightClick_OnNode_BuildsAndOpensNodeMenu() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            AddSupplier(fx, item, Point.Empty);
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(screenPoint, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(screenPoint, MouseButton.Right, RawInputModifiers.None);

            Assert.NotNull(fx.Control.LastContextMenuEntries);
            Assert.Contains(fx.Control.LastContextMenuEntries!, e => e.Caption == "Delete node");
        }

        [AvaloniaFact]
        public void RealLeftClick_OnErrorBadge_AutoresolvesWithoutOpeningAMenu() {
            Fixture fx = NewFixture();
            ItemPrototype ghost = fx.NewItem("ghost-item", isMissing: true);
            SupplierNodeElement node = AddSupplier(fx, fx.Pair(ghost), Point.Empty);
            NodeId nodeId = node.ViewModel.Id;
            AvaloniaPoint badgeScreenPoint = fx.Control.Viewport.GraphToScreen(new Point(node.X - (node.Width / 2), node.Y - (node.Height / 2)));

            fx.Window.MouseDown(badgeScreenPoint, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(badgeScreenPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.False(fx.Control.Viewer.NodeElementDictionary.ContainsKey(nodeId));
            Assert.Null(fx.Control.LastContextMenuEntries);
        }

        //---- redraw after a menu action (fix round: upstream's per-handler Invalidate() calls,
        //ProductionGraphViewer.cs:488,497,819 - a menu click fires from Avalonia's own popup loop, after
        //the pointer handler that would normally invalidate has already returned) ----

        [AvaloniaFact]
        public void ElementMenus_Build_RunsAfterInvokeAfterTheClickedEntrysOwnAction() {
            bool actionRan = false;
            int afterInvokeCount = 0;
            var entries = new List<MenuEntry> {
                MenuEntry.Item("Do thing", () => actionRan = true)
            };

            ContextMenu menu = ElementMenus.Build(entries, () => {
                Assert.True(actionRan); //proves ordering: the entry's own action already ran
                afterInvokeCount++;
            });
            MenuItem item = menu.Items.OfType<MenuItem>().Single();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.True(actionRan);
            Assert.Equal(1, afterInvokeCount);
        }

        //---- checkable entries (reference §4c's paste-options checkboxes): Avalonia's MenuItem doesn't
        //auto-toggle IsChecked on a raised Click the way WinForms' CheckOnClick does, so ElementMenus.Build
        //flips MenuCheckboxState (and the built item's own IsChecked) itself - this proves that mechanism
        //end to end through a real built MenuItem, not just at the MenuEntry/MenuCheckboxState model level
        //RecipeMenu's own tests exercise it at.

        [AvaloniaFact]
        public void ElementMenus_Build_CheckableEntry_TogglesStateAndItemOnEachClick_AndSkipsAfterInvoke() {
            var state = new MenuCheckboxState(initialChecked: false);
            var entries = new List<MenuEntry> { MenuEntry.Checkable("Toggle me", state) };
            int afterInvokeCount = 0;

            ContextMenu menu = ElementMenus.Build(entries, () => afterInvokeCount++);
            MenuItem item = menu.Items.OfType<MenuItem>().Single();
            Assert.False(item.IsChecked);

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(state.Checked);
            Assert.True(item.IsChecked);

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(state.Checked);
            Assert.False(item.IsChecked);

            Assert.Equal(0, afterInvokeCount); //toggling a checkbox isn't a "did something, needs a repaint" action
        }

        [AvaloniaFact]
        public void RealRightClick_InvokingMenuItem_MutatesModelAndRequestsRedraw() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            SupplierNodeElement node = AddSupplier(fx, item, Point.Empty);
            NodeId nodeId = node.ViewModel.Id;
            AvaloniaPoint screenPoint = fx.Control.Viewport.GraphToScreen(Point.Empty);
            fx.Window.MouseDown(screenPoint, MouseButton.Right, RawInputModifiers.None);
            fx.Window.MouseUp(screenPoint, MouseButton.Right, RawInputModifiers.None);
            MenuItem deleteItem = fx.Control.LastContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Delete node"));
            int redrawsBefore = fx.Control.RedrawRequestCount;

            deleteItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.False(fx.Control.Viewer.NodeElementDictionary.ContainsKey(nodeId));
            Assert.True(fx.Control.RedrawRequestCount > redrawsBefore);
        }

        //---- right-click during an in-progress node drag opens no menu (fix round: mirrors
        //BaseNodeElement.MouseUp's wasDragged early-return, upstream lines 259-268 - reachable via a
        //chorded left-drag-then-right-release) ----

        [AvaloniaFact]
        public void RightClickRelease_DuringInProgressNodeDrag_OpensNoMenu() {
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            AddSupplier(fx, item, Point.Empty);
            AvaloniaPoint nodeScreen = fx.Control.Viewport.GraphToScreen(Point.Empty);

            fx.Window.MouseDown(nodeScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(nodeScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);
            Assert.Equal(GraphCanvasControl.DragOperation.Item, fx.Control.CurrentDragOperation);

            //A real chorded left-drag-then-right-release delivers a genuine per-button PointerReleased
            //(InitialPressMouseButton = Right) on every desktop backend, same as WinForms' separate
            //MouseUp events upstream relies on. Avalonia.Headless's single synthetic pointer doesn't
            //reproduce that: MouseDown/MouseUp for a second button while the first is already captured
            //both surface as PointerMoved instead (confirmed by tracing OnPointerMoved/OnPointerReleased
            //call order here - the same coalescing NodeDragTests.cs documents for a MouseDown chord turns
            //out to apply to the release side too), so there is no synthetic input sequence through
            //Window.MouseDown/MouseUp that reaches OnPointerReleased with InitialPressMouseButton = Right
            //while another button's drag is still in progress. Raising a hand-built PointerReleasedEventArgs
            //directly is Avalonia's own documented escape hatch for exactly this gap (its constructor is
            //marked [Unstable] with a doc comment steering everyday tests at the headless mouse helpers,
            //which is what every other test in this file uses - this is the one scenario those helpers
            //cannot reach).
            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.RightButtonReleased);
            var releaseArgs = new PointerReleasedEventArgs(fx.Control, pointer, fx.Control, nodeScreen, 0, properties, KeyModifiers.None, MouseButton.Right);

            fx.Control.RaiseEvent(releaseArgs);

            Assert.Null(fx.Control.LastContextMenu);
            Assert.Equal(GraphCanvasControl.DragOperation.Item, fx.Control.CurrentDragOperation);
        }

        [AvaloniaFact]
        public void RightClickRelease_WhenNotDragging_StillOpensTheMenu() {
            //Sanity check for the hand-built PointerReleasedEventArgs technique above: the same
            //construction, with no drag in progress, still reaches the real right-click routing and opens
            //a menu - proving the prior test's null result comes from the DragOperation.Item guard, not
            //from RaiseEvent failing to dispatch at all.
            Fixture fx = NewFixture();
            ItemQualityPair item = fx.Pair(fx.NewItem("iron-ore"));
            AddSupplier(fx, item, Point.Empty);
            AvaloniaPoint nodeScreen = fx.Control.Viewport.GraphToScreen(Point.Empty);

            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased);
            var releaseArgs = new PointerReleasedEventArgs(fx.Control, pointer, fx.Control, nodeScreen, 0, properties, KeyModifiers.None, MouseButton.Right);

            fx.Control.RaiseEvent(releaseArgs);

            Assert.NotNull(fx.Control.LastContextMenu);
            Assert.Contains(fx.Control.LastContextMenuEntries!, e => e.Caption == "Delete node");
        }
    }
}
