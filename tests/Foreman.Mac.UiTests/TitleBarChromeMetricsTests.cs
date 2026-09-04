using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Converters;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Gate-failure fix (2026-09-03 phase9 diagnosis): the live gate found the unified titlebar collapsed on
    //real macOS because Margin bound straight to WindowDecorationMargin reads a top inset there, not a left
    //one, arranging the toolbar group at zero height. Headless never catches this because
    //AvaloniaHeadlessPlatformOptions always reports a zero margin - this rig drives the same layout math
    //with simulated nonzero metrics instead (proven equivalent by the probe behind the diagnosis).
    public class TitleBarChromeMetricsTests {
        private const double Epsilon = 0.01;

        public static TheoryData<double, double, double, double> SimulatedMetrics => new() {
            { 0, 0, 0, 0 },
            { 0, 28, 0, 0 },
            { 0, 52, 0, 0 },
        };

        [AvaloniaTheory]
        [MemberData(nameof(SimulatedMetrics))]
        public void ToolbarButtons_StayInsideTitleBarRow_AcrossSimulatedMetrics(double left, double top, double right, double bottom) {
            var window = new SimulatedChromeWindow();
            window.Show();

            window.DecorationMarginProxy = new Thickness(left, top, right, bottom);
            Dispatcher.UIThread.RunJobs();

            AssertToolbarInsideRow(window);
        }

        [AvaloniaFact]
        public void ToolbarButtons_StayInsideTitleBarRow_AtUiScale1Point5() {
            var window = new SimulatedChromeWindow();
            window.Show();

            window.DecorationMarginProxy = new Thickness(0, 52, 0, 0);
            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            try {
                AssertToolbarInsideRow(window);
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        //Diagnosis test-strategy item 3: after WindowScaler.Track re-parents Content into a
        //LayoutTransformControl, mutating the proxy afterward must still reach the leading group's Margin -
        //locks in the behaviour the probe measured so a future scaler change can't silently sever the binding.
        [AvaloniaFact]
        public void DecorationMarginProxy_MutatedAfterShow_StillReachesLeadingGroup() {
            var window = new SimulatedChromeWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.DecorationMarginProxy = new Thickness(0, 52, 0, 0);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(new Thickness(0), window.LeadingGroup.Margin);
        }

        //Task requirement (d): the leading group must clear the simulated traffic-light zone when the
        //extend-client-area metric is actually nonzero, and must not be padded when it's zero (Linux/other
        //chrome that never extends shouldn't carry the inset pointlessly).
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

        //Diagnosis fix set item 3: TitleBarRow.Height must derive from the window's own
        //ExtendClientAreaTitleBarHeightHint instead of repeating the "52" literal, so the two can't drift.
        [AvaloniaFact]
        public void MainWindow_TitleBarRowHeight_MatchesExtendClientAreaTitleBarHeightHint() {
            var window = new Foreman.Mac.MainWindow();
            window.Show();

            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");

            Assert.Equal(window.ExtendClientAreaTitleBarHeightHint, titleBar.Height);
        }

        //Diagnosis fix set item 2: TitleBarRow must clip its own children, so nothing it lays out can bleed
        //into the canvas slot even if a future chrome metric misbehaves the way WindowDecorationMargin did.
        [AvaloniaFact]
        public void MainWindow_TitleBarRow_ClipsToBounds() {
            var window = new Foreman.Mac.MainWindow();
            window.Show();

            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");

            Assert.True(titleBar.ClipToBounds);
        }

        //Diagnosis fix set item 2: TitleBarRow must out-rank GraphViewerHost in paint order, so the canvas
        //can never end up drawn over the chrome regardless of declaration order in the XAML.
        [AvaloniaFact]
        public void MainWindow_TitleBarRow_PaintsAboveGraphViewerHost() {
            var window = new Foreman.Mac.MainWindow();
            window.Show();

            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");

            Assert.True(titleBar.ZIndex > window.GraphViewerHost.ZIndex);
        }

        //Diagnosis test-strategy item 5: stops the exact mistake from returning - a bare Margin bound
        //straight to WindowDecorationMargin (no converter) is the root cause of the live-gate failure.
        [Fact]
        public void NoAxamlFile_BindsMarginDirectlyToWindowDecorationMargin() {
            string repoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
            string srcDir = Path.Combine(repoRoot, "src", "Foreman.Mac");
            var offenders = new System.Collections.Generic.List<string>();

            foreach (string path in Directory.EnumerateFiles(srcDir, "*.axaml", SearchOption.AllDirectories)) {
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"Margin=""\{Binding[^""]*WindowDecorationMargin[^""]*""")) {
                    if (!match.Value.Contains("Converter="))
                        offenders.Add($"{path}: {match.Value}");
                }
            }

            Assert.Empty(offenders);
        }

        private static void AssertToolbarInsideRow(SimulatedChromeWindow window) {
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            Dispatcher.UIThread.RunJobs();

            Rect rowRect = WindowBounds(window.TitleBarRow, window);
            Rect iconRect = WindowBounds(window.IconButton, window);
            Rect wideRect = WindowBounds(window.WideButton, window);
            Rect canvasRect = WindowBounds(window.CanvasHost, window);

            Assert.True(iconRect.Y >= rowRect.Y - Epsilon, $"icon top {iconRect.Y} above row top {rowRect.Y}");
            Assert.True(iconRect.Bottom <= rowRect.Bottom + Epsilon, $"icon bottom {iconRect.Bottom} below row bottom {rowRect.Bottom}");
            Assert.True(wideRect.Y >= rowRect.Y - Epsilon, $"wide button top {wideRect.Y} above row top {rowRect.Y}");
            Assert.True(wideRect.Bottom <= rowRect.Bottom + Epsilon, $"wide button bottom {wideRect.Bottom} below row bottom {rowRect.Bottom}");
            Assert.True(canvasRect.Y >= rowRect.Bottom - Epsilon, $"canvas top {canvasRect.Y} above row bottom {rowRect.Bottom}");
        }

        private static Rect WindowBounds(Visual control, Window window) {
            Matrix transform = control.TransformToVisual(window)!.Value;
            return new Rect(control.Bounds.Size).TransformToAABB(transform);
        }
    }
}
