using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Automation;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using Foreman.Models.Nodes;
using System.Drawing;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class CanvasAutomationTests {
        private static BaseNodeElement AddFixedRecipe(CanvasFixture fx, string recipeName, Point at, double rate) {
            IRecipe recipe = fx.Cache.Recipes[recipeName];
            NodeId id = fx.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, fx.Cache.DefaultQuality!), at);
            var controller = fx.Viewer.Session.Editor.RequestNodeController(id)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(rate);
            fx.Viewer.Graph.UpdateNodeValues();
            return KeyboardRig.Prime(fx.Viewer, id);
        }

        private static BaseNodeElement AddFixedSupplier(CanvasFixture fx, string item, Point at, double rate) {
            BaseNodeElement element = KeyboardRig.AddSupplier(fx, item, at);
            var controller = fx.Viewer.Session.Editor.RequestNodeController(element.ViewModel.Id)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(rate);
            return element;
        }


        [AvaloniaFact]
        public void CanvasPeer_IsGroupNamedGraphCanvas_ChildrenAreNodesThenLinks_NodeNamesFollowTheRuledPattern_IsSelectedTracks() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            BaseNodeElement cable = AddFixedRecipe(fx, "copper-cable", new Point(0, 0), 1);
            BaseNodeElement source = AddFixedSupplier(fx, "copper-plate", new Point(0, 300), 90);
            BaseNodeElement sink = KeyboardRig.AddConsumer(fx, "copper-plate", new Point(300, 300));
            fx.Viewer.Session.Editor.CreateLink(source.ViewModel.Id, sink.ViewModel.Id, new ItemQualityPair(fx.Cache.Items["copper-plate"], fx.Cache.DefaultQuality!));
            fx.Viewer.Graph.UpdateNodeValues();
            AutomationPeer canvas = ControlAutomationPeer.CreatePeerForElement(fx.Control);

            Assert.Equal(AutomationControlType.Group, canvas.GetAutomationControlType());
            Assert.Equal("Graph canvas", canvas.GetName());
            Assert.Equal("3 nodes, 1 link", canvas.GetHelpText());
            var children = canvas.GetChildren();
            Assert.Equal(4, children.Count);
            Assert.StartsWith("Copper cable, recipe, ", children[0].GetName());
            Assert.EndsWith(", overproducing", children[0].GetName());
            Assert.Equal("Copper plate, source, 90 per second, fixed", children[1].GetName());
            Assert.StartsWith("Copper plate link, Copper plate to Copper plate, 90 per second", children[3].GetName());

            ISelectionItemProvider selection = children[0].GetProvider<ISelectionItemProvider>()!;
            Assert.False(selection.IsSelected);
            fx.Viewer.SetSelection([cable]);
            Assert.True(selection.IsSelected);
            fx.Viewer.ClearSelection();
            Assert.False(selection.IsSelected);
        }

        [AvaloniaFact]
        public void NodeNames_SpellTheBuildingTheCountTheUnitAndEveryStateWord() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            BaseNodeElement cable = AddFixedRecipe(fx, "copper-cable", new Point(0, 0), 3);
            string building = ((IRecipeNodeViewModel)cable.ViewModel).SelectedAssembler.FriendlyName!;

            Assert.Contains($", {building} ×", cable.AutomationName);
            Assert.Contains(" per second, fixed", cable.AutomationName);

            var controller = fx.Viewer.Session.Editor.RequestNodeController(cable.ViewModel.Id)!;
            controller.SetKeyNode(true);
            controller.SetKeyNodeTitle("Cables");
            Assert.Contains(", fixed, key node Cables", cable.AutomationName);
        }

        private static void SetSolveState(CanvasFixture fx, BaseNodeElement element, NodeSolveState state) {
            fx.Viewer.Session.TryGetDomainNode(element.ViewModel.Id, out BaseNode? node);
            node!.SolveState = state;
        }

        [AvaloniaFact]
        public void NeverSolvedNode_ReadsNotYetSolved_AndStaleRatesAreCalledOutOfDate() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            BaseNodeElement source = KeyboardRig.AddSupplier(fx, "iron-ore", new Point(0, 0));
            SetSolveState(fx, source, NodeSolveState.NeverSolved);
            Assert.Equal("Iron ore, source, not yet solved", source.AutomationName);

            SetSolveState(fx, source, NodeSolveState.Stale);
            Assert.EndsWith(", rates out of date", source.AutomationName);
        }

        [AvaloniaFact]
        public void NodeActions_MirroredIntoHelpText_FixErrorsOnlyWithAnError() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            BaseNodeElement healthy = KeyboardRig.AddSupplier(fx, "iron-ore", new Point(0, 0));
            var ghost = new ItemPrototype(fx.Cache, "ghost-item", "Ghost item", new SubgroupPrototype(fx.Cache, "§§test:subgroup", "z"), "z", true);
            NodeId id = fx.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(ghost, fx.Cache.DefaultQuality!), new Point(300, 0));
            BaseNodeElement broken = KeyboardRig.Prime(fx.Viewer, id);

            Assert.Equal("Actions: Delete, Flip, Auto-connect, Show Inspector", healthy.AutomationHelpText);
            Assert.Equal(NodeState.Error, broken.ViewModel.State);
            Assert.Equal("Actions: Delete, Flip, Auto-connect, Show Inspector, Fix Errors", broken.AutomationHelpText);
            Assert.Contains(", error, Item \"Ghost item\" doesnt exist in preset!", broken.AutomationName);
        }

        [AvaloniaFact]
        public void CanvasPeer_HelpTextCarriesThePausedSuffix() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            KeyboardRig.AddSupplier(fx, "iron-ore", new Point(0, 0));
            AutomationPeer canvas = ControlAutomationPeer.CreatePeerForElement(fx.Control);

            fx.Viewer.IsPaused = true;

            Assert.Equal("1 node, 0 links, calculations paused", canvas.GetHelpText());
        }

        [AvaloniaFact]
        public void NodePeer_ExposesEachItemTabAsATextChild_OutputsBeforeInputs_WithTheRuledNamesAndNoKeyboardStop() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            AddFixedRecipe(fx, "copper-cable", new Point(0, 0), 1);
            AutomationPeer canvas = ControlAutomationPeer.CreatePeerForElement(fx.Control);
            AutomationPeer node = canvas.GetChildren().Single();

            var tabs = node.GetChildren();

            Assert.Equal(2, tabs.Count);
            Assert.All(tabs, tab => {
                Assert.Equal(AutomationControlType.Text, tab.GetAutomationControlType());
                Assert.False(tab.IsKeyboardFocusable());
                Assert.False(tab.HasKeyboardFocus());
                Assert.Same(node, tab.GetParent());
            });
            Assert.Equal("Copper cable, output, 2 per second, overproducing", tabs[0].GetName());
            Assert.Equal("Copper plate, input, 1 per second", tabs[1].GetName());
            Assert.Equal("Actions: Delete, Flip, Auto-connect, Show Inspector", node.GetHelpText());

            fx.Viewer.Graph.SelectedRateUnit = ProductionGraph.RateUnit.Per1Min;
            BaseNodeElement source = AddFixedSupplier(fx, "iron-plate", new Point(0, 600), 1);
            BaseNodeElement sink = KeyboardRig.AddConsumer(fx, "iron-plate", new Point(0, 900));
            fx.Viewer.Session.Editor.CreateLink(source.ViewModel.Id, sink.ViewModel.Id, new ItemQualityPair(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!));
            var controller = fx.Viewer.Session.Editor.RequestNodeController(sink.ViewModel.Id)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(5);
            fx.Viewer.Graph.UpdateNodeValues();
            foreach (BaseNodeElement element in new[] { source, sink }) {
                element.RequestStateUpdate();
                element.PrePaint();
            }

            BaseNodeElement lone = AddFixedSupplier(fx, "iron-plate", new Point(300, 600), 90);
            fx.Viewer.Graph.UpdateNodeValues();
            lone.RequestStateUpdate();
            lone.PrePaint();

            Assert.Equal("Iron plate, output, 90 per minute, overproducing", lone.SubElements.OfType<ItemTabElement>().Single().AutomationName);
            string sourceTab = source.SubElements.OfType<ItemTabElement>().Single().AutomationName;
            string sinkTab = sink.SubElements.OfType<ItemTabElement>().Single().AutomationName;
            Assert.Equal("Iron plate, output, 1 per minute", sourceTab);
            Assert.StartsWith("Iron plate, input, ", sinkTab);
            Assert.EndsWith(" per minute, short of supply", sinkTab);
            Assert.DoesNotContain("/", sourceTab + sinkTab);
        }

        [AvaloniaFact]
        public void OffscreenNode_VoCursor_PansWithTheFindRule() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            KeyboardRig.AddSupplier(fx, "iron-ore", new Point(0, 0));
            BaseNodeElement far = KeyboardRig.AddSupplier(fx, "coal", new Point(2400, 0));
            AutomationPeer canvas = ControlAutomationPeer.CreatePeerForElement(fx.Control);
            AutomationPeer peer = canvas.GetChildren().Single(c => c.GetName()!.StartsWith("Coal", System.StringComparison.Ordinal));
            Assert.True(peer.IsOffscreen());

            Motion.ReduceMotion = true;
            try {
                peer.BringIntoView();
            } finally {
                Motion.ReduceMotion = false;
            }

            Assert.False(peer.IsOffscreen());
            Assert.Null(fx.Control.Viewport.PanIntoViewTarget(CanvasTraversal.WorldBounds(far), GraphCanvasControl.FindPanMargin));
        }

        [AvaloniaFact]
        public void NodePeer_InvokeIsReturn_AndSelectSelectsOnlyThatNode() {
            using KeyboardRig rig = KeyboardRig.ThreeSuppliers();
            AutomationPeer canvas = ControlAutomationPeer.CreatePeerForElement(rig.Canvas);
            var children = canvas.GetChildren();
            BaseNodeElement? requested = null;
            rig.Canvas.InspectorRequested += (_, node) => requested = node;

            children[1].GetProvider<ISelectionItemProvider>()!.Select();
            children[2].GetProvider<IInvokeProvider>()!.Invoke();

            Assert.Same(rig.Third, requested);
            Assert.Equal([rig.Third], rig.Selected);
        }

        [AvaloniaFact]
        public void LinkPeer_IsSelectableAndNamedAsTheLink() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            BaseNodeElement source = KeyboardRig.AddSupplier(fx, "iron-plate", new Point(0, 0));
            BaseNodeElement sink = KeyboardRig.AddConsumer(fx, "iron-plate", new Point(0, 300));
            fx.Viewer.Session.Editor.CreateLink(source.ViewModel.Id, sink.ViewModel.Id, new ItemQualityPair(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!));
            AutomationPeer link = ControlAutomationPeer.CreatePeerForElement(fx.Control).GetChildren()[2];

            Assert.Equal("Iron plate link, Iron plate to Iron plate, 0 per second, Rounded", link.GetName());
            Assert.Equal("Actions: Delete, Rounded, Orthogonal, Clear Waypoints", link.GetHelpText());
            ISelectionItemProvider selection = link.GetProvider<ISelectionItemProvider>()!;
            selection.Select();
            Assert.True(selection.IsSelected);
            Assert.Single(fx.Viewer.SelectedLinks);
        }

        [AvaloniaFact]
        public void CanvasPeer_ListsTheEmptyStatePanelAndThePausedCapsuleOnlyWhileTheyAreVisible_AndNodeAndLinkPeersOpenTheElementMenu() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            AutomationPeer canvas = ControlAutomationPeer.CreatePeerForElement(fx.Control);
            AutomationPeer panel = ControlAutomationPeer.CreatePeerForElement(fx.Control.EmptyState);
            Assert.Same(panel, Assert.Single(canvas.GetChildren()));

            KeyboardRig.AddSupplier(fx, "iron-ore", new Point(0, 0));
            Assert.DoesNotContain(panel, canvas.GetChildren());

            using CanvasFixture paused = CanvasFixture.WithVanilla();
            KeyboardRig.AddSupplier(paused, "iron-ore", new Point(0, 0));
            paused.Viewer.IsPaused = true;
            var children = ControlAutomationPeer.CreatePeerForElement(paused.Control).GetChildren();
            Assert.Same(ControlAutomationPeer.CreatePeerForElement(paused.Control.PausedCapsuleButton), children[^1]);

            using CanvasFixture menus = CanvasFixture.WithVanilla();
            BaseNodeElement source = KeyboardRig.AddSupplier(menus, "iron-plate", new Point(0, 0));
            BaseNodeElement sink = KeyboardRig.AddConsumer(menus, "iron-plate", new Point(0, 300));
            menus.Viewer.Session.Editor.CreateLink(source.ViewModel.Id, sink.ViewModel.Id, new ItemQualityPair(menus.Cache.Items["iron-plate"], menus.Cache.DefaultQuality!));
            var menuChildren = ControlAutomationPeer.CreatePeerForElement(menus.Control).GetChildren();

            menuChildren[0].ShowContextMenu();
            ContextMenu nodeMenu = menus.Control.LastContextMenu!;
            Assert.Contains(nodeMenu.Items.OfType<MenuItem>(), i => Equals(i.Header, "Delete node"));
            Assert.Empty(menus.Viewer.SelectedLinks);

            menuChildren[2].ShowContextMenu();
            Assert.NotSame(nodeMenu, menus.Control.LastContextMenu);
            var linkItems = menus.Control.LastContextMenu!.Items.OfType<MenuItem>().ToList();
            Assert.DoesNotContain(linkItems, i => Equals(i.Header, "Delete node"));
            Assert.Contains(linkItems, i => Equals(i.Header, "Rounded"));
            Assert.Contains(linkItems, i => Equals(i.Header, "Orthogonal"));
            Assert.Contains(linkItems, i => Equals(i.Header, "Delete Link"));
            Assert.Same(((LinkAutomationPeer)menuChildren[2]).Link, Assert.Single(menus.Viewer.SelectedLinks));
        }

        [AvaloniaFact]
        public void NodeAccessibility_StateWord_StripsResolutionPrefix() {
            using CanvasFixture fx = CanvasFixture.WithSpaceAge21();
            NodeId id = fx.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(fx.Cache.Recipes["sulfur"], fx.Cache.Qualities["uncommon"]), new Point(0, 0));
            BaseNodeElement node = KeyboardRig.Prime(fx.Viewer, id);

            Assert.Contains("warning, Selected recipe can only be crafted at Normal quality", node.AutomationName);
            Assert.DoesNotContain(">", node.AutomationName);
        }
    }
}
