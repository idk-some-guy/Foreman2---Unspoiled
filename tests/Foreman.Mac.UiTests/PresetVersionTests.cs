using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class PresetVersionTests {
        private const string Sentence = "To get the latest fixes, import it again from Factorio in Settings > Presets.";
        private const string TwoZero = "Factorio 2.0 Vanilla";
        private const string TwoOne = "Factorio 2.1 Vanilla";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedTwoZeroCache;

        private static Preset WriteUserPreset(string name, string json) {
            string path = PresetProcessor.GetUserPresetPath(name, ".pjson");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            return new Preset(name, false, false);
        }

        private static Preset OldModpack() =>
            WriteUserPreset("Old Modpack", "{\"mods\":[{\"name\":\"base\",\"version\":\"2.0.28\"}]}");

        private static Preset MysteryPack() =>
            WriteUserPreset("Mystery Pack", "{\"mods\":[{\"name\":\"core\",\"version\":\"1.0\"}]}");

        private static DataCache NewCache(int version, FactorioGameVersion? gameVersion) => CacheVersions.Create(version, gameVersion);

        private static void RemoveUserPresets() {
            foreach (string name in new[] { "Old Modpack", "Mystery Pack" })
                File.Delete(PresetProcessor.GetUserPresetPath(name, ".pjson"));
        }

        private static async Task<DataCache> TwoZeroCacheAsync() {
            if (sharedTwoZeroCache is not null)
                return sharedTwoZeroCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedTwoZeroCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(TwoZero, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedTwoZeroCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedTwoZeroCache;
        }

        private static async Task Warm(params Preset[] presets) {
            foreach (Preset preset in presets)
                await PresetHeaders.GetAsync(preset);
        }

        private static TextBlock Named(Visual root, string name) =>
            root.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);

        private static Color TokenColor(string key) {
            Assert.True(Avalonia.Application.Current!.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out object? value), key);
            return Assert.IsType<Color>(value);
        }

        private static void AssertSecondary(TextBlock text) {
            Assert.Equal(TokenColor("Foreman.Color.TextSecondary"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            Assert.Equal(TextTrimming.None, text.TextTrimming);
        }

        private static async Task<PresetComparatorWindow> ComparedAsync(DataCache left, DataCache right) {
            var presets = new List<Preset> { new("Left", true, true), new("Right", false, false) };
            var window = new PresetComparatorWindow(presets) {
                LoadCacheStub = preset => Task.FromResult(new DataLoadResult(preset.Name == "Left" ? left : right, DataLoadOutcome.Loaded)),
            };
            window.Show();
            await window.SimulateCompareClickAsync();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        [AvaloniaFact]
        public async Task Comparator_TwoOnePreset_ShowsGameVersion2_1_24() {
            var left = new DataCache(filterRecipes: true);
            await left.LoadAllData(new Preset(TwoZero, true, true), new Progress<KeyValuePair<int, string>>());
            var right = new DataCache(filterRecipes: true);
            await right.LoadAllData(new Preset(TwoOne, false, false), new Progress<KeyValuePair<int, string>>());
            var presets = new List<Preset> { new(TwoZero, true, true), new(TwoOne, false, false) };
            var window = new PresetComparatorWindow(presets) {
                LoadCacheStub = preset => Task.FromResult(new DataLoadResult(preset.Name == TwoZero ? left : right, DataLoadOutcome.Loaded)),
            };
            window.Show();
            await window.SimulateCompareClickAsync();
            Dispatcher.UIThread.RunJobs();

            TextBlock rightVersion = Named(window, "PART_RightVersion");
            TextBlock leftVersion = Named(window, "PART_LeftVersion");

            Assert.Equal("Factorio 2.1.24", rightVersion.Text);
            Assert.Equal("Factorio 2.0.76", leftVersion.Text);
            AssertSecondary(rightVersion);
            AssertSecondary(leftVersion);
            Assert.Equal("Factorio 2.1 Vanilla, Factorio 2.1.24", AutomationProperties.GetName(Named(window, "PART_RightHeader")));
            Assert.Equal("Factorio 2.0 Vanilla, Factorio 2.0.76", AutomationProperties.GetName(Named(window, "PART_LeftHeader")));
            window.Close();
        }

        [AvaloniaFact]
        public async Task Comparator_OutdatedPreset_HeaderSaysOutdated() {
            PresetComparatorWindow window = await ComparedAsync(NewCache(0, new FactorioGameVersion(2, 0, 28)), NewCache(1, new FactorioGameVersion(2, 1, 24)));

            Assert.Equal("Factorio 2.0.28 · Outdated", Named(window, "PART_LeftVersion").Text);
            Assert.Equal("Factorio 2.1.24", Named(window, "PART_RightVersion").Text);
            Assert.Equal("Left, Factorio 2.0.28, made by an older version of Foreman", AutomationProperties.GetName(Named(window, "PART_LeftHeader")));
            Assert.Equal("Left, Factorio 2.0.28, made by an older version of Foreman", ControlAutomationPeer.CreatePeerForElement(Named(window, "PART_LeftHeader")).GetName());
            window.Close();
        }

        [AvaloniaFact]
        public async Task Comparator_PresetWithoutVersion_HeaderSaysUnknownVersion() {
            PresetComparatorWindow window = await ComparedAsync(NewCache(1, null), NewCache(0, null));

            Assert.Equal("Unknown version", Named(window, "PART_LeftVersion").Text);
            Assert.Equal("Unknown version · Outdated", Named(window, "PART_RightVersion").Text);
            Assert.Equal("Left, unknown version", AutomationProperties.GetName(Named(window, "PART_LeftHeader")));
            Assert.Equal("Right, unknown version, made by an older version of Foreman", AutomationProperties.GetName(Named(window, "PART_RightHeader")));
            window.Close();
        }

        [AvaloniaFact]
        public async Task ComparatorLoad_OutdatedPreset_ShowsNoAlert() {
            var owner = new Window();
            owner.Show();
            DataLoadWindow window = PresetComparatorWindow.NewLoadWindow(new Preset("Old Modpack", false, false), (_, _, _, _) => Task.CompletedTask);
            window.Show(owner);

            await window.LoadTask;
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.OutdatedAlertTask);
            Assert.Empty(SheetHost.For(owner).GetVisualDescendants().OfType<AlertSheet>());
            owner.Close();
        }

        private static (Window Owner, DataLoadWindow Window) ShownLoad(string presetName, Func<Preset, DataCache, IProgress<KeyValuePair<int, string>>, CancellationToken, Task> load,
                Func<Window, string, string, Task>? showAlert = null) {
            var owner = new Window();
            owner.Show();
            var window = new DataLoadWindow(new Preset(presetName, false, false), true, DataLoadMode.AfterLaunch, null, load, showAlert);
            window.Show(owner);
            return (owner, window);
        }

        private static Func<Preset, DataCache, IProgress<KeyValuePair<int, string>>, CancellationToken, Task> LoadingAs(int version, int failFirst = 0) {
            int calls = 0;
            return (_, cache, _, _) => {
                if (++calls <= failFirst)
                    throw new InvalidOperationException("unreadable");
                typeof(DataCache).GetProperty(nameof(DataCache.Version))!.SetValue(cache, version);
                return Task.CompletedTask;
            };
        }

        [AvaloniaFact]
        public async Task PresetLoad_UnversionedPreset_ShowsOutdatedSentenceOnce() {
            (Window owner, DataLoadWindow window) = ShownLoad("Old Modpack", LoadingAs(0, failFirst: 1));
            await window.AttemptTask;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(SheetHost.For(owner).GetVisualDescendants().OfType<AlertSheet>());

            Button tryAgain = window.FailurePanelControl!.Buttons!.Items.OfType<Button>().Single(b => (string)b.Content! == "Try Again");
            tryAgain.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.LoadTask;
            Dispatcher.UIThread.RunJobs();

            AlertSheet alert = Assert.Single(SheetHost.For(owner).GetVisualDescendants().OfType<AlertSheet>());
            Assert.Equal("“Old Modpack” was made by an older version of Foreman.", alert.FrameControl.Headline);
            Assert.Equal(Sentence, alert.FrameControl.InformativeText);
            Button only = Assert.Single(alert.Buttons!.Items.OfType<Button>());
            Assert.Equal("OK", only.Content);
            Assert.NotNull(window.OutdatedAlertTask);
            owner.Close();
        }

        [AvaloniaFact]
        public async Task PresetLoad_CurrentPreset_ShowsNoAlert() {
            var shown = new List<string>();
            (Window owner, DataLoadWindow window) = ShownLoad(TwoOne, LoadingAs(1), (_, title, _) => { shown.Add(title); return Task.CompletedTask; });

            await window.LoadTask;
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(shown);
            Assert.Null(window.OutdatedAlertTask);
            owner.Close();
        }

        [AvaloniaFact]
        public async Task PresetLoad_Failed_ShowsNoAlert() {
            var shown = new List<string>();
            (Window owner, DataLoadWindow window) = ShownLoad("Old Modpack", LoadingAs(0, failFirst: 1), (_, title, _) => { shown.Add(title); return Task.CompletedTask; });

            await window.AttemptTask;
            Dispatcher.UIThread.RunJobs();
            window.FailurePanelControl!.Buttons!.Items.OfType<Button>().Single(b => (string)b.Content! == "Choose Another Preset…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.LoadTask;
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(shown);
            Assert.Null(window.OutdatedAlertTask);
            owner.Close();
        }

        [AvaloniaFact]
        public async Task WelcomeLoad_OutdatedPreset_ShowsOutdatedSentenceOnce() {
            using var library = new LibraryFixture();
            var settings = new AppSettings { CurrentPresetName = "Old Modpack" };
            var context = new ShellContext(library.Service, settings, new DataCache(false), null, null) { CacheLoaded = false };
            var gate = new TaskCompletionSource<DataCache?>();
            var shown = new List<(Window Owner, string Title, string Message)>();
            WelcomeLauncher.CacheLoaderOverride = _ => gate.Task;
            WelcomeLauncher.WarningOverride = (owner, title, message) => { shown.Add((owner, title, message)); return Task.CompletedTask; };
            ShellHost.Context = context;
            Preset preset = OldModpack();
            try {
                WelcomeWindow welcome = WelcomeLauncher.Show(context, preset)!;
                Task<bool> second = WelcomeLauncher.BuildHost(context, () => welcome).LoadPreset(preset, new Progress<KeyValuePair<int, string>>(), CancellationToken.None);

                gate.SetResult(NewCache(0, new FactorioGameVersion(2, 0, 28)));
                await welcome.ViewModel.LoadTask;
                await second;
                Dispatcher.UIThread.RunJobs();

                (Window owner, string title, string message) = Assert.Single(shown);
                Assert.Same(welcome, owner);
                Assert.Equal("“Old Modpack” was made by an older version of Foreman.", title);
                Assert.Equal(Sentence, message);
            } finally {
                WelcomeLauncher.Current?.Close();
                WelcomeLauncher.CacheLoaderOverride = null;
                WelcomeLauncher.WarningOverride = null;
                ShellHost.Context = null;
                ShellHost.ResetQueue();
                RemoveUserPresets();
            }
        }

        private static async Task<SettingsWindow> ShownSettingsAsync(Preset current, params Preset[] others) {
            await Warm([current, .. others]);
            var options = new SettingsWindow.SettingsWindowOptions(await TwoZeroCacheAsync()) { Presets = [current, .. others], SelectedPreset = current };
            var window = new SettingsWindow(options);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static ListBoxItem RowFor(ListBox list, string name) =>
            list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == name));

        [AvaloniaFact]
        public async Task PresetsTab_Rows_ShowGameVersion() {
            var current = new Preset(TwoZero, true, true);
            SettingsWindow window = await ShownSettingsAsync(current, new Preset(TwoOne, false, false));

            ListBoxItem row = RowFor(window.PresetsTabControl.PresetListBoxControl, TwoOne);

            TextBlock caption = Named(row, "PresetVersionText");
            Assert.Equal("2.1.24", caption.Text);
            AssertSecondary(caption);
            Assert.Equal("Factorio 2.1 Vanilla, Factorio 2.1.24", AutomationProperties.GetName(row));
            window.Close();
        }

        [AvaloniaFact]
        public async Task PresetsTab_OutdatedRow_SaysOutdated() {
            try {
                Preset old = OldModpack();
                SettingsWindow window = await ShownSettingsAsync(new Preset(TwoZero, true, true), old);

                ListBoxItem row = RowFor(window.PresetsTabControl.PresetListBoxControl, "Old Modpack");

                Assert.Equal("2.0.28 · Outdated", Named(row, "PresetVersionText").Text);
                Assert.Equal("Old Modpack, Factorio 2.0.28, made by an older version of Foreman", AutomationProperties.GetName(row));
                window.Close();
            } finally {
                RemoveUserPresets();
            }
        }

        [AvaloniaFact]
        public async Task PresetsTab_CurrentPreset_ShowsVersion() {
            SettingsWindow window = await ShownSettingsAsync(new Preset(TwoZero, true, true), new Preset(TwoOne, false, false));

            TextBlock caption = Named(window.PresetsTabControl, "CurrentPresetVersionText");

            Assert.Equal("2.0.76", caption.Text);
            AssertSecondary(caption);
            Assert.Equal("Factorio 2.0 Vanilla, Factorio 2.0.76", AutomationProperties.GetName(window.PresetsTabControl.CurrentPresetLabelControl));
            window.Close();
        }

        [AvaloniaFact]
        public async Task Welcome_PresetRows_ShowGameVersion() {
            try {
                using var library = new LibraryFixture();
                Preset twoOne = new(TwoOne, false, false);
                Preset old = OldModpack();
                Preset mystery = MysteryPack();
                await Warm(twoOne, old, mystery);
                var host = new WelcomeHost {
                    Library = library.Service,
                    Settings = new AppSettings { CurrentPresetName = TwoOne },
                    SaveSettings = () => { },
                    InstalledPresets = () => [twoOne, old, mystery],
                    LoadPreset = (_, _, _) => Task.FromResult(true),
                    NewGraph = () => Task.FromResult(true),
                    OpenFile = () => Task.FromResult(false),
                    OpenGraph = (_, _) => Task.FromResult(true),
                    ExportGraph = _ => Task.CompletedTask,
                    RatePresets = _ => Task.FromResult(new List<PresetErrorPackage>()),
                    HasOpenGraph = () => false,
                };
                var window = new WelcomeWindow(host);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                ListBoxItem current = RowFor(window.PresetList, TwoOne);
                ListBoxItem outdated = RowFor(window.PresetList, "Old Modpack");
                ListBoxItem unknown = RowFor(window.PresetList, "Mystery Pack");

                Assert.Equal("2.1.24", Named(current, "PresetVersionText").Text);
                AssertSecondary(Named(current, "PresetVersionText"));
                Assert.Equal("Factorio 2.1 Vanilla, Factorio 2.1.24", AutomationProperties.GetName(current));
                Assert.Equal("2.0.28 · Outdated", Named(outdated, "PresetVersionText").Text);
                Assert.Equal("Old Modpack, Factorio 2.0.28, made by an older version of Foreman", AutomationProperties.GetName(outdated));
                Assert.Equal("Outdated", Named(unknown, "PresetVersionText").Text);
                Assert.Equal("Mystery Pack, made by an older version of Foreman", AutomationProperties.GetName(unknown));
                window.Close();
                WelcomeWindow.ForgetSize();
            } finally {
                RemoveUserPresets();
            }
        }

        [AvaloniaFact]
        public async Task WelcomeLoad_ReopenPath_PresentsTheAlertOnceTheMainWindowShows() {
            using var library = new LibraryFixture();
            var settings = new AppSettings { CurrentPresetName = TwoZero };
            var context = new ShellContext(library.Service, settings, new DataCache(false), null, new PresetResolver()) { CacheLoaded = false };
            var shown = new List<(Window Owner, string Title)>();
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(NewCache(0, new FactorioGameVersion(2, 0, 28)));
            WelcomeLauncher.WarningOverride = (owner, title, _) => { shown.Add((owner, title)); return Task.CompletedTask; };
            ShellHost.Context = context;
            MainWindow? window = null;
            try {
                Assert.True(await WelcomeLauncher.EnsureLoadedAsync(context));
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(shown);

                window = WelcomeLauncher.EnsureWindow(context, deferShow: true);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(shown);
                window.RevealDeferred();
                Dispatcher.UIThread.RunJobs();

                (Window owner, string title) = Assert.Single(shown);
                Assert.Same(window, owner);
                Assert.Equal("“Factorio 2.0 Vanilla” was made by an older version of Foreman.", title);
                Assert.Null(context.PendingOutdatedAlert);
            } finally {
                window?.Close();
                WelcomeLauncher.Current?.Close();
                WelcomeLauncher.CacheLoaderOverride = null;
                WelcomeLauncher.WarningOverride = null;
                ShellHost.Context = null;
                ShellHost.ResetQueue();
            }
        }

        [AvaloniaFact]
        public async Task WelcomeLoad_SupersededLoad_PresentsNoAlert() {
            using var library = new LibraryFixture();
            var context = new ShellContext(library.Service, new AppSettings(), new DataCache(false), null, null) { CacheLoaded = false };
            var shown = new List<string>();
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(NewCache(0, null));
            WelcomeLauncher.WarningOverride = (_, title, _) => { shown.Add(title); return Task.CompletedTask; };
            try {
                await WelcomeLauncher.BuildHost(context, () => null).LoadPreset(new Preset(TwoZero, false, false), new Progress<KeyValuePair<int, string>>(), CancellationToken.None);
                context.LoadGeneration++;
                Dispatcher.UIThread.RunJobs();

                Assert.Empty(shown);
                Assert.Null(context.PendingOutdatedAlert);
            } finally {
                WelcomeLauncher.CacheLoaderOverride = null;
                WelcomeLauncher.WarningOverride = null;
            }
        }

        [AvaloniaFact]
        public async Task WelcomeLoad_CurrentPresetLoadedAfterAnOutdatedOne_LeavesNoPendingAlert() {
            using var library = new LibraryFixture();
            var context = new ShellContext(library.Service, new AppSettings(), new DataCache(false), null, null) { CacheLoaded = false };
            var shown = new List<string>();
            WelcomeLauncher.CacheLoaderOverride = preset => Task.FromResult<DataCache?>(NewCache(preset.Name == TwoZero ? 0 : PresetExportFormat.CurrentVersion, null));
            WelcomeLauncher.WarningOverride = (_, title, _) => { shown.Add(title); return Task.CompletedTask; };
            try {
                var host = WelcomeLauncher.BuildHost(context, () => null);
                await host.LoadPreset(new Preset(TwoZero, false, false), new Progress<KeyValuePair<int, string>>(), CancellationToken.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(TwoZero, context.PendingOutdatedAlert?.Preset.Name);

                await host.LoadPreset(new Preset("Current", false, false), new Progress<KeyValuePair<int, string>>(), CancellationToken.None);
                Dispatcher.UIThread.RunJobs();

                Assert.Null(context.PendingOutdatedAlert);
                Assert.Empty(shown);
            } finally {
                WelcomeLauncher.CacheLoaderOverride = null;
                WelcomeLauncher.WarningOverride = null;
            }
        }

        [AvaloniaFact]
        public async Task PresetsTab_LongCurrentPresetName_TrimsInsteadOfOverflowing() {
            var current = new Preset(new string('x', 160), true, true);
            var options = new SettingsWindow.SettingsWindowOptions(await TwoZeroCacheAsync()) { Presets = [current], SelectedPreset = current };
            var window = new SettingsWindow(options);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBlock label = window.PresetsTabControl.CurrentPresetLabelControl;
            TextBlock caption = window.PresetsTabControl.CurrentPresetVersionTextControl;

            double right = label.TranslatePoint(new Point(label.Bounds.Width, 0), window)!.Value.X;

            Assert.True(right <= window.Bounds.Width, $"label ends at {right} in a {window.Bounds.Width} wide window");
            Assert.Equal(VerticalAlignment.Bottom, caption.VerticalAlignment);
            window.Close();
        }

        [AvaloniaFact]
        public async Task PresetHeaders_TwoRequestsForOnePreset_ShareOneRead() {
            var preset = new Preset("Shared " + Guid.NewGuid().ToString("N"), false, false);

            Task<PresetHeader?> first = PresetHeaders.GetAsync(preset);
            Task<PresetHeader?> second = PresetHeaders.GetAsync(preset);

            Assert.Same(first, second);
            Assert.Null(await first);
        }
    }
}
