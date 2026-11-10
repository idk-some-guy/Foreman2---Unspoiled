using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Research;
using Foreman.Mac.Views.Sheets;
using Foreman.Models.Research;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views.Research {
    public class ResearchWindowTests {
        internal static readonly DateTime SyncedLocal = new(2026, 11, 10, 14, 5, 0, DateTimeKind.Local);
        internal const long PlayedTicks = (41L * 3600 + 7 * 60) * 60;

        private static readonly Lazy<string> ExistingSave = new(() => {
            string path = Path.Combine(Path.GetTempPath(), "research-window-" + Guid.NewGuid().ToString("N") + ".zip");
            File.WriteAllBytes(path, []);
            return path;
        });

        internal static ResearchSource Source(string name = "Megabase", long? ticks = PlayedTicks, string? path = null) =>
            new(name, path ?? ExistingSave.Value, ticks, SyncedLocal.ToUniversalTime());

        internal static ResearchState Synced(ResearchSource? source = null, Dictionary<ResearchField, double>? values = null, Dictionary<string, double>? recipes = null) {
            values ??= new() { [ResearchField.MiningProductivity] = 1.2 };
            recipes ??= [];
            return new ResearchState(source ?? Source(), values, values, recipes, recipes, []);
        }

        internal sealed class FixedClock(DateTime localNow) : TimeProvider {
            public override DateTimeOffset GetUtcNow() => new DateTimeOffset(localNow).ToUniversalTime();
        }

        internal sealed class Rig : IDisposable {
            public readonly ShellFixture Shell = ShellFixture.Open();

            public MainWindow Window => Shell.Window;
            public ProductionGraph Graph => Shell.Graph;
            public ResearchWindow Research => Window.CurrentResearchWindow!;
            public ToolbarItem ResearchButton => Window.Toolbar.Groups.SelectMany(g => g.Items).Single(i => i.Name == "ResearchButton");

            public ResearchWindow Open(ResearchState? state = null) {
                if (state is not null)
                    Graph.Research = state;
                Window.ShowResearch();
                Dispatcher.UIThread.RunJobs();
                Research.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                return Research;
            }

            public ResearchWindow OpenDirect(ResearchState state, ResearchWindowHost host) {
                Graph.Research = state;
                ResearchWindow window = ResearchWindow.Create(Window.GraphCanvas.Viewer.Session, host);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                return window;
            }

            public ResearchWindowHost Host(Func<string?, bool>? exists = null, DateTime? now = null) => new() {
                Sync = () => Window.ResearchSync,
                Settings = Window.Settings,
                SaveExists = exists ?? (_ => true),
                Clock = new FixedClock(now ?? SyncedLocal.AddHours(6)),
            };

            public void Dispose() => Shell.Dispose();
        }

        [AvaloniaFact]
        public void ResearchWindow_Layout() {
            using var rig = new Rig();
            rig.Window.ViewModel.DocumentName = "Green Circuit Factory";
            ResearchWindow window = rig.Open(Synced());

            Assert.Equal(620, window.Width);
            Assert.Equal(600, window.Height);
            Assert.Equal(520, window.MinWidth);
            Assert.Equal(460, window.MinHeight);
            Assert.Equal("Research — Green Circuit Factory", window.Title);
            Assert.Equal(38, window.ToolbarRowControl.Bounds.Height);
            Assert.Equal(["Bonuses", "Recipes", "Technologies"], window.SegmentControl.Items);
            Assert.Equal(0, window.SegmentControl.SelectedIndex);

            rig.Window.ViewModel.DocumentName = new string('x', 60);
            Assert.Equal("Research — " + Foreman.Mac.Dialogs.MiddleTruncate(new string('x', 60), 40), window.Title);

            rig.Window.ShowResearch();
            Assert.Same(window, rig.Window.CurrentResearchWindow);
            Assert.Single(rig.Window.OwnedWindows.OfType<ResearchWindow>());

            bool closed = false;
            window.Closed += (_, _) => closed = true;
            rig.Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
        }

        [AvaloniaFact]
        public void ResearchWindow_Layout_SegmentKeysAndMemory() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Synced());

            CanvasKeys.KeyDownOn(window, Key.D2, KeyModifiers.Meta);
            Assert.Equal(1, window.SegmentControl.SelectedIndex);
            CanvasKeys.KeyDownOn(window, Key.D3, KeyModifiers.Meta);
            Assert.Equal(2, window.SegmentControl.SelectedIndex);
            CanvasKeys.KeyDownOn(window, Key.D1, KeyModifiers.Meta);
            Assert.Equal(0, window.SegmentControl.SelectedIndex);
            CanvasKeys.KeyDownOn(window, Key.D3, KeyModifiers.Meta);
            Assert.Equal(2, rig.Window.Settings!.ResearchSegment);

            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(rig.Window.CurrentResearchWindow);
            window = rig.Open();
            Assert.Equal(2, window.SegmentControl.SelectedIndex);
        }

        [AvaloniaFact]
        public void ResearchWindow_Placement_IsRememberedUnscaled() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Synced());
            window.Width = 700;
            window.Height = 640;
            window.Position = new PixelPoint(40, 60);
            window.Close();
            Dispatcher.UIThread.RunJobs();

            AppSettings settings = rig.Window.Settings!;
            Assert.Equal(40, settings.ResearchLeft);
            Assert.Equal(60, settings.ResearchTop);
            Assert.NotNull(settings.ResearchWidth);
            Assert.NotNull(settings.ResearchHeight);

            ResearchWindow reopened = rig.Open();
            Assert.Equal(new PixelPoint(40, 60), reopened.Position);
            Assert.Equal(settings.ResearchWidth, reopened.Width);
        }

        [AvaloniaFact]
        public void SourceBar_States() {
            using var rig = new Rig();
            ResearchWindow window = rig.OpenDirect(Synced(), rig.Host());
            ResearchSourceBar bar = window.SourceBar;

            Assert.Equal("Megabase", bar.NameText);
            Assert.Equal("41 h 7 min played · synced today at 14:05", bar.MetaText);
            Assert.False(bar.IsSyncing);
            Assert.True(bar.SyncButton.IsEnabled);
            Assert.True(bar.ChooseButton.IsEnabled);
            Assert.Null(bar.EditsText);
            Assert.False(bar.RevertAllButton.IsVisible);
            Assert.Null(bar.WarningText);
            Assert.Equal("Source", AutomationProperties.GetName(bar));
            Assert.Equal("Megabase, 41 hours 7 minutes played, synced today at 14:05", AutomationProperties.GetHelpText(bar));
            Assert.Equal("Choose Save…", bar.ChooseButton.Content);
            Assert.Equal("Sync", bar.SyncButton.Content);

            rig.Graph.Research = rig.Graph.Research.WithValue(ResearchField.MiningProductivity, 1.5).WithValue(ResearchField.LabSpeed, 0.4);
            window.Refresh();
            Assert.Equal("2 values edited by hand", bar.EditsText);
            Assert.True(bar.RevertAllButton.IsVisible);
            Assert.Equal("Revert All", bar.RevertAllButton.Content);
            Assert.Equal("Megabase, 41 hours 7 minutes played, synced today at 14:05, 2 values edited by hand", AutomationProperties.GetHelpText(bar));
            rig.Graph.Research = rig.Graph.Research.Revert(ResearchField.LabSpeed);
            window.Refresh();
            Assert.Equal("1 value edited by hand", bar.EditsText);

            rig.Graph.Research = Synced(Source(ticks: null));
            window.Refresh();
            Assert.Equal("synced today at 14:05", bar.MetaText);
            Assert.Equal("Megabase, synced today at 14:05", AutomationProperties.GetHelpText(bar));

            rig.Graph.Research = ResearchState.None;
            window.Refresh();
            Assert.Equal("Not synced", bar.NameText);
            Assert.Equal("Sync from a save to read this graph’s research.", bar.MetaText);
            Assert.False(bar.SyncButton.IsEnabled);
            Assert.True(bar.ChooseButton.IsEnabled);
            Assert.Equal("Not synced", AutomationProperties.GetHelpText(bar));
            window.Close();
        }

        [AvaloniaFact]
        public void SourceBar_States_Syncing() {
            using var rig = new Rig();
            rig.Graph.Research = Synced().WithValue(ResearchField.LabSpeed, 0.4);
            ResearchWindow window = rig.OpenDirect(rig.Graph.Research, rig.Host());
            ResearchSourceBar bar = window.SourceBar;

            var release = new TaskCompletionSource<ResearchSyncOutcome>();
            rig.Window.ResearchSync.SheetStub = _ => release.Task;
            Task run = rig.Window.ResearchSync.RunAsync(rig.Window, chooseAnother: false);
            Dispatcher.UIThread.RunJobs();

            Assert.True(bar.IsSyncing);
            Assert.Equal("Syncing from “Megabase”…", bar.MetaText);
            Assert.False(bar.ChooseButton.IsEnabled);
            Assert.False(bar.SyncButton.IsEnabled);
            Assert.False(bar.RevertAllButton.IsEnabled);

            release.SetResult(ResearchSyncOutcome.Cancelled);
            run.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.False(bar.IsSyncing);
            Assert.True(bar.SyncButton.IsEnabled);
            Assert.True(bar.RevertAllButton.IsEnabled);
            window.Close();
        }

        [AvaloniaFact]
        public void SourceBar_States_MissingSave() {
            using var rig = new Rig();
            bool exists = false;
            ResearchWindow window = rig.OpenDirect(Synced(), rig.Host(path => exists));
            ResearchSourceBar bar = window.SourceBar;

            Assert.Equal("This save was moved or deleted. Choose it again to sync.", bar.WarningText);
            Assert.False(bar.SyncButton.IsEnabled);
            Assert.True(bar.ChooseButton.IsEnabled);
            Assert.Equal("Megabase, 41 hours 7 minutes played, synced today at 14:05, save moved or deleted", AutomationProperties.GetHelpText(bar));

            exists = true;
            window.CheckSource();
            Assert.Null(bar.WarningText);
            Assert.True(bar.SyncButton.IsEnabled);

            exists = false;
            window.CheckSource();
            Assert.NotNull(bar.WarningText);
            window.Close();
        }

        [AvaloniaFact]
        public void ResearchWindow_ToolbarToggleAndMenu() {
            using var rig = new Rig();
            Dispatcher.UIThread.RunJobs();

            ToolbarGroup group = rig.Window.Toolbar.Groups.Single(g => g.Role == ToolbarGroupRole.Research);
            Assert.Equal(["Sync Research", "Research"], group.Items.Select(i => i.AutomationName));
            Assert.Equal("Research", group.Items[1].OverflowLabel);
            Avalonia.Application.Current!.TryGetResource("Kit.Glyph.Research.17", null, out object? glyph);
            Assert.Same(glyph, group.Items[1].Glyph);
            Assert.True(group.Items[1].IsToggle);
            Assert.Equal("⌘R", AutomationProperties.GetAcceleratorKey(group.Items[0]));

            NativeMenu windowMenu = NativeMenu.GetMenu(rig.Window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Window").Menu!;
            NativeMenuItem[] items = [.. windowMenu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator)];
            int comparator = Array.FindIndex(items, i => i.Header == "Preset Comparator");
            NativeMenuItem research = items[comparator + 1];
            Assert.Equal("Research", research.Header);
            Assert.Equal(new KeyGesture(Key.R, PlatformModifiers.Primary | KeyModifiers.Alt), research.Gesture);

            Assert.False(rig.ResearchButton.IsOn);
            research.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(rig.Window.CurrentResearchWindow);
            Assert.True(rig.ResearchButton.IsOn);

            rig.ResearchButton.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(rig.Window.CurrentResearchWindow);
            Assert.False(rig.ResearchButton.IsOn);

            rig.Window.ShowResearch();
            Dispatcher.UIThread.RunJobs();
            Assert.True(rig.ResearchButton.IsOn);
            rig.Research.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(rig.ResearchButton.IsOn);
        }

        [AvaloniaFact]
        public void ResearchWindow_SyncSheet_ShowResearchOpensTheWindow() {
            using var rig = new Rig();
            Assert.NotNull(rig.Window.ResearchSync.ShowResearch);
            rig.Window.ResearchSync.ShowResearch!();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(rig.Window.CurrentResearchWindow);
            Assert.Equal(0, rig.Research.SegmentControl.SelectedIndex);
        }

        [AvaloniaFact]
        public void ResearchWindow_Keyboard() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Synced(values: new() { [ResearchField.MiningProductivity] = 1.2, [ResearchField.LabSpeed] = 0.2 }));
            rig.Graph.Research = rig.Graph.Research.WithValue(ResearchField.MiningProductivity, 1.5);
            window.Refresh();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal("Research view", AutomationProperties.GetName(window.SegmentControl));
            ResearchBonusRow mining = window.BonusRow(ResearchField.MiningProductivity);
            ResearchBonusRow lab = window.BonusRow(ResearchField.LabSpeed);
            Control[] expected = [window.SegmentControl, window.SourceBar.ChooseButton, window.SourceBar.SyncButton, window.SourceBar.RevertAllButton,
                mining.Stepper, mining.Revert, window.RecipeProductivityRow.ShowRecipesButton, lab.Stepper];

            window.SegmentControl.Focus(NavigationMethod.Tab);
            Assert.True(expected[0].IsKeyboardFocusWithin);
            for (int i = 1; i < expected.Length; i++) {
                CanvasKeys.KeyDownOn((Control)window.FocusManager!.GetFocusedElement()!, Key.Tab);
                Assert.True(expected[i].IsKeyboardFocusWithin, $"Tab {i} should reach {expected[i].GetType().Name}, reached {window.FocusManager.GetFocusedElement()}");
            }
        }
    }
}
