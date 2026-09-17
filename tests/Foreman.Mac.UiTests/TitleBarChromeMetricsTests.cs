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
    [Collection(ScaleTestGroup.Name)]
    public class TitleBarChromeMetricsTests {
        //Diagnosis test-strategy item 3: mutating the proxy after Show() must still reach the leading group's Margin.
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
    }
}
