using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Converters;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    [Collection(ScaleTestGroup.Name)]
    public class TitleBarChromeMetricsTests {
        [AvaloniaFact]
        public void DecorationMarginProxy_MutatedAfterShow_StillReachesLeadingGroup() {
            var window = new SimulatedChromeWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.DecorationMarginProxy = new Thickness(0, 52, 0, 0);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(new Thickness(0), window.LeadingGroup.Margin);
        }

        [AvaloniaFact]
        public void LeadingGroup_ClearsTrafficLightZone_OnlyWhenExtendClientAreaIsActive() {
            var window = new SimulatedChromeWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, window.LeadingGroup.Margin.Left);

            window.DecorationMarginProxy = new Thickness(0, 52, 0, 0);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(TrafficLightInsetConverter.TrafficLightInsetWidth, window.LeadingGroup.Margin.Left);
        }

        [AvaloniaFact]
        public void MainWindow_TitleBarRowHeight_MatchesExtendClientAreaTitleBarHeightHint() {
            var window = new Foreman.Mac.MainWindow();
            window.Show();

            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");

            Assert.Equal(window.ExtendClientAreaTitleBarHeightHint, titleBar.Height);
        }

        [AvaloniaFact]
        public async Task ShowToolbarOff_IsA28ptTitlebarWithTheTitleCentred_AndLeavesTheGraphStill_AtEveryScale() {
            try {
                foreach (double scale in new[] { 1.0, 1.5 }) {
                    WindowScaler.SetScale(scale);
                    Foreman.Mac.MainWindow window = await Inspector.InspectorTestSupport.OpenWindowWithGraphAsync();
                    var settings = new AppSettings();
                    window.Settings = settings;
                    window.ViewModel.Inspector.IsPinned = true;
                    Dispatcher.UIThread.RunJobs();
                    Grid titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");
                    TextBlock title = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TitleBarTitle");
                    Control content = window.GraphViewerHost.GetVisualParent<Control>()!;
                    NativeMenuItem showToolbar = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "View").Menu!
                        .Items.OfType<NativeMenuItem>().Single(i => i.Header == "Show Toolbar");
                    Foreman.Mac.Canvas.Elements.BaseNodeElement node = window.GraphCanvas.NodeElements.Single();
                    Point before = OnWindow(window, node);

                    AssertToolbarShown();

                    showToolbar.Command!.Execute(null);
                    Dispatcher.UIThread.RunJobs();

                    Assert.Equal(28, titleBar.Bounds.Height, 0.5);
                    Assert.False(window.Toolbar.IsVisible);
                    Assert.True(title.IsVisible);
                    Assert.Equal(window.ViewModel.WindowTitle, title.Text);
                    Assert.Equal(window.Bounds.Width / 2, title.TranslatePoint(new Point(title.Bounds.Width / 2, 0), window)!.Value.X, 1.0);
                    double rowTop = content.TranslatePoint(default, window)!.Value.Y;
                    Assert.Equal(28, rowTop, 0.5);
                    Assert.Equal(window.Bounds.Height - 28, content.Bounds.Height, 0.5);
                    Assert.True(window.Inspector.IsVisible);
                    Assert.Equal(rowTop, window.Inspector.TranslatePoint(default, window)!.Value.Y, 0.5);
                    Assert.False(showToolbar.IsChecked);
                    Assert.False(settings.ShowToolbar);
                    AssertStill(before, OnWindow(window, node));

                    showToolbar.Command!.Execute(null);
                    Dispatcher.UIThread.RunJobs();

                    AssertToolbarShown();
                    AssertStill(before, OnWindow(window, node));
                    window.Close();

                    void AssertToolbarShown() {
                        Assert.True(settings.ShowToolbar);
                        Assert.Equal(52, titleBar.Bounds.Height, 0.5);
                        Assert.True(window.Toolbar.IsVisible);
                        Assert.False(title.IsVisible);
                        Assert.True(showToolbar.IsChecked);
                        Assert.Equal(new KeyGesture(Key.T, KeyModifiers.Meta | KeyModifiers.Alt), showToolbar.Gesture);
                    }
                }
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        private static Point OnWindow(Foreman.Mac.MainWindow window, Foreman.Mac.Canvas.Elements.BaseNodeElement node) =>
            window.GraphCanvas.TranslatePoint(window.GraphCanvas.Viewport.GraphToScreen(node.Location), window)!.Value;

        private static void AssertStill(Point expected, Point actual) {
            Assert.Equal(expected.X, actual.X, 0.5);
            Assert.Equal(expected.Y, actual.Y, 0.5);
        }

        [AvaloniaFact]
        public void MainWindow_TitleBarRow_PaintsAboveGraphViewerHost() {
            var window = new Foreman.Mac.MainWindow();
            window.Show();

            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");

            Assert.True(titleBar.ZIndex > window.GraphViewerHost.ZIndex);
        }
    }
}
