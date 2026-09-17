using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    //Phase 9 Task 7: the scale contract - asserts against ScaleContract's token values and live bindings.
    [Collection(ScaleTestGroup.Name)]
    public class UiScaleTests : IDisposable {
        public UiScaleTests() => WindowScaler.SetScale(1.0);

        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        private static double ResourceDouble(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            return Assert.IsType<double>(value);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_MultipliesTheTypeRampAndControlHeights() {
            WindowScaler.SetScale(1.5);

            Assert.Equal(19.5, ResourceDouble("Foreman.Type.Body.FontSize"));
            Assert.Equal(33.0, ResourceDouble("Foreman.Size.ControlHeightRegular"));
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_LeavesBordersRadiiFocusRingAndScrimFixed() {
            WindowScaler.SetScale(1.5);

            Assert.Equal(3.0, ResourceDouble("Foreman.FocusRing.Width"));

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(new CornerRadius(6), radius);

            Application.Current!.TryGetResource("Foreman.Color.SheetScrim", Application.Current.ActualThemeVariant, out object? scrim);
            Assert.Equal(Avalonia.Media.Color.Parse("#80000000"), scrim);
        }

        //A window opened after a scale change picks up the multiplied resources immediately, since they
        //live on Application.Resources rather than a per-window wrapper - the whole point of the re-push.
        [AvaloniaFact]
        public void Window_OpenedAfterScaleChange_ShowsMultipliedValuesThroughLiveBinding() {
            WindowScaler.SetScale(1.5);

            var probe = new Border();
            probe.Bind(Border.WidthProperty, new DynamicResourceExtension("Foreman.Size.ControlHeightRegular"));
            var window = new Window { Content = probe };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33.0, probe.Width);
        }

        [AvaloniaFact]
        public void DynamicResourceConsumer_Border_ResizesLive_WhenScaleChanges() {
            var probe = new Border();
            probe.Bind(Border.WidthProperty, new DynamicResourceExtension("Foreman.Size.ControlHeightRegular"));
            var window = new Window { Content = probe };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(22.0, probe.Width);

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33.0, probe.Width);
        }

        //Kit.PopoverFrame renders directly in PopoverHost's Canvas, so DynamicResource reaches it live with no LayoutTransformControl needed anywhere in its ancestry.
        [AvaloniaFact]
        public void SetScale_ViewOptionsPopoverContent_FollowsWithoutAnyLayoutTransform() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var label = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Gridlines");
            var popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single(p => p.Name == "ViewOptionsPopover");
            var panel = popover.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");
            Assert.DoesNotContain(popover.GetVisualAncestors(), a => a is LayoutTransformControl);
            Assert.Equal(11.0, label.FontSize);
            Assert.Equal(272, panel.Bounds.Width);

            //The trailing group owns no bottom margin, so the panel's own padding is the last row's only gap below it.
            FormRow lastRow = popover.GetVisualDescendants().OfType<FormRow>().Last();
            double panelContentBottom = panel.TranslatePoint(new Point(0, panel.Bounds.Height), window)!.Value.Y - panel.BorderThickness.Bottom;
            double rowBottom = lastRow.TranslatePoint(new Point(0, lastRow.Bounds.Height), window)!.Value.Y;
            Assert.Equal(panel.Padding.Bottom, panelContentBottom - rowBottom, 1);

            FormGroup[] groups = [.. popover.GetVisualDescendants().OfType<FormGroup>()];
            double gridlinesBottom = groups[0].TranslatePoint(new Point(0, groups[0].Bounds.Height), window)!.Value.Y;
            double graphTop = groups[1].TranslatePoint(new Point(0, 0), window)!.Value.Y;
            Assert.Equal(16, graphTop - gridlinesBottom, 1);

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(16.5, label.FontSize);
            Assert.Equal(408, panel.Bounds.Width);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_InterpolatesMainWindowMinimumSizeBetweenContractEndpoints() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(900.0, window.MinWidth);
            Assert.Equal(600.0, window.MinHeight);

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1025.0, window.MinWidth);
            Assert.Equal(660.0, window.MinHeight);

            WindowScaler.SetScale(2.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1150.0, window.MinWidth);
            Assert.Equal(720.0, window.MinHeight);
        }

        //No contract endpoint on this window, so its minimum is plain base times the scale step.
        [AvaloniaFact]
        public void SetScale_AtOneFifty_ScalesAddBrowserWindowMinimumSize_ByBaseTimesScale() {
            var window = new AddBrowserWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(960.0, window.MinWidth);
            Assert.Equal(660.0, window.MinHeight);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_ScalesPresetComparatorWindowMinimumSize_ByBaseTimesScale() {
            var window = new PresetComparatorWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1018.5, window.MinWidth);
            Assert.Equal(621.0, window.MinHeight);
        }

        //AddBrowserWindow leaves the hub while MainWindow hides it, so a scale change made during that
        //gap must still land once it is shown again, not wait for the next unrelated notification.
        [AvaloniaFact]
        public void AddBrowserWindow_MinimumSizeResyncsOnShow_AfterAScaleChangeWhileHidden() {
            var window = new AddBrowserWindow();
            window.Show();
            window.Hide();
            Dispatcher.UIThread.RunJobs();

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(960.0, window.MinWidth);
            Assert.Equal(660.0, window.MinHeight);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_CanvasTokenBridgeNodePaddingFollows() {
            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(12.0, TokenBridge.Current.NodePadding);
        }

        [AvaloniaFact]
        public void SetScale_OutOfRangeValue_Clamps() {
            WindowScaler.SetScale(0.4);
            Assert.Equal(1.0, WindowScaler.CurrentScale);

            WindowScaler.SetScale(9.0);
            Assert.Equal(2.0, WindowScaler.CurrentScale);
        }

        //A persisted UiScale takes effect at boot, not just after opening Settings once.
        [AvaloniaFact]
        public void ApplyLoadedSettings_UiScaleFromSettings_ScalesResourcesAtBoot() {
            var window = new MainWindow { Settings = new AppSettings { UiScale = 1.5 } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ApplyLoadedSettings();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33.0, ResourceDouble("Foreman.Size.ControlHeightRegular"));
            Assert.False(window.Content is LayoutTransformControl);
        }

        [AvaloniaFact]
        public void DefaultScale_ResourcesEqualBaseValues_AndContentIsNotWrapped() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(13.0, ResourceDouble("Foreman.Type.Body.FontSize"));
            Assert.Equal(22.0, ResourceDouble("Foreman.Size.ControlHeightRegular"));
            Assert.False(window.Content is LayoutTransformControl);
        }

        //ViewScale is the graph's own zoom (mouse wheel only) - UiScale must never touch it.
        [AvaloniaFact]
        public void SetScale_DoesNotAlterGraphCanvasViewportZoom() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            float viewScaleBefore = window.GraphCanvas.Viewport.ViewScale;

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(viewScaleBefore, window.GraphCanvas.Viewport.ViewScale);
        }
    }
}
