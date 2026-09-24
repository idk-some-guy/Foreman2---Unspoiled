using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Inspector {
    //Migrates every EditFlowPanelTests.cs behavior onto the docked FlowSegmentView (Task 4): the non-recipe
    //node editor now lives in the inspector sidebar instead of a floating panel. Rate value (SelectedRateUnit-
    //aware, already handled by Foreman.Core's ActualRate/DesiredRate), fixed/auto toggle, simple-passthrough
    //toggle, and key-node controls all go through FlowSegmentViewModel, which calls the same controllers
    //EditFlowPanel used to call directly.
    public class FlowSegmentTests {
        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        //---- brief step 1 ----

        [AvaloniaFact]
        public async Task FixedRate_WritesDesiredSetValueThroughController() {
            //Supplier -> consumer, matching EditFlowPanelTests' own FixedRateEdit_UpdatesNodeAndResolvesDownstream
            //fixture: a passthrough with nothing feeding it can never push anything past itself, so it can't
            //stand in for the original's downstream-resolve assertion the way it does for the rest of this file.
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(-100, 0));
            NodeId consumerId = window.GraphCanvas.Viewer.Session.Editor.CreateConsumerNode(pair, new Point(100, 0));
            window.GraphCanvas.Viewer.Session.Editor.CreateLink(supplierId, consumerId, pair);
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            window.GraphCanvas.Viewer.Session.View.TryGetNode(consumerId, out INodeViewModel? consumer);
            Dispatcher.UIThread.RunJobs();

            InspectorTestSupport.Select(window, supplier!);

            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            Segmented mode = view.FindControl<Segmented>("RateMode")!;
            AnimationClock.UntilReflowsSettled(window);
            Assert.Equal(mode.DesiredSize.Width, mode.Bounds.Width, 0.5);
            mode.SelectedIndex = 1;
            view.FindControl<Stepper>("RateValue")!.Value = 90;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(RateType.Manual, supplier!.RateType);
            Assert.Equal(90, supplier.DesiredRate, 3);
            Assert.Equal(90, consumer!.ActualSetValue, 3); //downstream resolve, matching EditFlowPanelTests' original assertion
        }

        [AvaloniaFact]
        public async Task KeyNodeToggle_And_Title_RoundTrip() {
            (MainWindow window, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(window, node);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;

            view.FindControl<CheckBox>("KeyNode")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            view.FindControl<TextBox>("KeyNodeTitle")!.Text = "Main output";
            Dispatcher.UIThread.RunJobs();

            Assert.True(node.KeyNode);
            Assert.Equal("Main output", node.KeyNodeTitle);
        }

        //EditFlowPanel threaded a RequestRedraw callback from GraphCanvasControl through every one of these
        //edits; FlowSegmentViewModel now takes the same callback as an optional constructor parameter, and
        //InspectorView wires it from MainWindow (GraphCanvas.InvalidateVisual). Overriding the callback with
        //a counter before Select forces FlowSegmentView.Bind to capture that override.
        [AvaloniaFact]
        public async Task FixedRateToggle_KeyNodeToggle_TitleEdit_AndSimplifyToggle_EachRequestCanvasRedraw() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId passthroughId = window.GraphCanvas.Viewer.Session.Editor.CreatePassthroughNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(passthroughId, out INodeViewModel? node);
            Dispatcher.UIThread.RunJobs();

            int redrawCount = 0;
            window.Inspector.RequestRedraw = () => redrawCount++;
            InspectorTestSupport.Select(window, node!);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;

            view.FindControl<Segmented>("RateMode")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.True(redrawCount > 0);

            int afterFixedRate = redrawCount;
            view.FindControl<CheckBox>("KeyNode")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(redrawCount > afterFixedRate);

            int afterKeyNode = redrawCount;
            view.FindControl<TextBox>("KeyNodeTitle")!.Text = "Main output";
            Dispatcher.UIThread.RunJobs();
            Assert.True(redrawCount > afterKeyNode);

            int afterTitle = redrawCount;
            view.FindControl<CheckBox>("Simplify")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(redrawCount > afterTitle);
        }

        [AvaloniaFact]
        public async Task Simplify_OnlyForPassthrough_WritesSetSimpleDraw() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);

            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();
            InspectorTestSupport.Select(window, supplier!);
            var supplierView = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            Assert.False(supplierView.FindControl<CheckBox>("Simplify")!.IsVisible);

            NodeId passthroughId = window.GraphCanvas.Viewer.Session.Editor.CreatePassthroughNode(pair, new Point(100, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(passthroughId, out INodeViewModel? passthrough);
            Dispatcher.UIThread.RunJobs();
            InspectorTestSupport.Select(window, passthrough!);
            var passthroughView = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            CheckBox simplify = passthroughView.FindControl<CheckBox>("Simplify")!;
            Assert.True(simplify.IsVisible);
            Assert.False(simplify.IsChecked);

            simplify.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(((IPassthroughNodeViewModel)passthrough!).SimpleDraw);
        }

        [AvaloniaFact]
        public async Task NodeValuesUpdated_RefreshesFields() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();
            InspectorTestSupport.Select(window, supplier!);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;

            if (window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(supplierId) is not BaseNodeController controller)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(37);
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(37, view.FindControl<Stepper>("RateValue")!.Value);
        }

        [AvaloniaFact]
        public async Task ClickingFlowNode_NoLongerOpensFloatingPanel() {
            (MainWindow window, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            Avalonia.Point canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(new Point(0, 0));
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(window.GraphCanvas, canvasPoint, window) ?? canvasPoint;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
            Assert.True(window.Inspector.IsVisible);
            Assert.Equal(node.Id, window.ViewModel.Inspector.Node!.Id);
            Assert.NotNull(window.Inspector.FindControl<FlowSegmentView>("FlowSegment"));
        }

        //---- migrated from EditFlowPanelTests.cs ----

        [AvaloniaFact]
        public async Task FixedOptionChecked_SwitchesNodeToManualRateType() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(RateType.Auto, supplier!.RateType);

            InspectorTestSupport.Select(window, supplier);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;

            view.FindControl<Segmented>("RateMode")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(RateType.Manual, supplier.RateType);
        }

        [AvaloniaFact]
        public async Task AutoOptionChecked_SwitchesNodeBackToAutoRateType() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();

            if (window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(supplierId) is not BaseNodeController controller)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            controller.SetRateType(RateType.Manual);

            InspectorTestSupport.Select(window, supplier!);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            Assert.Equal(1, view.FindControl<Segmented>("RateMode")!.SelectedIndex);

            view.FindControl<Segmented>("RateMode")!.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(RateType.Auto, supplier!.RateType);
        }

        [AvaloniaFact]
        public async Task RateDisplay_ConvertsPerSelectedRateUnit_PerSecondThenPerMinute() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();

            SetRateUnitViaMenu(window, "1 sec");
            if (window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(supplierId) is not BaseNodeController controller)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(2); //2 items/sec while SelectedRateUnit is Per1Sec

            InspectorTestSupport.Select(window, supplier!);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            Assert.Equal(2, view.FindControl<Stepper>("RateValue")!.Value);

            //Same underlying rate, new display unit, through the real View menu path (MenuSetRateUnit) -
            //no re-select: InspectorView.RefreshSegment forwards the change straight to the bound segment.
            SetRateUnitViaMenu(window, "1 min");
            Assert.Equal(120, view.FindControl<Stepper>("RateValue")!.Value); //2/sec * 60
        }

        //The passthrough Rate group's own Rate unit row (2026-09-17 building-picker ruling): a Kit.PopUp on
        //the same graph-level setting View Options' pop-up writes, listing all six values.
        [AvaloniaFact]
        public async Task RateUnitPopUp_ListsAllSixValues_AndWritesThroughToTheGraph() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();

            InspectorTestSupport.Select(window, supplier!);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            PopUp rateUnitPopUp = view.FindControl<PopUp>("RateUnitPopUp")!;
            Assert.Equal(MainWindow.RateUnitOptions, rateUnitPopUp.Items);
            Assert.Equal("per second", rateUnitPopUp.SelectedItem);

            rateUnitPopUp.Command!.Execute("per minute");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProductionGraph.RateUnit.Per1Min, window.GraphCanvas.Viewer.Graph.SelectedRateUnit);
            Assert.Equal("per minute", rateUnitPopUp.SelectedItem);
        }

        //Mirrors NativeMenuTests.cs's own TopMenu/SubMenu helpers (not shared - private to that file) to
        //drive MenuSetRateUnit the same way a real View > Rate Unit click does.
        private static void SetRateUnitViaMenu(MainWindow window, string header) {
            NativeMenu viewMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "View").Menu!;
            NativeMenu rateUnitMenu = viewMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Rate Unit").Menu!;
            NativeMenuItem item = rateUnitMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == header);
            item.Command!.Execute(item.CommandParameter);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public async Task FixedToggle_OnAutoNodeWithDivergedDesiredValue_AppliesDisplayedActualValueNotStaleDesired() {
            MainWindow window = await InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);

            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(-100, 0));
            NodeId consumerId = window.GraphCanvas.Viewer.Session.Editor.CreateConsumerNode(pair, new Point(100, 0));
            window.GraphCanvas.Viewer.Session.Editor.CreateLink(supplierId, consumerId, pair);
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();

            if (window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(supplierId) is not BaseNodeController supplierController)
                throw new Xunit.Sdk.XunitException("Supplier node has no controller.");
            if (window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(consumerId) is not BaseNodeController consumerController)
                throw new Xunit.Sdk.XunitException("Consumer node has no controller.");
            consumerController.SetRateType(RateType.Manual);
            consumerController.SetDesiredSetValue(7);
            supplierController.SetDesiredSetValue(999); //stale: supplier stays Auto, so solve never reads this
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            Assert.Equal(RateType.Auto, supplier!.RateType);
            Assert.Equal(7, supplier.ActualSetValue, 3); //solved to satisfy the consumer's demand
            Assert.Equal(999, supplier.DesiredSetValue, 3); //still stale, untouched by an Auto-node solve

            InspectorTestSupport.Select(window, supplier);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            Assert.Equal(7, view.FindControl<Stepper>("RateValue")!.Value); //displayed value is what the solver actually produced

            view.FindControl<Segmented>("RateMode")!.SelectedIndex = 1; //toggle only - never touch RateValue directly
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(RateType.Manual, supplier.RateType);
            Assert.Equal(7, supplier.DesiredSetValue, 3); //applies the displayed value, not the stale 999
        }

        [AvaloniaFact]
        public async Task KeyNodeTitleInput_TypedTextBeyond200Chars_IsTruncatedToMaxLength() {
            (MainWindow window, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(window, node);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;

            view.FindControl<CheckBox>("KeyNode")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            TextBox titleBox = view.FindControl<TextBox>("KeyNodeTitle")!;
            titleBox.Text = "";
            titleBox.Focus();
            Dispatcher.UIThread.RunJobs();

            var avaloniaWindow = (AvaloniaWindow)window;
            avaloniaWindow.KeyTextInput(new string('a', 250));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(200, titleBox.Text?.Length);
            Assert.Equal(200, node.KeyNodeTitle.Length);
        }

        //---- new: node types the migration bullet calls out by name ----

        [AvaloniaFact]
        public async Task SpoilAndPlantNodes_GetFlowSegment() {
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var rawFish = new ItemPrototype(cache, "raw-fish", "raw-fish", subgroup, "a") { Available = true };
            var spoiledFish = new ItemPrototype(cache, "spoiled-fish", "spoiled-fish", subgroup, "a") { Available = true };
            rawFish.SpoilResult = spoiledFish;
            store.Items[rawFish.Name] = rawFish;
            store.Items[spoiledFish.Name] = spoiledFish;

            var seed = new ItemPrototype(cache, "seed", "seed", subgroup, "a") { Available = true };
            var wheat = new ItemPrototype(cache, "wheat", "wheat", subgroup, "a") { Available = true };
            var process = new PlantProcessPrototype(cache, "plant-seed");
            process.InternalOneWayAddProduct(wheat, 1);
            process.Seed = seed;
            seed.PlantResult = process;
            store.Items[seed.Name] = seed;
            store.Items[wheat.Name] = wheat;

            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = quality;

            NodeId spoilId = window.GraphCanvas.Viewer.Session.Editor.CreateSpoilNode(new ItemQualityPair(rawFish, quality), spoiledFish, new Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(spoilId, out INodeViewModel? spoilNode);
            Dispatcher.UIThread.RunJobs();
            InspectorTestSupport.Select(window, spoilNode!);
            Assert.NotNull(window.Inspector.FindControl<FlowSegmentView>("FlowSegment"));

            NodeId plantId = window.GraphCanvas.Viewer.Session.Editor.CreatePlantNode(process, quality, new Point(100, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(plantId, out INodeViewModel? plantNode);
            Dispatcher.UIThread.RunJobs();
            InspectorTestSupport.Select(window, plantNode!);
            Assert.NotNull(window.Inspector.FindControl<FlowSegmentView>("FlowSegment"));
        }
    }
}
