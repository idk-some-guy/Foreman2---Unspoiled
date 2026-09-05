using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Graph;
using Foreman.Mac.Views.Inspector;
using System.Linq;
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
    }
}
