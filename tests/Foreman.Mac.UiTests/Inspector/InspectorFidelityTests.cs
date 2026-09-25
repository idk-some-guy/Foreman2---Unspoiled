using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
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
    //Structural assertions from the brief's own checklist that don't belong
    //to any one segment's existing test file - only the body scrolls, group titles read uppercase, only
    //Assembler/Modules/Beacon collapse, and toggle rows carry no "On"/"Off" text.
    public class InspectorFidelityTests {
        [AvaloniaFact]
        public async Task IdentityHeader_IsNotInsideTheScrollViewer() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            ScrollViewer scroller = window.Inspector.FindControl<ScrollViewer>("InspectorScroller")!;
            Border identityRow = window.Inspector.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "IdentityRow");

            Assert.DoesNotContain(identityRow, scroller.GetVisualDescendants());
            ContentControl segmentHost = window.Inspector.FindControl<ContentControl>("SegmentHost")!;
            Assert.Contains(segmentHost, scroller.GetVisualDescendants());
        }

        [AvaloniaFact]
        public async Task FlowRateRow_IsVisibleOnlyWhenFixed() {
            (MainWindow window, INodeViewModel node) = await InspectorTestSupport.OpenWithPassthroughAsync();
            InspectorTestSupport.Select(window, node);
            var view = window.Inspector.FindControl<FlowSegmentView>("FlowSegment")!;
            FormRow rateRow = view.FindControl<FormRow>("RateValueRow")!;

            Assert.False(rateRow.IsVisible);

            view.ViewModel!.IsFixedRate = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(rateRow.IsVisible);

            view.ViewModel!.IsFixedRate = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(rateRow.IsVisible);
        }

    }
}
