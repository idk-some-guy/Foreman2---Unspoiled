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
    //Exercises task 7's SmartNodeDirection auto-placement branch (reference §8), Autoconnect/Align Selected
    //toolbar wiring, the node menu's selection-scoped auto-connect pair (reference §4b, consolidated onto
    //Core's GraphAutoconnect per §11 step 11), and the Delete-key keyboard-map completion (reference §7).
    public class AutoPlacementTests {
        private const int Half = 300;

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required GraphCanvasControl Control { get; init; }
            public required AvaloniaWindow Window { get; init; }

            private readonly Dictionary<string, ItemPrototype> items = [];

            public ItemQualityPair Pair(string name) {
                if (!items.TryGetValue(name, out ItemPrototype? item)) {
                    item = new ItemPrototype(Cache, name, name, Subgroup, "z", false);
                    Store(Cache).Items[name] = item;
                    items[name] = item;
                }
                return new ItemQualityPair(item, Quality);
            }

            //ItemChooserPanel (task 3) only shows an item with a production/consumption recipe behind an
            //enabled assembler (docs/panels-reference.md §2's "visible"/"validAssembler" gates) - this
            //fixture's items had no recipes at all before this task's real chooser existed, so tests that
            //drive Add Item through the panel need at least a trivial one.
            public void WireProduction(string itemName) {
                ItemPrototype item = (ItemPrototype)Pair(itemName).Item!;
                var assembler = new AssemblerPrototype(Cache, "§§test:asm-" + itemName, "Assembler", EntityType.Assembler, EnergySource.Electric) { Available = true };
                var recipe = new RecipePrototype(Cache, "§§test:recipe-" + itemName, itemName, Subgroup, "z") { Available = true };
                recipe.InternalOneWayAddProduct(item, 1, 0);
                item.ProductionRecipesInternal.Add(recipe);
                recipe.AssemblersInternal.Add(assembler);
                assembler.RecipesInternal.Add(recipe);
                Store(Cache).Recipes[recipe.Name] = recipe;
            }

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

        //IRChooserPanel.startingGroup is process-wide static (docs/panels-reference.md §2) - reset per test so
        //the real ItemChooserPanel/RecipeChooserPanel this fixture now drives (task 3) doesn't inherit a
        //leftover selection from another test class.
        public AutoPlacementTests() {
            FieldInfo field = typeof(IRChooserPanel).GetField("startingGroup", BindingFlags.Static | BindingFlags.NonPublic)!;
            field.SetValue(null, null);
        }

        private static Fixture NewFixture() {
            var cache = new DataCache(filterRecipes: true);
            var group = new GroupPrototype(cache, "logistics", "Logistics", "a");
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z") { MyGroupInternal = group };
            group.SubgroupsInternal.Add(subgroup);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            store.Groups[group.Name] = group;
            store.Subgroups[subgroup.Name] = subgroup;

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            control.Viewer.Context.DCache = cache;
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Control = control, Window = window };
        }

        private static AvaloniaPoint TabScreen(Fixture fx, BaseNodeElement node, LinkType linkType) {
            ItemTabElement tab = node.SubElements.OfType<ItemTabElement>().First(t => t.LinkType == linkType);
            return fx.Control.Viewport.GraphToScreen(node.LocalToGraph(tab.Location));
        }

        private static void StartDragFromTab(Fixture fx, BaseNodeElement node, LinkType linkType) {
            AvaloniaPoint tabScreen = TabScreen(fx, node, linkType);
            fx.Window.MouseDown(tabScreen, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(tabScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(tabScreen + new AvaloniaPoint(40, 0), RawInputModifiers.LeftMouseButton);
        }

        //Forces UpdateVisibility/UpdateCurve to run for the in-progress ghost (reference §3's Type/LineType is
        //paint-driven, not mouse-event-driven) so ResolvedNewNodeDirection reads a fresh Type instead of the
        //element's just-constructed Simple default - mirrors LinkDragTests' CmdDrag tests forcing the same.
        private static void ForceGhostRepaint(Fixture fx) {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * Half, 2 * Half));
            fx.Control.Render(surface.Canvas);
        }

        //---- SmartNodeDirection auto-placement branch (reference §8, ResolvedNewNodeDirection) ----

        [AvaloniaFact]
        public void SmartNodeDirectionOff_NewNodeRequest_UsesGraphDefaultDirection_RegardlessOfCursor() {
            Fixture fx = NewFixture();
            fx.Control.SmartNodeDirection = false;
            fx.Control.Viewer.Graph.DefaultNodeDirection = NodeDirection.Down;
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);

            var farBelow = new AvaloniaPoint(Half, Half + 900);
            fx.Window.MouseMove(farBelow, RawInputModifiers.LeftMouseButton);
            ForceGhostRepaint(fx);
            fx.Window.MouseUp(farBelow, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(NodeDirection.Down, fx.Control.LastNewNodeLinkDragRequest!.Value.Direction);
        }

        //Origin direction Up, output tab drag, cursor placed well BELOW the tab: matches
        //DraggedLinkElement.GetEndpointDirections' UShape-triggering condition ("Up && tab.Y < cursor.Y") -
        //ResolvedNewNodeDirection flips the origin's direction for a UShape ghost.
        [AvaloniaFact]
        public void SmartNodeDirectionOn_UShapeGhost_NewNodeRequestFlipsOriginDirection() {
            Fixture fx = NewFixture();
            fx.Control.SmartNodeDirection = true;
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            Assert.Equal(NodeDirection.Up, supplier.ViewModel.NodeDirection); //ProductionGraph's own creation default
            StartDragFromTab(fx, supplier, LinkType.Output);

            AvaloniaPoint tabScreen = TabScreen(fx, supplier, LinkType.Output);
            var wellBelowTab = tabScreen + new AvaloniaPoint(0, 900);
            fx.Window.MouseMove(wellBelowTab, RawInputModifiers.LeftMouseButton);
            ForceGhostRepaint(fx);
            fx.Window.MouseUp(wellBelowTab, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(NodeDirection.Down, fx.Control.LastNewNodeLinkDragRequest!.Value.Direction);
        }

        //Same origin/tab, cursor placed well ABOVE the tab instead: the "natural" side, so the ghost resolves
        //to a Simple (same-direction) shape and the new node keeps the origin's own direction.
        [AvaloniaFact]
        public void SmartNodeDirectionOn_SimpleGhost_NewNodeRequestMatchesOriginDirection() {
            Fixture fx = NewFixture();
            fx.Control.SmartNodeDirection = true;
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            StartDragFromTab(fx, supplier, LinkType.Output);

            AvaloniaPoint tabScreen = TabScreen(fx, supplier, LinkType.Output);
            var wellAboveTab = tabScreen + new AvaloniaPoint(0, -900);
            fx.Window.MouseMove(wellAboveTab, RawInputModifiers.LeftMouseButton);
            ForceGhostRepaint(fx);
            fx.Window.MouseUp(wellAboveTab, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(NodeDirection.Up, fx.Control.LastNewNodeLinkDragRequest!.Value.Direction);
        }

        //---- disconnected creation (background menu / toolbar, reference §4a/§10): no origin -> the node
        //creation default (Graph.DefaultNodeDirection) applies with no explicit direction call needed ----

        //Ports the two-stage AddItem->node-type flow (docs/panels-reference.md §2/§8/§10, task 3): picking
        //"iron-plate" from the real ItemChooserPanel opens a RecipeChooserPanel with that item as KeyItem;
        //clicking its "Source" footer button (Task 3's real ItemChooserPanel/RecipeChooserPanel subclasses)
        //is what actually creates the disconnected Supplier node - the direction/selection assertions this
        //test cares about are unchanged by that swap.
        [AvaloniaFact]
        public void AddItemAsync_ChoosesItemThenSource_CreatesSupplierNodeSelectedAtGraphPoint() {
            Fixture fx = NewFixture();
            fx.Control.Viewer.Graph.DefaultNodeDirection = NodeDirection.Down;
            fx.WireProduction("iron-plate");
            ItemQualityPair ironPlate = fx.Pair("iron-plate");
            var graphPoint = new Point(120, -40);

            fx.Control.AddItemAsync(graphPoint);
            var itemPanel = Assert.IsType<ItemChooserPanel>(fx.Control.FloatingPanelHost.Content);
            IconButton itemCell = itemPanel.GetVisualDescendants().OfType<IconButton>().First(b => Equals(b.DataObject, ironPlate.Item));
            Click(itemCell);
            var recipePanel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Button sourceButton = recipePanel.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == "Source");
            Click(sourceButton);

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsType<SupplierNodeElement>(created);
            Assert.Equal(graphPoint, created.ViewModel.Location);
            Assert.Equal(NodeDirection.Down, created.ViewModel.NodeDirection);
            Assert.Same(created, Assert.Single(fx.Control.Viewer.SelectedNodes));
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        private static void Click(Control control) {
            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            var args = new PointerReleasedEventArgs(control, pointer, control, default, 0, properties, KeyModifiers.None, MouseButton.Left);
            control.RaiseEvent(args);
        }

        [AvaloniaFact]
        public void AddItemAsync_NoDataCache_NoOp() {
            Fixture fx = NewFixture();
            fx.Control.Viewer.Context.DCache = null;

            fx.Control.AddItemAsync(new Point(0, 0));

            Assert.Empty(fx.Control.Viewer.NodeElements);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void AddItemAsync_ChooserCancelled_NoNodeCreated() {
            Fixture fx = NewFixture();
            fx.Pair("iron-plate");

            fx.Control.AddItemAsync(new Point(0, 0));
            Assert.True(fx.Control.FloatingPanelHost.IsOpen);
            fx.Control.FloatingPanelHost.Close();

            Assert.Empty(fx.Control.Viewer.NodeElements);
        }

        //---- Align Selected (reference §8/§11 step 11) ----

        [AvaloniaFact]
        public void AlignSelected_SnapsEverySelectedNode_LeavesUnselectedNodesInPlace() {
            Fixture fx = NewFixture();
            fx.Control.Grid.CurrentGridUnit = 32;
            BaseNodeElement offGridSelected = fx.AddSupplier("iron-ore", new Point(5, 41));
            BaseNodeElement offGridUnselected = fx.AddConsumer("iron-ore", new Point(7, 9));
            fx.Control.Viewer.SetSelection([offGridSelected]);

            fx.Control.Viewer.AlignSelected();

            Assert.Equal(fx.Control.Grid.AlignToGrid(new Point(5, 41)), offGridSelected.ViewModel.Location);
            Assert.Equal(new Point(7, 9), offGridUnselected.ViewModel.Location);
        }

        //---- Autoconnect toolbar button: whole graph, not scoped to selection (reference §8) ----

        [AvaloniaFact]
        public void AutoconnectDisconnectedInputs_ConnectsWholeGraph_EvenWithNothingSelected() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            BaseNodeElement consumer = fx.AddConsumer("iron-ore", new Point(100, 0));

            int created = fx.Control.Viewer.AutoconnectDisconnectedInputs();

            Assert.Equal(1, created);
            Assert.Single(fx.Control.Viewer.Session.View.Links);
            _ = supplier;
            _ = consumer;
        }

        //---- node menu's selection-scoped auto-connect pair (reference §4b, consolidated via GraphAutoconnect) ----

        [AvaloniaFact]
        public void RightClickMenu_SelectionHasOpenInputMatchedByAvailableOutput_ShowsAutoconnectInputsItem() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            BaseNodeElement consumer = fx.AddConsumer("iron-ore", new Point(100, 0));
            //soaks up the supplier's OWN output link outside the selection, so matchedOI (which reads ALL
            //outputs across the selection, not just open ones) stays false and only matchedIO is exercised.
            BaseNodeElement otherConsumer = fx.AddConsumer("iron-ore", new Point(0, 300));
            fx.Control.Viewer.Session.Editor.CreateLink(supplier.ViewModel.Id, otherConsumer.ViewModel.Id, fx.Pair("iron-ore"));
            fx.Control.Viewer.SetSelection([supplier, consumer]);

            List<MenuEntry> entries = consumer.BuildRightClickMenu();

            Assert.Contains(entries, e => e.Caption == "Auto-connect disconnected inputs");
            Assert.DoesNotContain(entries, e => e.Caption == "Auto-connect disconnected outputs");
        }

        [AvaloniaFact]
        public void RightClickMenu_SelectionHasNoOpenMatch_NoAutoconnectItems() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            fx.Control.Viewer.SetSelection([supplier]);

            List<MenuEntry> entries = supplier.BuildRightClickMenu();

            Assert.DoesNotContain(entries, e => e.Caption == "Auto-connect disconnected inputs");
            Assert.DoesNotContain(entries, e => e.Caption == "Auto-connect disconnected outputs");
        }

        //Matches upstream's own quirk (reference §4b): the item appears based on the CURRENT selection as a
        //whole, not on whether the right-clicked node is part of it.
        [AvaloniaFact]
        public void RightClickMenu_ClickedNodeOutsideSelection_StillShowsAutoconnectItemsFromSelection() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            BaseNodeElement consumer = fx.AddConsumer("iron-ore", new Point(100, 0));
            BaseNodeElement bystander = fx.AddSupplier("copper-ore", new Point(200, 0));
            fx.Control.Viewer.SetSelection([supplier, consumer]);

            List<MenuEntry> entries = bystander.BuildRightClickMenu();

            Assert.Contains(entries, e => e.Caption == "Auto-connect disconnected inputs");
        }

        //The menu's own matchedIO gate only checks item-existence (any output vs. any open input), same as
        //upstream - it has no knowledge of GraphAutoconnect's own "a node with its own open input isn't
        //offered as a supplier" exclusion, so the item still shows here even though invoking it creates
        //nothing (the passthrough's plate output is the only candidate supplier, and its own open input
        //disqualifies it - reference §4b/§11 step 11, pinned against Core's GraphAutoconnectTests too).
        [AvaloniaFact]
        public void RightClickMenu_InvokeAutoconnectInputs_ExcludesANodeWithItsOwnOpenInputFromSupplying() {
            Fixture fx = NewFixture();
            BaseNodeElement passthrough = fx.AddPassthrough("plate", new Point(0, 0)); //has its own open input
            BaseNodeElement consumer = fx.AddConsumer("plate", new Point(100, 0));
            fx.Control.Viewer.SetSelection([passthrough, consumer]);

            List<MenuEntry> entries = consumer.BuildRightClickMenu();
            entries.Single(e => e.Caption == "Auto-connect disconnected inputs").Invoke!.Invoke();

            Assert.Empty(fx.Control.Viewer.Session.View.Links);
        }

        [AvaloniaFact]
        public void RightClickMenu_InvokeAutoconnectOutputs_LinksOpenOutputToScopedConsumer() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("plate", new Point(0, 0));
            BaseNodeElement consumer = fx.AddConsumer("plate", new Point(100, 0));
            fx.Control.Viewer.SetSelection([supplier, consumer]);

            List<MenuEntry> entries = supplier.BuildRightClickMenu();
            entries.Single(e => e.Caption == "Auto-connect disconnected outputs").Invoke!.Invoke();

            Assert.Single(fx.Control.Viewer.Session.View.Links);
        }

        //---- Delete key (reference §7's keyboard-map completion) ----

        [AvaloniaFact]
        public void DeleteKey_NoAnnotationsSelected_DeletesSelectedNodes() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            fx.Control.Viewer.SetSelection([supplier]);
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            fx.Window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None);

            Assert.Empty(fx.Control.Viewer.NodeElements);
        }

        [AvaloniaFact]
        public void DeleteKey_AnnotationSelected_DeletesTheAnnotation_LeavesUnselectedNodesAlone() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            var annotation = new ShapeAnnotationElement(new Point(50, 50));
            fx.Control.Viewer.AddAnnotationElement(annotation);
            fx.Control.Viewer.SelectedAnnotations.Add(annotation);
            annotation.IsSelected = true;
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            fx.Window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None);

            Assert.Empty(fx.Control.Viewer.Annotations);
            Assert.Single(fx.Control.Viewer.NodeElements);
            _ = supplier;
        }

        //Mixed selection with an annotation selected: upstream's Delete case prefers TryDeleteSelection
        //(nodes + annotations together) over the node-only TryDeleteSelectedNodes the instant any annotation
        //is selected, so a selected node still gets deleted alongside the annotation.
        [AvaloniaFact]
        public void DeleteKey_AnnotationAndNodeBothSelected_DeletesBoth() {
            Fixture fx = NewFixture();
            BaseNodeElement supplier = fx.AddSupplier("iron-ore", new Point(0, 0));
            fx.Control.Viewer.SetSelection([supplier]);
            var annotation = new ShapeAnnotationElement(new Point(50, 50));
            fx.Control.Viewer.AddAnnotationElement(annotation);
            fx.Control.Viewer.SelectedAnnotations.Add(annotation);
            annotation.IsSelected = true;
            fx.Control.Focus();

            fx.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            fx.Window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None);

            Assert.Empty(fx.Control.Viewer.NodeElements);
            Assert.Empty(fx.Control.Viewer.Annotations);
        }
    }
}
