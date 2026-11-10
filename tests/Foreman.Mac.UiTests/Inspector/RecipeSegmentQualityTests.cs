using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Application = Avalonia.Application;
using Color = Avalonia.Media.Color;
using CornerRadius = Avalonia.CornerRadius;
using Thickness = Avalonia.Thickness;

namespace Foreman.Mac.UiTests.Inspector {
    public class RecipeSegmentQualityTests {
        private const string PresetName = "Factorio 2.0 Space Age";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                sharedCache ??= await LoadCacheAsync().ConfigureAwait(false);
                return sharedCache;
            } finally {
                CacheGate.Release();
            }
        }

        private static async Task<DataCache> LoadCacheAsync() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(PresetName, true, true), new System.Progress<KeyValuePair<int, string>>()).ConfigureAwait(false);
            return cache;
        }

        private static (GraphCanvasControl Control, IQuality Normal, IQuality Legendary, IRecipeNodeViewModel Node) NewFixture(DataCache cache) {
            IQuality normal = cache.DefaultQuality!;
            IQuality legendary = cache.Qualities["legendary"];
            IRecipe recipe = cache.Recipes["wooden-chest"];
            IAssembler assembler = cache.Assemblers["assembling-machine-2"];

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            control.Viewer.Graph.MaxQualitySteps = 2;

            NodeId id = control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, normal), new Point(0, 0));
            Assert.True(control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? nodeViewModel));
            var node = (IRecipeNodeViewModel)nodeViewModel!;

            if (control.Viewer.Session.Editor.RequestNodeController(id) is not RecipeNodeController controller)
                throw new Xunit.Sdk.XunitException("Recipe node has no controller.");
            controller.SetAssembler(new AssemblerQualityPair(assembler, normal));
            controller.RemoveAssemblerModules();
            control.Viewer.Graph.UpdateNodeValues();

            return (control, normal, legendary, node);
        }

        [AvaloniaFact]
        public async Task PickQuality_IconsResolve_ForModuleOptions() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache);

            PickGridItem qualityItem = vm.QualityOptions.Single(i => Equals(i.Key, legendary));
            vm.PickQuality(qualityItem);

            Assert.Equal(legendary, node.SelectedAssembler.Quality);
            Assert.Equal(9, vm.ModuleOptions.Count);
            Assert.All(vm.ModuleOptions, option => Assert.NotNull(option.Icon));
        }

        [AvaloniaFact]
        public async Task PickQuality_ThenAddModule_EquipsAtThePickedQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache);

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));
            vm.AddModule(vm.ModuleOptions[0]);

            Assert.Single(node.AssemblerModules);
            Assert.Equal(legendary, node.AssemblerModules[0].Quality);
        }

        [AvaloniaFact]
        public async Task PickQuality_ThenPickBeacon_UsesThePickedQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache);

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));
            Assert.NotEmpty(vm.BeaconOptions);
            vm.PickBeacon(vm.BeaconOptions[0]);

            Assert.True(node.SelectedBeacon);
            Assert.Equal(legendary, node.SelectedBeacon.Quality);
        }

        [AvaloniaFact]
        public async Task PickQuality_ThenAddBeaconModule_UsesThePickedQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache);

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));
            vm.PickBeacon(vm.BeaconOptions[0]);
            Assert.NotEmpty(vm.BeaconModuleOptions);
            vm.AddBeaconModule(vm.BeaconModuleOptions[0]);

            Assert.Single(node.BeaconModules);
            Assert.Equal(legendary, node.BeaconModules[0].Quality);
        }

        [AvaloniaFact]
        public async Task PickQuality_SetsGraphDefaultAssemblerQuality() {
            DataCache cache = await GetCacheAsync();
            (GraphCanvasControl control, _, IQuality legendary, IRecipeNodeViewModel node) = NewFixture(cache);
            var vm = new RecipeSegmentViewModel(node, control.Viewer, cache);

            vm.PickQuality(vm.QualityOptions.Single(i => Equals(i.Key, legendary)));

            Assert.Equal(legendary, control.Viewer.Graph.DefaultAssemblerQuality);
        }

        private const string LockSentence = "This recipe can only be crafted at Normal quality.";

        private static (ShellFixture Shell, RecipeSegmentView View, IRecipeNodeViewModel Node) OpenInspectorOn(string recipe, string quality) {
            ShellFixture shell = ShellFixture.OpenWithSpaceAge21();
            DataCache cache = shell.Window.DataCache!;
            NodeId id = shell.Window.GraphCanvas.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(cache.Recipes[recipe], cache.Qualities[quality]), new Point(0, 0));
            shell.Graph.UpdateNodeValues();
            Assert.True(shell.Window.GraphCanvas.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node));
            InspectorTestSupport.Select(shell.Window, node!);
            Dispatcher.UIThread.RunJobs();
            return (shell, shell.Window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!, (IRecipeNodeViewModel)node!);
        }

        private static FormRow QualityRow(RecipeSegmentView view) => view.FindControl<FormRow>("RecipeQualityRow")!;

        private static ValueText QualityValue(RecipeSegmentView view) => view.FindControl<ValueText>("RecipeQualityValue")!;

        private static Border QualityWarning(RecipeSegmentView view) => view.FindControl<Border>("RecipeQualityWarning")!;

        private static Color Token(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value));
            return Assert.IsType<Color>(value);
        }

        [AvaloniaFact]
        public void QualityRow_SettableRecipe_ShowsTierAndCreationNote() {
            (ShellFixture shell, RecipeSegmentView view, _) = OpenInspectorOn("iron-gear-wheel", "uncommon");
            using (shell) {
                FormRow row = QualityRow(view);

                Assert.True(row.IsVisible);
                Assert.Equal("Quality", row.Label);
                Assert.Equal("Uncommon", QualityValue(view).Text);
                Assert.Equal("Set when the node is added.", row.Note);
                Assert.False(QualityWarning(view).IsVisible);
                Assert.Empty(row.GetVisualDescendants().OfType<PopUp>());
            }
        }

        [AvaloniaFact]
        public void QualityRow_UnsettableAtLowest_ShowsLockNote() {
            (ShellFixture shell, RecipeSegmentView view, _) = OpenInspectorOn("sulfur", "normal");
            using (shell) {
                FormRow row = QualityRow(view);
                TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");

                Assert.True(row.IsVisible);
                Assert.Equal("Normal", QualityValue(view).Text);
                Assert.Equal(LockSentence, row.Note);
                Assert.Equal(Token("Foreman.Color.TextTertiary"), Assert.IsAssignableFrom<ISolidColorBrush>(note.Foreground).Color);
                Assert.False(QualityWarning(view).IsVisible);
            }
        }

        [AvaloniaFact]
        public void QualityRow_UnsettableAboveLowest_ShowsWarningBlock() {
            (ShellFixture shell, RecipeSegmentView view, _) = OpenInspectorOn("sulfur", "uncommon");
            using (shell) {
                AnimationClock.UntilReflowsSettled(shell.Window);
                Border block = QualityWarning(view);

                Assert.True(block.IsVisible);
                Assert.Equal("Uncommon", QualityValue(view).Text);
                Assert.True(string.IsNullOrEmpty(QualityRow(view).Note));
                Assert.Equal(Token("Foreman.Color.WarningSoft"), Assert.IsAssignableFrom<ISolidColorBrush>(block.Background).Color);
                Assert.Equal(new CornerRadius(6), block.CornerRadius);
                Assert.Equal(new Thickness(6, 8), block.Padding);
                Assert.Equal(new Thickness(12, 0, 12, 0), block.Margin);
                Path glyph = block.GetVisualDescendants().OfType<Path>().Single();
                Assert.Equal(Token("Foreman.Color.Warning"), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Stroke).Color);
                Assert.Equal(LockSentence, block.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("callout")).Text);
                Button button = block.GetVisualDescendants().OfType<Button>().Single();
                Assert.Equal("Switch to Normal Quality", button.Content);
                Assert.Equal(22, button.Bounds.Height);
                Assert.Equal(((Control)block.Parent!).Bounds.Width - 24, block.Bounds.Width);
            }
        }

        [AvaloniaFact]
        public void QualityRow_SingleEnabledQuality_HiddenUnlessWarned() {
            ShellFixture shell = ShellFixture.OpenWithSpaceAge21();
            using (shell) {
                DataCache cache = shell.Window.DataCache!;
                IQuality[] others = [.. cache.Qualities.Values.Where(q => q != cache.DefaultQuality)];
                try {
                    foreach (IQuality quality in others)
                        ((QualityPrototype)quality).Enabled = false;
                    var graphCanvas = shell.Window.GraphCanvas;
                    NodeId settable = graphCanvas.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(cache.Recipes["iron-gear-wheel"], cache.DefaultQuality!), new Point(0, 0));
                    shell.Graph.UpdateNodeValues();
                    Assert.True(graphCanvas.Viewer.Session.View.TryGetNode(settable, out INodeViewModel? settableNode));
                    InspectorTestSupport.Select(shell.Window, settableNode!);
                    Dispatcher.UIThread.RunJobs();
                    RecipeSegmentView view = shell.Window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;
                    Assert.False(QualityRow(view).IsVisible);

                    NodeId warned = graphCanvas.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(cache.Recipes["sulfur"], cache.Qualities["uncommon"]), new Point(200, 0));
                    shell.Graph.UpdateNodeValues();
                    Assert.True(graphCanvas.Viewer.Session.View.TryGetNode(warned, out INodeViewModel? warnedNode));
                    InspectorTestSupport.Select(shell.Window, warnedNode!);
                    Dispatcher.UIThread.RunJobs();
                    AnimationClock.UntilReflowsSettled(shell.Window);
                    Assert.True(QualityRow(view).IsVisible);
                    Assert.True(QualityWarning(view).IsVisible);
                } finally {
                    foreach (IQuality quality in others)
                        ((QualityPrototype)quality).Enabled = true;
                }
            }
        }

        [AvaloniaFact]
        public void SwitchButton_ReplacesNode_SelectsCopyAndFocusesFirstControl() {
            (ShellFixture shell, RecipeSegmentView view, IRecipeNodeViewModel node) = OpenInspectorOn("sulfur", "uncommon");
            using (shell) {
                var announced = new List<string>();
                shell.Window.GraphCanvas.Announced += (_, text) => announced.Add(text);
                AnimationClock.UntilReflowsSettled(shell.Window);
                Button button = QualityWarning(view).GetVisualDescendants().OfType<Button>().Single();

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                AnimationClock.UntilReflowsSettled(shell.Window);

                RecipeNode copy = Assert.Single(shell.Graph.Nodes.OfType<RecipeNode>());
                Assert.Equal("normal", copy.BaseRecipe.Quality!.Name);
                Assert.NotEqual(node.Id.Value, copy.NodeID);
                BaseNodeElement selected = Assert.Single(shell.Window.GraphCanvas.Viewer.SelectedNodes);
                Assert.Equal(copy.NodeID, selected.ViewModel.Id.Value);
                Assert.Equal(copy.NodeID, view.ViewModel!.Node.Id.Value);
                Assert.False(QualityWarning(view).IsVisible);
                Assert.Equal(LockSentence, QualityRow(view).Note);
                Assert.Contains("Sulfur switched to Normal quality", announced);
                Assert.True(shell.Window.Inspector.IsKeyboardFocusWithin);
            }
        }

        [AvaloniaFact]
        public void QualityRow_ValueAccessibility() {
            (ShellFixture shell, RecipeSegmentView view, _) = OpenInspectorOn("iron-gear-wheel", "uncommon");
            using (shell) {
                ValueText value = QualityValue(view);
                AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(value);
                Assert.Equal("Recipe quality", peer.GetName());
                Assert.Equal("Uncommon", peer.GetProvider<IValueProvider>()!.Value);
                Assert.Equal("Set when the node is added.", peer.GetHelpText());
                Assert.False(value.Focusable);
            }

            (ShellFixture lockedShell, RecipeSegmentView lockedView, _) = OpenInspectorOn("sulfur", "normal");
            using (lockedShell)
                Assert.Equal(LockSentence, ControlAutomationPeer.CreatePeerForElement(QualityValue(lockedView)).GetHelpText());

            (ShellFixture warnedShell, RecipeSegmentView warnedView, _) = OpenInspectorOn("sulfur", "uncommon");
            using (warnedShell) {
                AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(QualityValue(warnedView));
                Assert.Equal("Uncommon", peer.GetProvider<IValueProvider>()!.Value);
                Assert.Equal(LockSentence, peer.GetHelpText());
                Button button = QualityWarning(warnedView).GetVisualDescendants().OfType<Button>().Single();
                Assert.Equal("Switch to Normal Quality", AutomationProperties.GetName(button));
                Assert.Equal("Replaces this node with a Normal quality copy. Its settings stay; links for items the copy doesn\u2019t make are removed.", AutomationProperties.GetHelpText(button));
            }
        }

        [AvaloniaFact]
        public void BuildingQualityMiniSelect_UnsettableRecipe_OffersAllEnabledTiers() {
            (ShellFixture shell, _, _) = OpenInspectorOn("sulfur", "uncommon");
            using (shell) {
                PopUp miniSelect = shell.Window.Inspector.FindControl<PopUp>("QualityMiniSelect")!;
                DataCache cache = shell.Window.DataCache!;

                Assert.True(miniSelect.IsVisible);
                Assert.Equal(cache.AvailableQualities.Count(q => q.Enabled), miniSelect.Items.Count);
                Assert.True(miniSelect.IsEnabled);
            }
        }

        [AvaloniaFact]
        public void BuildingQualityMiniSelect_AccessibleName_IsBuildingQuality() {
            (ShellFixture shell, _, _) = OpenInspectorOn("iron-gear-wheel", "normal");
            using (shell) {
                PopUp miniSelect = shell.Window.Inspector.FindControl<PopUp>("QualityMiniSelect")!;

                Assert.Equal("Building quality", AutomationProperties.GetName(miniSelect));
                Assert.Equal("Building quality", ToolTip.GetTip(miniSelect));
            }
        }

        [AvaloniaFact]
        public void QualityRowAndBlock_CarryTheFormRowReflowTransitions() {
            (ShellFixture shell, RecipeSegmentView view, _) = OpenInspectorOn("sulfur", "uncommon");
            using (shell) {
                var group = (Control)QualityRow(view).Parent!.Parent!;
                foreach (Control control in new Control[] { QualityRow(view), QualityWarning(view) }) {
                    Assert.NotNull(control.Transitions);
                    Assert.Contains(control.Transitions!, t => t is Avalonia.Animation.DoubleTransition { Property.Name: "Opacity" });
                    Assert.Contains(control.Transitions!, t => t is Avalonia.Animation.DoubleTransition { Property.Name: "Height" });
                }
                Assert.True(group.IsVisible);
            }
        }
    }
}
