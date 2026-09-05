using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Inspector {
    //Task 9 gate-fix fidelity pass: structural assertions from the brief's own checklist that don't belong
    //to any one segment's existing test file - only the body scrolls, group titles read uppercase, only
    //Assembler/Modules/Beacon collapse, and toggle rows carry no "On"/"Off" text.
    public class InspectorFidelityTests {
        [AvaloniaFact]
        public async Task IdentityHeader_IsNotInsideTheScrollViewer() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            ScrollViewer scroller = window.Inspector.FindControl<ScrollViewer>("InspectorScroller")!;
            Border identityRow = window.Inspector.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("foreman-identity-row"));

            Assert.DoesNotContain(identityRow, scroller.GetVisualDescendants());
            ContentControl segmentHost = window.Inspector.FindControl<ContentControl>("SegmentHost")!;
            Assert.Contains(segmentHost, scroller.GetVisualDescendants());
        }

        [AvaloniaFact]
        public async Task RecipeGroups_TitlesRenderUppercase() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            InspectorGroup rateGroup = window.Inspector.GetVisualDescendants().OfType<InspectorGroup>().Single(g => g.Title == "Rate");
            TextBlock headerTitle = rateGroup.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("foreman-section-header"));

            Assert.Equal("RATE", headerTitle.Text);
        }

        [AvaloniaFact]
        public async Task RateAndFuelGroups_AreNonCollapsible() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            var groups = window.Inspector.GetVisualDescendants().OfType<InspectorGroup>().ToList();
            InspectorGroup rateGroup = groups.Single(g => g.Title == "Rate");

            Assert.False(rateGroup.IsCollapsible);
            Assert.True(rateGroup.IsExpanded);

            Button header = rateGroup.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GroupHeader");
            Assert.False(header.IsHitTestVisible);
        }

        [AvaloniaFact]
        public async Task AssemblerModulesAndBeaconGroups_AreCollapsible() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            var groups = window.Inspector.GetVisualDescendants().OfType<InspectorGroup>().ToList();
            Assert.True(groups.Single(g => g.Title == "Assembler").IsCollapsible);
            Assert.True(groups.Single(g => g.Title!.StartsWith("Modules", System.StringComparison.Ordinal)).IsCollapsible);
            Assert.True(groups.Single(g => g.Title == "Beacon").IsCollapsible);
        }

        [AvaloniaFact]
        public async Task ToggleRows_CarryNoOnOffText() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            ToggleSwitch lowPriority = window.Inspector.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "LowPriority");
            Assert.Null(lowPriority.OnContent);
            Assert.Null(lowPriority.OffContent);
            Assert.DoesNotContain(lowPriority.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "On" or "Off");
        }

        [AvaloniaFact]
        public async Task ToggleSwitches_RenderAsAVisiblePillToTheRightOfTheirLabel() {
            (MainWindow flowWindow, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(flowWindow, node);
            var flowView = flowWindow.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            AssertRendersAsPill(flowWindow, flowView, "KeyNode");
            AssertRendersAsPill(flowWindow, flowView, "Simplify");

            MainWindow recipeWindow = await InspectorTestSupport.OpenWindowWithGraphAsync();
            recipeWindow.GraphCanvas.Viewer.SetSelection([recipeWindow.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            var recipeView = recipeWindow.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            AssertRendersAsPill(recipeWindow, recipeView, "LowPriority");
            AssertRendersAsPill(recipeWindow, recipeView, "KeyNode");
        }

        //A pill switch is 30x18 (foreman-mini-switch's template Border), sits right of its row label, is
        //actually clickable, and its track paints at full opacity in both the off and on state - the
        //brief's own reproduction for the toggle rows that render as plain text: FluentTheme's built-in
        //ToggleSwitch ControlTheme still applies its own ^:unchecked/^:checked opacity toggling to any
        //template child named "SwitchKnobBounds", which is the exact part name our template used to reuse
        //for the whole pill (rather than just the on-state fill FluentTheme built it for), so the pill
        //went to Opacity 0 whenever the switch was unchecked - its normal resting state.
        private static void AssertRendersAsPill(AvaloniaWindow window, Control segment, string switchName) {
            ToggleSwitch toggle = segment.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == switchName);
            Grid row = (Grid)toggle.Parent!;
            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("foreman-row-label"));
            Border track = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Bounds.Width > 20);

            Assert.True(toggle.IsVisible);
            Assert.Equal(30, toggle.Bounds.Width, 1);
            Assert.Equal(18, toggle.Bounds.Height, 1);
            Assert.True(toggle.Bounds.X >= label.Bounds.Right, $"{switchName} switch (X={toggle.Bounds.X}) should sit right of its label (Right={label.Bounds.Right}).");
            Assert.Equal(1, track.Opacity);

            Point topLeft = toggle.TranslatePoint(new Point(0, 0), window)!.Value;
            Point center = topLeft + new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2);
            bool wasChecked = toggle.IsChecked == true;
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(!wasChecked, toggle.IsChecked);
            Assert.Equal(1, track.Opacity);
        }

        [AvaloniaFact]
        public async Task ToggleRows_ConsecutivePillsAreSpacedApart_AndCenteredOnTheirLabels() {
            (MainWindow flowWindow, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(flowWindow, node);
            var flowView = flowWindow.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            AssertToggleRowSpacing(flowWindow, flowView, "KeyNode", "Simplify");

            MainWindow recipeWindow = await InspectorTestSupport.OpenWindowWithGraphAsync();
            recipeWindow.GraphCanvas.Viewer.SetSelection([recipeWindow.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            var recipeView = recipeWindow.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            AssertToggleRowSpacing(recipeWindow, recipeView, "LowPriority", "KeyNode");
        }

        private static void AssertToggleRowSpacing(AvaloniaWindow window, Control segment, string firstName, string secondName) {
            ToggleSwitch first = segment.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == firstName);
            ToggleSwitch second = segment.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == secondName);

            double firstTop = first.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            double secondTop = second.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            Assert.True(Math.Abs(secondTop - firstTop - 28) <= 1,
                $"{firstName}->{secondName} pill top-edge gap was {secondTop - firstTop}, expected 28 +/- 1.");

            AssertPillCenteredOnLabel(first);
            AssertPillCenteredOnLabel(second);
        }

        private static void AssertPillCenteredOnLabel(ToggleSwitch toggle) {
            Grid row = (Grid)toggle.Parent!;
            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("foreman-row-label"));
            double pillCenter = toggle.Bounds.Y + toggle.Bounds.Height / 2;
            double labelCenter = label.Bounds.Y + label.Bounds.Height / 2;
            Assert.True(Math.Abs(pillCenter - labelCenter) <= 1,
                $"{toggle.Name} pill centre ({pillCenter}) is not within 1px of its label centre ({labelCenter}).");
        }

        [AvaloniaFact]
        public async Task BeaconGroup_StepperRows_AreSpacedApart() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            var view = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
            RecipeSegmentViewModel vm = view.ViewModel!;
            Assert.NotEmpty(vm.BeaconOptions);

            vm.PickBeacon(vm.BeaconOptions[0]);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            CompactStepper beaconCount = view.FindControl<CompactStepper>("BeaconCountValue")!;
            CompactStepper perAssembler = view.FindControl<CompactStepper>("BeaconsPerAssemblerValue")!;
            CompactStepper additional = view.FindControl<CompactStepper>("BeaconsTotalValue")!;

            double beaconCountTop = beaconCount.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            double perAssemblerTop = perAssembler.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            double additionalTop = additional.TranslatePoint(new Point(0, 0), window)!.Value.Y;

            Assert.True(Math.Abs(perAssemblerTop - beaconCountTop - 30) <= 1, $"# Beacons -> / Assembler gap was {perAssemblerTop - beaconCountTop}, expected 30 +/- 1.");
            Assert.True(Math.Abs(additionalTop - perAssemblerTop - 30) <= 1, $"/ Assembler -> Additional gap was {additionalTop - perAssemblerTop}, expected 30 +/- 1.");
        }

        [AvaloniaFact]
        public async Task FlowRateRow_IsVisibleOnlyWhenFixed() {
            (MainWindow window, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(window, node);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            CompactStepper rateValue = view.FindControl<CompactStepper>("RateValue")!;
            Grid rateRow = (Grid)rateValue.Parent!;

            Assert.False(rateRow.IsVisible);

            view.ViewModel!.IsFixedRate = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(rateRow.IsVisible);

            view.ViewModel!.IsFixedRate = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(rateRow.IsVisible);
        }

        [AvaloniaFact]
        public void GeneratorCaption_Wraps() {
            RecipeSegmentView view = BuildGeneratorSegmentView();
            TextBlock caption = view.FindControl<TextBlock>("AssemblerStatsText")!;

            Assert.Equal(TextWrapping.Wrap, caption.TextWrapping);
            Assert.Contains("Effectivity", caption.Text);
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static RecipeSegmentView BuildGeneratorSegmentView() {
            var cache = new DataCache(filterRecipes: true);
            var normal = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[normal.Name] = normal;
            store.DefaultQuality = normal;

            var group = new GroupPrototype(cache, "production", "Production", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "production-sub", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var steam = new FluidPrototype(cache, "steam", "Steam", subgroup, "a") { Available = true, IsTemperatureDependent = true, DefaultTemperature = 15 };
            store.Items[steam.Name] = steam;

            var generatorRecipe = new RecipePrototype(cache, "generate-power", "Generate Power", subgroup, "a") { Available = true };
            generatorRecipe.InternalOneWayAddIngredient(steam, 1, 100, 500);
            steam.ConsumptionRecipesInternal.Add(generatorRecipe);
            store.Recipes[generatorRecipe.Name] = generatorRecipe;

            var generator = new AssemblerPrototype(cache, "generator", "Generator", EntityType.Generator, EnergySource.Electric) { Available = true, Enabled = true };
            generator.OperationTemperature = 500;
            generator.EnergyProductionInternal[normal] = 1_000_000.0;
            generatorRecipe.AssemblersInternal.Add(generator);
            generator.RecipesInternal.Add(generatorRecipe);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            var canvasHost = new AvaloniaWindow { Content = control, Width = 900, Height = 900 };
            canvasHost.Show();

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(generatorRecipe, normal), new System.Drawing.Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;
            if (control.Viewer.Session.Editor.RequestNodeController(id) is not RecipeNodeController controller)
                throw new Xunit.Sdk.XunitException("Recipe node has no controller.");
            controller.SetAssembler(new AssemblerQualityPair(generator, normal));
            control.Viewer.Graph.UpdateNodeValues();

            var view = new RecipeSegmentView();
            var host = new AvaloniaWindow { Content = view, Width = 220, Height = 600 };
            host.Show();
            view.Bind(node, control.Viewer, cache);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            return view;
        }
    }
}
