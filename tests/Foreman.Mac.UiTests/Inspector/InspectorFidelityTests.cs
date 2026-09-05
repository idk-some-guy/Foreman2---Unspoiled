using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Views.Inspector;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    //Task 9 gate-fix fidelity pass: structural assertions from the brief's own checklist that don't belong
    //to any one segment's existing test file - only the body scrolls, group titles read uppercase, only
    //Assembler/Modules/Beacon collapse, and toggle rows carry no "On"/"Off" text.
    public class InspectorFidelityTests {
        [AvaloniaFact]
        public async Task IdentityHeaderAndSegmentedControl_AreNotInsideTheScrollViewer() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            ScrollViewer scroller = window.Inspector.FindControl<ScrollViewer>("InspectorScroller")!;
            Control segmentControl = window.Inspector.FindControl<Control>("SegmentControl")!;
            Border identityRow = window.Inspector.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("foreman-identity-row"));

            Assert.DoesNotContain(segmentControl, scroller.GetVisualDescendants());
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
    }
}
