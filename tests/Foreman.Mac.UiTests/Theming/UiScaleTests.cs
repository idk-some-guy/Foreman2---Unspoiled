using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    //Phase 9 Task 3: user-adjustable UI scale. WindowScaler wraps each top-level window's content in a
    //LayoutTransformControl, mirroring ThemeService's one-call-site shape for FlagDarkMode. These tests
    //measure real Avalonia layout (TranslatePoint through the live visual tree) rather than asserting on
    //WindowScaler's own bookkeeping, since the whole point of the mechanism is what it does to on-screen
    //bounds.
    public class UiScaleTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        //WindowScaler carries static, process-wide state (mirrors ThemeService's Application.Current
        //singleton, already reset explicitly per-test by DarkModeTests) - reset before every test here so
        //a leftover scale from a previous test can't leak in.
        public UiScaleTests() => WindowScaler.SetScale(1.0);

        private static SettingsWindow.SettingsWindowOptions NewOptions(DataCache cache) =>
            new(cache) { Presets = [new Preset(VanillaPresetName, true, true)] };

        private static double TransformedWidth(Control control, Window window) {
            Point topLeft = control.TranslatePoint(new Point(0, 0), window)!.Value;
            Point topRight = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value;
            return topRight.X - topLeft.X;
        }

        //Acceptance criterion: a known control's on-screen bounds at scale 1.25 are exactly 1.25x its
        //bounds at scale 1.0. QualityStepsInputControl carries an explicit XAML Width, so its LOCAL size
        //never reflows with available space - only the ambient LayoutTransformControl changes.
        [AvaloniaFact]
        public async Task SetScale_KnownControlBounds_AreExactlyOnePointTwoFiveTimesDefault() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync()));
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 2; //Graph Options tab - TabControl only realizes the selected tab's content
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double widthAtDefault = TransformedWidth(window.QualityStepsInputControl, window);

            WindowScaler.SetScale(1.25);
            Dispatcher.UIThread.RunJobs();
            double widthAtOneQuarterUp = TransformedWidth(window.QualityStepsInputControl, window);

            Assert.Equal(widthAtDefault * 1.25, widthAtOneQuarterUp, 2);
        }

        //A window opened while a non-default scale is already active must pick it up immediately, not
        //just on the next Settings Confirm.
        [AvaloniaFact]
        public async Task Track_WindowOpenedAfterScaleChange_StartsAtCurrentScale() {
            WindowScaler.SetScale(1.5);

            var window = new SettingsWindow(NewOptions(await GetCacheAsync()));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var wrapper = Assert.IsType<LayoutTransformControl>(window.Content);
            var transform = Assert.IsType<ScaleTransform>(wrapper.LayoutTransform);
            Assert.Equal(1.5, transform.ScaleX);
            Assert.Equal(1.5, transform.ScaleY);
        }

        [AvaloniaFact]
        public void SetScale_OutOfRangeValue_Clamps() {
            WindowScaler.SetScale(0.4);
            Assert.Equal(1.0, WindowScaler.CurrentScale);

            WindowScaler.SetScale(9.0);
            Assert.Equal(2.0, WindowScaler.CurrentScale);
        }

        //--- Canvas/UI-scale independence -----------------------------------------------------------------

        //CRITICAL INDEPENDENCE RULE: GraphCanvasControl's Viewport (Canvas/Viewport.cs) never reads
        //UiScale/WindowScaler - ViewScale (the graph's own zoom) is written only by ZoomAt (mouse wheel).
        //A UiScale change can move GraphCanvasControl's local Bounds (the LayoutTransformControl measure
        //pass gives it proportionally less/more local space) which re-centers ViewOffset through
        //SetSize's UpdateGraphBounds call, but it must never touch ViewScale itself - that's the one field
        //that would double-apply zoom on top of the user's own scroll-wheel zoom if something here wired
        //it to UiScale by mistake.
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

        //FloatingPanelHost's own Canvas is a child inside GraphCanvasControl's visual tree (Canvas/Panels/
        //FloatingPanelHost.cs), which in turn sits inside MainWindow's Content - so it inherits UiScale
        //through the ordinary Avalonia ancestor-transform chain rather than needing its own wiring.
        [AvaloniaFact]
        public void SetScale_FloatingPanelContent_ScalesWithUiScale() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var content = new Border { Width = 40, Height = 20, Focusable = true };
            window.GraphCanvas.FloatingPanelHost.Show(content, new System.Drawing.Point(0, 0));
            Dispatcher.UIThread.RunJobs();
            double widthAtDefault = TransformedWidth(content, window);

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();
            double widthAtOneAndHalf = TransformedWidth(content, window);

            Assert.Equal(widthAtDefault * 1.5, widthAtOneAndHalf, 2);
        }

        //Zero-change proof at the default scale: GraphCanvasControl's own Skia draw operation (DrawOperation
        //in GraphCanvasControl.cs) renders through the exact same code path Avalonia already uses for every
        //other control under a ScaleTransform=1.0 identity - a plain sibling render, not a special case.
        [AvaloniaFact]
        public void DefaultScale_LayoutTransformIsIdentity_NoVisibleChange() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var wrapper = Assert.IsType<LayoutTransformControl>(window.Content);
            var transform = Assert.IsType<ScaleTransform>(wrapper.LayoutTransform);
            Assert.Equal(1.0, transform.ScaleX);
            Assert.Equal(1.0, transform.ScaleY);
        }
    }
}
