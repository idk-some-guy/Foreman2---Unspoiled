using Avalonia.Headless.XUnit;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Models;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Views.Sheets;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class PresetResolverTests {
        private static string NewPresetsDirectory() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Factorio 2.0 Vanilla.pjson"), "");
            File.WriteAllText(Path.Combine(dir, "Factorio 2.0 Vanilla.dat"), "");
            File.WriteAllText(Path.Combine(dir, "Factorio 2.1 Vanilla.pjson"), "");
            File.WriteAllText(Path.Combine(dir, "Factorio 2.1 Vanilla.dat"), "");
            File.WriteAllText(Path.Combine(dir, "Some Other Preset.pjson"), "");
            File.WriteAllText(Path.Combine(dir, "Some Other Preset.dat"), "");
            return dir;
        }

        [Fact]
        public void FreshSettings_ResolveToFactorio21Vanilla() {
            var resolver = new PresetResolver(NewPresetsDirectory(), NewEmptyDirectory());

            var preset = resolver.Resolve(null);

            Assert.Equal("Factorio 2.1 Vanilla", preset.Name);
            Assert.True(preset.IsDefaultPreset);
        }

        [Fact]
        public void SettingsNamingFactorio20Vanilla_KeepIt() {
            var resolver = new PresetResolver(NewPresetsDirectory(), NewEmptyDirectory());

            var preset = resolver.Resolve("Factorio 2.0 Vanilla");

            Assert.Equal("Factorio 2.0 Vanilla", preset.Name);
            Assert.False(preset.IsDefaultPreset);
        }

        [Fact]
        public void Resolve_ValidPresetName_KeepsIt() {
            var resolver = new PresetResolver(NewPresetsDirectory());

            var preset = resolver.Resolve("Some Other Preset");

            Assert.Equal("Some Other Preset", preset.Name);
            Assert.False(preset.IsDefaultPreset);
        }

        [Fact]
        public void Resolve_BogusPresetName_FallsBackToVanilla() {
            var resolver = new PresetResolver(NewPresetsDirectory());

            var preset = resolver.Resolve("Nonexistent Preset");

            Assert.Equal(PresetResolver.DefaultPresetName, preset.Name);
            Assert.True(preset.IsDefaultPreset);
        }

        [Fact]
        public void Resolve_EmptyPresetName_FallsBackToVanilla() {
            var resolver = new PresetResolver(NewPresetsDirectory());

            var preset = resolver.Resolve("");

            Assert.Equal(PresetResolver.DefaultPresetName, preset.Name);
        }

        [Fact]
        public void Resolve_BogusPresetNameAndDefaultMissingOnDisk_ThrowsDefaultPresetUnavailableException() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            var resolver = new PresetResolver(dir);

            var ex = Assert.Throws<DefaultPresetUnavailableException>(() => resolver.Resolve("Anything"));

            Assert.Contains(PresetResolver.DefaultPresetName, ex.Message);
            Assert.Contains("re-install", ex.Message);
        }

        [Fact]
        public void Resolve_DefaultNameRequestedButMissingOnDisk_ThrowsDefaultPresetUnavailableException() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            var resolver = new PresetResolver(dir);

            Assert.Throws<DefaultPresetUnavailableException>(() => resolver.Resolve(PresetResolver.DefaultPresetName));
        }

        //---- dual-directory read (docs/upstream-divergences.md file-location policy, io-reference.md §7) --

        private static string NewEmptyDirectory() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WritePresetFiles(string dir, string name) {
            File.WriteAllText(Path.Combine(dir, name + ".pjson"), "");
            File.WriteAllText(Path.Combine(dir, name + ".dat"), "");
        }

        [Fact]
        public void Resolve_PresetOnlyInUserDirectory_IsFound() {
            string bundleDir = NewPresetsDirectory();
            string userDir = NewEmptyDirectory();
            WritePresetFiles(userDir, "Imported Preset");
            var resolver = new PresetResolver(bundleDir, userDir);

            var preset = resolver.Resolve("Imported Preset");

            Assert.Equal("Imported Preset", preset.Name);
        }

        [Fact]
        public void BuildPresetList_PresetsFromBothDirectories_AreMergedAndDeduplicated() {
            string bundleDir = NewPresetsDirectory(); //ships both Vanilla presets + "Some Other Preset"
            string userDir = NewEmptyDirectory();
            WritePresetFiles(userDir, "Imported Preset");
            WritePresetFiles(userDir, "Factorio 2.0 Vanilla"); //also present in bundleDir - must not duplicate
            var resolver = new PresetResolver(bundleDir, userDir);

            var names = resolver.BuildPresetList("Factorio 2.0 Vanilla").ConvertAll(p => p.Name);

            Assert.Equal(4, names.Count);
            Assert.Contains("Imported Preset", names);
            Assert.Contains("Some Other Preset", names);
            Assert.Single(names, n => n == "Factorio 2.0 Vanilla");
        }

        [Fact]
        public void Resolve_SameNameInBothDirectories_UserDirectoryWins() {
            //GetExistingPresetNames only proves the name is visible once; PresetProcessor.GetPresetPath
            //(Foreman.Core) is what actually prefers the user copy's file content on a name collision.
            string bundleDir = NewPresetsDirectory();
            string userDir = NewEmptyDirectory();
            WritePresetFiles(userDir, "Factorio 2.0 Vanilla");
            var resolver = new PresetResolver(bundleDir, userDir);

            var names = resolver.BuildPresetList("Factorio 2.0 Vanilla").ConvertAll(p => p.Name);

            Assert.Single(names, n => n == "Factorio 2.0 Vanilla");
        }
    }

    public class StartupTests {
        private static readonly string[] RetiredLoadAlerts = ["Preset load interrupted", "is corrupt. Switching to the default preset", "No Preset is loaded!"];

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private static Task Bounded(Task task) => task.WaitAsync(Timeout);

        private static Task<T> Bounded<T>(Task<T> task) => task.WaitAsync(Timeout);

        private static string NewTempHome() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(home);
            return home;
        }

        private static string NewPresetsDirectoryWithVanillaAnd(string otherName) {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Factorio 2.0 Vanilla.pjson"), "");
            File.WriteAllText(Path.Combine(dir, "Factorio 2.0 Vanilla.dat"), "");
            File.WriteAllText(Path.Combine(dir, "Factorio 2.1 Vanilla.pjson"), "");
            File.WriteAllText(Path.Combine(dir, "Factorio 2.1 Vanilla.dat"), "");
            File.WriteAllText(Path.Combine(dir, otherName + ".pjson"), "");
            File.WriteAllText(Path.Combine(dir, otherName + ".dat"), "");
            return dir;
        }

        private static AppSettings Factorio20Settings(SettingsService settingsService) {
            AppSettings settings = settingsService.Load();
            settings.CurrentPresetName = "Factorio 2.0 Vanilla";
            return settings;
        }

        private static AppSettings WelcomeOff(SettingsService settingsService) {
            AppSettings settings = Factorio20Settings(settingsService);
            settings.ShowWelcomeAtLaunch = false;
            return settings;
        }

        private static void CloseWelcome() {
            WelcomeLauncher.Current?.Close();
            WelcomeLauncher.CacheLoaderOverride = null;
            WelcomeLauncher.WarningOverride = null;
            ShellHost.Context = null;
            ShellHost.ResetQueue();
        }

        private static async Task<string> EmptyVanillaGraphJsonAsync() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset("Factorio 2.0 Vanilla", true, true), new Progress<KeyValuePair<int, string>>());
            return Foreman.Serialization.GraphSaveCodec.WriteViewerDocumentToString(new Foreman.Serialization.GraphViewerSaveDocument {
                Version = Foreman.Serialization.GraphSaveFormat.SaveFormatVersion,
                SavedPresetName = cache.PresetName,
                IncludedMods = new Dictionary<string, string>(cache.IncludedMods),
                ProductionGraph = Foreman.Serialization.GraphSaveCodec.BuildProductionGraph(new Foreman.ProductionGraph()),
            });
        }

        private static PresetResolver ResolverWithoutUserPresets() => new(presetsDirectoryOverride: null, userPresetsDirectoryOverride: NewTempHome());

        private static void CloseWithoutWelcome(MainWindow window) {
            window.Settings!.ShowWelcomeAtLaunch = false;
            window.Close();
            if (ShellHost.Context?.Window == window)
                ShellHost.Context = null;
        }

        [AvaloniaFact]
        public async Task BootAsync_DefaultPresetMissingFromDisk_SignalsFatalExitWithoutLoading() {
            var settingsService = new SettingsService(NewTempHome());
            string emptyPresetsDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyPresetsDir);
            bool fatalExitCalled = false;
            bool loadAttempted = false;

            var window = await Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                settingsService.Load(),
                new PresetResolver(emptyPresetsDir),
                (_, _, _) => { loadAttempted = true; return Task.FromResult<(DataCache, Preset)?>(null); },
                (_, _, _) => Task.CompletedTask,
                () => fatalExitCalled = true));

            Assert.Null(window);
            Assert.True(fatalExitCalled);
            Assert.False(loadAttempted);
        }

        [AvaloniaFact]
        public async Task BootAsync_LaunchWindowQuits_SignalsFatalExit_AndRaisesNoWarning() {
            var settingsService = new SettingsService(NewTempHome());
            string presetsDir = NewPresetsDirectoryWithVanillaAnd("Custom Preset");
            var warnings = new List<string>();
            bool fatalExitCalled = false;

            var window = await Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                WelcomeOff(settingsService),
                new PresetResolver(presetsDir),
                (_, _, _) => Task.FromResult<(DataCache, Preset)?>(null),
                (_, _, message) => { warnings.Add(message); return Task.CompletedTask; },
                () => fatalExitCalled = true));

            Assert.Null(window);
            Assert.True(fatalExitCalled);
            Assert.Empty(warnings);
        }

        [AvaloniaFact]
        public async Task BootAsync_WelcomeOn_ShowsOnlyTheWelcomeWindow_LoadsThePresetInline_NoDataLoadWindow() {
            var settingsService = new SettingsService(NewTempHome());
            AppSettings settings = settingsService.Load();
            var cache = CacheVersions.Create(PresetExportFormat.CurrentVersion, filterRecipes: true);
            var gate = new TaskCompletionSource<DataCache?>();
            WelcomeLauncher.CacheLoaderOverride = _ => gate.Task;
            bool dataLoadUsed = false;
            MainWindow? before = MainWindow.MostRecentlyOpened;
            try {
                MainWindow? window = await Bounded(ShellBootstrapper.BootAsync(
                    settingsService,
                    settings,
                    ResolverWithoutUserPresets(),
                    (_, _, _) => { dataLoadUsed = true; return Task.FromResult<(DataCache, Preset)?>(null); },
                    (_, _, _) => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask));

                Assert.Null(window);
                WelcomeWindow welcome = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Assert.True(welcome.IsVisible);
                Assert.Null(ShellHost.Context!.Window);
                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Same(before, MainWindow.MostRecentlyOpened);
                Assert.Equal(WelcomeLoadPhase.Loading, welcome.ViewModel.Phase);
                Assert.False(welcome.NewGraphButton.IsEnabled);
                Assert.True(welcome.LoadingStrip.IsVisible);
                Assert.Contains("Factorio 2.1 Vanilla", welcome.ViewModel.LoadSentence);

                gate.SetResult(cache);
                await welcome.ViewModel.LoadTask;

                Assert.Equal(WelcomeLoadPhase.Idle, welcome.ViewModel.Phase);
                Assert.True(welcome.NewGraphButton.IsEnabled);
                Assert.Same(cache, ShellHost.Context.Cache);
                Assert.Same(before, MainWindow.MostRecentlyOpened);
                Assert.Equal("Factorio 2.1 Vanilla", settingsService.Load().CurrentPresetName);
                Assert.False(dataLoadUsed);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task BootAsync_WelcomeOn_PresetFailure_InlineErrorWithTryAgain_NoUseDefaultPreset() {
            var settingsService = new SettingsService(NewTempHome());
            bool fail = true;
            var cache = CacheVersions.Create(PresetExportFormat.CurrentVersion, filterRecipes: true);
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(fail ? null : cache);
            bool dataLoadUsed = false;
            bool fatalExit = false;
            var warnings = new List<string>();
            try {
                await Bounded(ShellBootstrapper.BootAsync(
                    settingsService,
                    settingsService.Load(),
                    ResolverWithoutUserPresets(),
                    (_, _, _) => { dataLoadUsed = true; return Task.FromResult<(DataCache, Preset)?>(null); },
                    (_, _, message) => { warnings.Add(message); return Task.CompletedTask; },
                    () => fatalExit = true,
                    () => Task.CompletedTask));
                WelcomeWindow welcome = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                await welcome.ViewModel.LoadTask;

                Assert.Equal(WelcomeLoadPhase.Failed, welcome.ViewModel.Phase);
                Assert.True(welcome.ErrorPanel.IsVisible);
                Assert.Equal("Couldn’t load “Factorio 2.1 Vanilla”", welcome.ErrorPanel.Headline);
                Button tryAgain = welcome.ErrorPanel.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Try Again"));
                Assert.True(tryAgain.IsDefault);
                Assert.DoesNotContain(welcome.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Use Default Preset"));
                Assert.False(welcome.NewGraphButton.IsEnabled);
                Assert.NotEmpty(welcome.PresetList.ItemsSource!.Cast<Preset>());
                Assert.False(dataLoadUsed);
                Assert.False(fatalExit);
                Assert.Empty(warnings);

                fail = false;
                await welcome.ViewModel.TryAgainAsync();

                Assert.Equal(WelcomeLoadPhase.Idle, welcome.ViewModel.Phase);
                Assert.False(welcome.ErrorPanel.IsVisible);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task BootAsync_WelcomeOff_ReopensTheMostRecentGraph_ThroughTheAtLaunchDataLoadWindow() {
            var settingsService = new SettingsService(NewTempHome());
            AppSettings settings = WelcomeOff(settingsService);
            const string vanilla = "Factorio 2.0 Vanilla";
            const string spaceAge = "Factorio 2.0 Space Age";
            string vanillaJson = await EmptyVanillaGraphJsonAsync();
            var spaceAgeCache = new DataCache(filterRecipes: true);
            await spaceAgeCache.LoadAllData(new Preset(spaceAge, false, false), new Progress<KeyValuePair<int, string>>());
            string spaceAgeJson = Foreman.Serialization.GraphSaveCodec.WriteViewerDocumentToString(new Foreman.Serialization.GraphViewerSaveDocument {
                Version = Foreman.Serialization.GraphSaveFormat.SaveFormatVersion,
                SavedPresetName = spaceAge,
                IncludedMods = new Dictionary<string, string>(spaceAgeCache.IncludedMods),
                ProductionGraph = Foreman.Serialization.GraphSaveCodec.BuildProductionGraph(new Foreman.ProductionGraph()),
            });
            var store = new GraphLibraryStore(settingsService.DatabasePath);
            DateTime now = DateTime.UtcNow;
            GraphRow older = store.Create("Older", vanilla, vanillaJson, 0, null, now.AddHours(-2));
            GraphRow newest = store.Create("Newest", spaceAge, spaceAgeJson, 0, null, now.AddHours(-1));
            Preset? requested = null;

            MainWindow? window = await Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                settings,
                ResolverWithoutUserPresets(),
                (preset, fallback, filter) => {
                    requested = preset;
                    return ShellBootstrapper.LoadPresetAsync(preset, fallback, filter);
                },
                (_, _, _) => Task.CompletedTask,
                () => { },
                () => Task.CompletedTask));

            Assert.NotNull(window);
            Assert.Equal(spaceAge, requested!.Name);
            Assert.Equal(spaceAge, window.DataCache!.PresetName);
            Assert.Equal(newest.Id, window.Library!.OpenGraphId);
            Assert.NotEqual(older.Id, window.Library.OpenGraphId);
            Assert.Equal("Newest", window.ViewModel.DocumentName);
            Assert.Equal(2, window.Library.All().Count);
            Assert.Null(WelcomeLauncher.Current);
            CloseWithoutWelcome(window);
        }

        [AvaloniaFact]
        public async Task BootAsync_WelcomeOff_AFailingMostRecentGraph_AlertsOnce_AndLandsOnAnUntitledGraph() {
            var settingsService = new SettingsService(NewTempHome());
            AppSettings settings = WelcomeOff(settingsService);
            var store = new GraphLibraryStore(settingsService.DatabasePath);
            GraphRow failing = store.Create("Broken", "Factorio 2.0 Vanilla", "{}", 0, null, DateTime.UtcNow.AddHours(-1));
            DataCache cache = await LoadedVanillaCacheAsync();

            Task<MainWindow?> booting = Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                settings,
                ResolverWithoutUserPresets(),
                (preset, _, _) => Task.FromResult<(DataCache, Preset)?>((cache, preset)),
                (_, _, _) => Task.CompletedTask,
                () => { },
                () => Task.CompletedTask));
            await WaitUntilAsync(() => MainWindow.MostRecentlyOpened is { } opened
                && SheetHost.For(opened).GetVisualDescendants().OfType<AlertSheet>().Any());
            MainWindow shown = MainWindow.MostRecentlyOpened!;
            AlertSheet alert = Assert.Single(SheetHost.For(shown).GetVisualDescendants().OfType<AlertSheet>());
            Assert.Equal("Couldn’t open “Broken”.", alert.FrameControl.Headline);
            alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            MainWindow? window = await booting;

            Assert.Same(shown, window);
            Assert.Equal("Untitled Graph", window!.ViewModel.DocumentName);
            Assert.NotNull(window.Library!.OpenGraphId);
            Assert.NotEqual(failing.Id, window.Library.OpenGraphId);
            Assert.NotNull(window.Library.Find(failing.Id));
            CloseWithoutWelcome(window);
        }

        [AvaloniaFact]
        public async Task BootAsync_WelcomeOff_EmptyLibrary_OpensUntitledGraph() {
            var settingsService = new SettingsService(NewTempHome());
            AppSettings settings = WelcomeOff(settingsService);
            settings.CurrentPresetName = "Custom Preset";
            settings.IconsSize = 200;
            DataCache vanillaCache = await LoadedVanillaCacheAsync();
            (Preset Requested, Preset? Fallback)? call = null;
            var warnings = new List<string>();

            MainWindow? window = await Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                settings,
                new PresetResolver(NewPresetsDirectoryWithVanillaAnd("Custom Preset"), NewTempHome()),
                (requested, fallback, _) => {
                    call = (requested, fallback);
                    return Task.FromResult<(DataCache, Preset)?>((vanillaCache, fallback!));
                },
                (_, _, message) => { warnings.Add(message); return Task.CompletedTask; },
                () => { },
                () => Task.CompletedTask));

            Assert.NotNull(window);
            Assert.Equal("Custom Preset", call!.Value.Requested.Name);
            Assert.Equal("Factorio 2.1 Vanilla", call.Value.Fallback!.Name);
            Assert.Empty(warnings);
            Assert.Equal("Untitled Graph", window.ViewModel.DocumentName);
            Assert.True(window.DataCache!.AvailableRecipes.Any());
            GraphRow row = Assert.Single(window.Library!.All());
            Assert.Equal(row.Id, window.Library.OpenGraphId);
            GraphRow probe = new GraphLibraryStore(settingsService.DatabasePath).Create("Probe", "p", "d", 0, null, DateTime.UtcNow);
            Assert.Contains(window.Library.All(), r => r.Id == probe.Id);
            Assert.Null(WelcomeLauncher.Current);
            CloseWithoutWelcome(window);

            AppSettings reloaded = settingsService.Load();
            Assert.Equal(200, reloaded.IconsSize);
            Assert.Equal("Factorio 2.1 Vanilla", reloaded.CurrentPresetName);
            Assert.Null(ShellHost.Context);
            Assert.False(ShellHost.HasLiveMainWindow);
        }

        private static async Task<DataCache> LoadedVanillaCacheAsync() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset("Factorio 2.0 Vanilla", true, true), new Progress<KeyValuePair<int, string>>());
            return cache;
        }

        private static async Task WaitUntilAsync(Func<bool> condition) {
            for (int i = 0; i < 400 && !condition(); i++) {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Assert.True(condition());
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_OpensEachFjsonFileAsALibraryGraph_LikeOpen_AndIgnoresOtherFiles() {
            using var library = new LibraryFixture();
            DataCache cache = await LoadedVanillaCacheAsync();
            MainWindow window = library.Track(new MainWindow());
            window.Show();
            window.GraphCanvas.Viewport.SetSize(800, 800);
            window.DataCache = cache;
            window.Settings = new AppSettings { CurrentPresetName = cache.PresetName!, ShowWelcomeAtLaunch = false };
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: library.NewTempDir());
            window.SettingsService = new SettingsService(library.NewTempDir());
            window.Library = library.Service;
            window.ApplyLoadedSettings();
            await window.NewGraphAsync();
            ShellHost.Context = window.ShellContext;
            try {
                GraphRow current = library.Service.Find(library.Service.OpenGraphId!.Value)!;
                string currentData = current.Data;
                int currentNodes = current.NodeCount;
                string json = await EmptyVanillaGraphJsonAsync();
                string dir = library.NewTempDir();
                string first = Path.Combine(dir, "first.fjson");
                string second = Path.Combine(dir, "second.fjson");
                string other = Path.Combine(dir, "notes.txt");
                string missing = Path.Combine(dir, "missing.fjson");
                File.WriteAllText(first, json);
                File.WriteAllText(second, json);
                File.WriteAllText(other, "text");

                Task opening = Bounded(ShellHost.OpenFilesAsync([first, other, missing, second]));
                await WaitUntilAsync(() => opening.IsCompleted || SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Any());
                AlertSheet alert = Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
                Assert.Equal("Couldn’t open “missing”.", alert.FrameControl.Headline);
                alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await opening;

                IReadOnlyList<GraphRow> rows = library.Service.All();
                Assert.Equal(3, rows.Count);
                GraphRow firstRow = Assert.Single(rows, r => r.Name == "first");
                GraphRow secondRow = Assert.Single(rows, r => r.Name == "second");
                Assert.Equal(first, firstRow.SourcePath);
                Assert.Equal(second, secondRow.SourcePath);
                Assert.Equal(secondRow.Id, library.Service.OpenGraphId);
                GraphRow after = library.Service.Find(current.Id)!;
                Assert.Equal(currentData, after.Data);
                Assert.Equal(currentNodes, after.NodeCount);

                await Bounded(ShellHost.OpenFilesAsync([first]));

                Assert.Equal(3, library.Service.All().Count);
                Assert.Equal(firstRow.Id, library.Service.OpenGraphId);
            } finally {
                ShellHost.Context = null;
                ShellHost.ResetQueue();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_BeforeTheShellIsReady_QueuesTheFiles_AndOpensThemOncePresetLoads() {
            var settingsService = new SettingsService(NewTempHome());
            string dir = NewTempHome();
            string early = Path.Combine(dir, "early.fjson");
            string loading = Path.Combine(dir, "loading.fjson");
            string json = await EmptyVanillaGraphJsonAsync();
            File.WriteAllText(early, json);
            File.WriteAllText(loading, json);
            DataCache cache = await LoadedVanillaCacheAsync();
            var gate = new TaskCompletionSource<DataCache?>();
            WelcomeLauncher.CacheLoaderOverride = _ => gate.Task;
            var store = new GraphLibraryStore(settingsService.DatabasePath);
            try {
                await Bounded(ShellHost.OpenFilesAsync([early]));
                Assert.Empty(store.ListByLastOpened());

                await Bounded(ShellBootstrapper.BootAsync(
                    settingsService,
                    Factorio20Settings(settingsService),
                    ResolverWithoutUserPresets(),
                    (_, _, _) => Task.FromResult<(DataCache, Preset)?>(null),
                    (_, _, _) => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask));
                WelcomeWindow welcome = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Assert.Equal(WelcomeLoadPhase.Loading, welcome.ViewModel.Phase);
                await Bounded(ShellHost.OpenFilesAsync([loading]));
                Assert.Empty(store.ListByLastOpened());

                gate.SetResult(cache);
                await WaitUntilAsync(() => WelcomeLauncher.Current is null && ShellHost.HasLiveMainWindow && store.ListByLastOpened().Count == 2);

                IReadOnlyList<GraphRow> rows = store.ListByLastOpened();
                GraphRow earlyRow = Assert.Single(rows, r => r.Name == "early");
                GraphRow loadingRow = Assert.Single(rows, r => r.Name == "loading");
                Assert.Equal(early, earlyRow.SourcePath);
                Assert.Equal(loading, loadingRow.SourcePath);
                Assert.Equal(loadingRow.Id, ShellHost.Context!.Library.OpenGraphId);
                Assert.Null(WelcomeLauncher.Current);
                CloseWithoutWelcome(ShellHost.Context.Window!);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_WhenTheWelcomeClosesWhileItLoads_WaitsForTheLoadedCache() {
            var settingsService = new SettingsService(NewTempHome());
            string file = Path.Combine(NewTempHome(), "dropped.fjson");
            File.WriteAllText(file, await EmptyVanillaGraphJsonAsync());
            DataCache cache = await LoadedVanillaCacheAsync();
            var gate = new TaskCompletionSource<DataCache?>();
            WelcomeLauncher.CacheLoaderOverride = _ => gate.Task;
            var store = new GraphLibraryStore(settingsService.DatabasePath);
            try {
                await Bounded(ShellBootstrapper.BootAsync(
                    settingsService,
                    Factorio20Settings(settingsService),
                    ResolverWithoutUserPresets(),
                    (_, _, _) => Task.FromResult<(DataCache, Preset)?>(null),
                    (_, _, _) => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask));
                WelcomeWindow welcome = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Assert.Equal(WelcomeLoadPhase.Loading, welcome.ViewModel.Phase);
                welcome.Close();

                await Bounded(ShellHost.OpenFilesAsync([file]));
                Assert.Empty(store.ListByLastOpened());
                Assert.False(ShellHost.HasLiveMainWindow);

                gate.SetResult(cache);
                await WaitUntilAsync(() => ShellHost.HasLiveMainWindow && store.ListByLastOpened().Count == 1);

                GraphRow row = Assert.Single(store.ListByLastOpened());
                Assert.Equal("dropped", row.Name);
                Assert.Equal(row.Id, ShellHost.Context!.Library.OpenGraphId);
                Assert.Same(cache, ShellHost.Context.Window!.DataCache);
                CloseWithoutWelcome(ShellHost.Context.Window);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_WhenTheLaunchLoadFailedAndTheWelcomeReopens_HoldsTheFiles() {
            var settingsService = new SettingsService(NewTempHome());
            string file = Path.Combine(NewTempHome(), "dropped.fjson");
            File.WriteAllText(file, await EmptyVanillaGraphJsonAsync());
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(null);
            var store = new GraphLibraryStore(settingsService.DatabasePath);
            try {
                await Bounded(ShellBootstrapper.BootAsync(
                    settingsService,
                    Factorio20Settings(settingsService),
                    ResolverWithoutUserPresets(),
                    (_, _, _) => Task.FromResult<(DataCache, Preset)?>(null),
                    (_, _, _) => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask));
                WelcomeWindow failed = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                await failed.ViewModel.LoadTask;
                Assert.Equal(WelcomeLoadPhase.Failed, failed.ViewModel.Phase);
                failed.Close();
                await Bounded(ShellHost.OpenFilesAsync([file]));

                await ShellHost.ReopenAsync();

                WelcomeWindow reopened = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Assert.False(reopened.ViewModel.ActionsEnabled);
                Assert.Empty(store.ListByLastOpened());
                Assert.False(ShellHost.HasLiveMainWindow);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_BeforeAWelcomeOffBoot_OpensTheFileOnceTheLaunchGraphIsUp() {
            var settingsService = new SettingsService(NewTempHome());
            AppSettings settings = WelcomeOff(settingsService);
            string file = Path.Combine(NewTempHome(), "dropped.fjson");
            File.WriteAllText(file, await EmptyVanillaGraphJsonAsync());
            DataCache vanillaCache = await LoadedVanillaCacheAsync();
            try {
                await Bounded(ShellHost.OpenFilesAsync([file]));

                MainWindow? window = await Bounded(ShellBootstrapper.BootAsync(
                    settingsService,
                    settings,
                    new PresetResolver(NewPresetsDirectoryWithVanillaAnd("Custom Preset"), NewTempHome()),
                    (requested, _, _) => Task.FromResult<(DataCache, Preset)?>((vanillaCache, requested)),
                    (_, _, _) => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask));

                Assert.NotNull(window);
                Assert.Equal("dropped", window.ViewModel.DocumentName);
                GraphRow row = Assert.Single(window.Library!.All(), r => r.Name == "dropped");
                Assert.Equal(file, row.SourcePath);
                Assert.Equal(row.Id, window.Library.OpenGraphId);
                CloseWithoutWelcome(window);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_WhileAnOpenIsRunning_QueuesBehindItAndOpensWhenItFinishes() {
            using var library = new LibraryFixture();
            MainWindow window = await ShellWindowAsync(library);
            try {
                string dir = library.NewTempDir();
                string second = Path.Combine(dir, "b.fjson");
                File.WriteAllText(second, await EmptyVanillaGraphJsonAsync());

                Task first = Bounded(ShellHost.OpenFilesAsync([Path.Combine(dir, "missing.fjson")]));
                await WaitUntilAsync(() => SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Any());
                await Bounded(ShellHost.OpenFilesAsync([second]));
                Assert.DoesNotContain(library.Service.All(), r => r.Name == "b");
                AlertSheet alert = Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>());
                alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await first;

                GraphRow row = Assert.Single(library.Service.All(), r => r.Name == "b");
                Assert.Equal(second, row.SourcePath);
                Assert.Equal(row.Id, library.Service.OpenGraphId);
                Assert.Null(WelcomeLauncher.Current);
            } finally {
                ShellHost.Context = null;
                ShellHost.ResetQueue();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_WithTheWelcomeUp_ShowsTheCouldntOpenAlertOnTheWelcome_AndCreatesNoMainWindow() {
            var settingsService = new SettingsService(NewTempHome());
            string missing = Path.Combine(NewTempHome(), "missing.fjson");
            DataCache cache = await LoadedVanillaCacheAsync();
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(cache);
            MainWindow? before = MainWindow.MostRecentlyOpened;
            try {
                await Bounded(ShellBootstrapper.BootAsync(
                    settingsService,
                    Factorio20Settings(settingsService),
                    ResolverWithoutUserPresets(),
                    (_, _, _) => Task.FromResult<(DataCache, Preset)?>(null),
                    (_, _, _) => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask));
                WelcomeWindow welcome = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                await welcome.ViewModel.LoadTask;

                Task opening = Bounded(ShellHost.OpenFilesAsync([missing]));
                await WaitUntilAsync(() => SheetHost.For(welcome).GetVisualDescendants().OfType<AlertSheet>().Any());
                AlertSheet alert = Assert.Single(SheetHost.For(welcome).GetVisualDescendants().OfType<AlertSheet>());
                Assert.Equal("Couldn’t open “missing”.", alert.FrameControl.Headline);
                Assert.False(ShellHost.HasLiveMainWindow);
                alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await opening;

                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Same(before, MainWindow.MostRecentlyOpened);
                Assert.Same(welcome, WelcomeLauncher.Current);
            } finally {
                CloseWelcome();
            }
        }

        private static async Task<WelcomeWindow> BootedWelcomeAsync() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(cache);
            AppSettings settings = settingsService.Load();
            settings.CurrentPresetName = "Factorio 2.0 Vanilla";
            await Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                settings,
                ResolverWithoutUserPresets(),
                (_, _, _) => Task.FromResult<(DataCache, Preset)?>(null),
                (_, _, _) => Task.CompletedTask,
                () => { },
                () => Task.CompletedTask));
            WelcomeWindow welcome = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
            await welcome.ViewModel.LoadTask;
            return welcome;
        }

        [AvaloniaFact]
        public async Task OpenGraphAsync_WithTheWelcomeUp_KeepsTheMainWindowHiddenAndAlertsOnTheWelcomeUntilTheContentsLoad() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                GraphRow row = context.Library.Create("Unreadable", context.Settings.CurrentPresetName, "not a graph", 0, null);
                context.Library.Detach();

                Task<bool> opening = WelcomeLauncher.OpenGraphAsync(context, row, null);
                await WaitUntilAsync(() => opening.IsCompleted || context.Window is not null && (SheetHost.For(context.Window).GetVisualDescendants().OfType<AlertSheet>().Any()
                    || SheetHost.For(welcome).GetVisualDescendants().OfType<AlertSheet>().Any()));

                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Empty(SheetHost.For(context.Window!).GetVisualDescendants().OfType<AlertSheet>());
                AlertSheet alert = Assert.Single(SheetHost.For(welcome).GetVisualDescendants().OfType<AlertSheet>());
                alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(await opening);
                Assert.True(context.Window is null || context.Window.IsClosed);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenFileAsync_WithTheWelcomeUp_WhenNothingIsPicked_LeavesNoMainWindowBehind() {
            try {
                await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;

                bool opened = await WelcomeLauncher.OpenFileAsync(context);
                Dispatcher.UIThread.RunJobs();

                Assert.False(opened);
                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.True(context.Window is null || context.Window.IsClosed);
            } finally {
                CloseWelcome();
            }
        }

        private static async Task<string> VanillaGraphJsonWithASupplierAsync() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset("Factorio 2.0 Vanilla", true, true), new Progress<KeyValuePair<int, string>>());
            var graph = new Foreman.ProductionGraph();
            graph.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), System.Drawing.Point.Empty);
            return Foreman.Serialization.GraphSaveCodec.WriteViewerDocumentToString(new Foreman.Serialization.GraphViewerSaveDocument {
                Version = Foreman.Serialization.GraphSaveFormat.SaveFormatVersion,
                SavedPresetName = cache.PresetName,
                IncludedMods = new Dictionary<string, string>(cache.IncludedMods),
                ProductionGraph = Foreman.Serialization.GraphSaveCodec.BuildProductionGraph(graph),
            });
        }

        private static void Dismiss(AlertSheet alert) =>
            alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        [AvaloniaFact]
        public async Task OpenGraphAsync_WhileAnotherOpenIsPending_ReturnsFalseAndLeavesTheWindowAlone() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                GraphRow unreadable = context.Library.Create("Unreadable", context.Settings.CurrentPresetName, "not a graph", 0, null);
                GraphRow readable = context.Library.Create("Readable", context.Settings.CurrentPresetName, await VanillaGraphJsonWithASupplierAsync(), 1, null);
                context.Library.Detach();

                Task<bool> first = WelcomeLauncher.OpenGraphAsync(context, unreadable, null);
                await WaitUntilAsync(() => SheetHost.For(welcome).GetVisualDescendants().OfType<AlertSheet>().Any());
                MainWindow pending = context.Window!;

                bool second = await WelcomeLauncher.OpenGraphAsync(context, readable, null);

                Assert.False(second);
                Assert.False(await WelcomeLauncher.NewGraphAsync(context));
                Assert.Same(pending, context.Window);
                Assert.False(pending.IsClosed);
                Assert.Same(welcome, pending.AlertOwner);
                Dismiss(Assert.Single(SheetHost.For(welcome).GetVisualDescendants().OfType<AlertSheet>()));
                Assert.False(await first);
                Assert.Null(context.Library.OpenGraphId);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenOnWindowAsync_WhenTheOpenThrows_ClosesTheDeferredWindowAndReleasesTheOpen() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                MainWindow? deferred = null;

                await Assert.ThrowsAsync<InvalidOperationException>(() => WelcomeLauncher.OpenOnWindowAsync(
                    context,
                    window => {
                        deferred = window;
                        throw new InvalidOperationException();
                    },
                    (_, _) => true));

                Assert.NotNull(deferred);
                Assert.Null(deferred.AlertOwner);
                Assert.True(deferred.IsClosed);
                Assert.False(context.OpenPending);
                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Same(welcome, WelcomeLauncher.Current);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenGraphAsync_WhenTheWelcomeClosesWhileItsAlertIsUp_ReleasesTheOpenAndLeavesNoHiddenWindow() {
            string dir = NewTempHome();
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                GraphRow unreadable = context.Library.Create("Unreadable", context.Settings.CurrentPresetName, "not a graph", 0, null);
                context.Library.Detach();

                Task<bool> opening = WelcomeLauncher.OpenGraphAsync(context, unreadable, null);
                await WaitUntilAsync(() => SheetHost.For(welcome).GetVisualDescendants().OfType<AlertSheet>().Any());
                welcome.Close();
                await WaitUntilAsync(() => opening.IsCompleted);

                Assert.False(await opening);
                Assert.False(ShellHost.Busy);
                Assert.True(context.Window is null || context.Window.IsClosed);
                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Null(WelcomeLauncher.Current);

                string dropped = Path.Combine(dir, "dropped.fjson");
                File.WriteAllText(dropped, await VanillaGraphJsonWithASupplierAsync());
                await Bounded(ShellHost.OpenFilesAsync([dropped]));
                Assert.True(ShellHost.HasLiveMainWindow);
                Assert.NotNull(context.Library.OpenGraphId);
            } finally {
                CloseWelcome();
                Directory.Delete(dir, true);
            }
        }

        [AvaloniaFact]
        public async Task NewGraphAsync_WhenTheWindowWasLeftDeferred_ShowsIt() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                welcome.Close();
                MainWindow hidden = ShellBootstrapper.CreateWindow(context, deferShow: true);

                Assert.True(await WelcomeLauncher.NewGraphAsync(context));

                Assert.Same(hidden, context.Window);
                Assert.True(ShellHost.HasLiveMainWindow);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenGraphAsync_WithNoWelcomeAndNoWindow_ShowsTheWindowOnlyOnceTheContentsHaveLoaded() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                GraphRow row = context.Library.Create("Readable", context.Settings.CurrentPresetName, await VanillaGraphJsonWithASupplierAsync(), 1, null);
                context.Library.Detach();
                welcome.Close();
                if (context.Window is { IsClosed: false } existing)
                    existing.Close();
                var nodesWhenShown = new List<int>();
                using IDisposable watch = Window.WindowOpenedEvent.AddClassHandler<MainWindow>((window, _) => nodesWhenShown.Add(window.GraphCanvas.Viewer.Graph.Nodes.Count()));

                Assert.True(await WelcomeLauncher.OpenGraphAsync(context, row, null));

                Assert.Equal([1], nodesWhenShown);
                Assert.True(ShellHost.HasLiveMainWindow);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenGraphAsync_WithNoWelcomeAndNoWindow_WhenTheGraphIsUnreadable_ShowsTheWindowForTheAlert() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                GraphRow row = context.Library.Create("Unreadable", context.Settings.CurrentPresetName, "not a graph", 0, null);
                context.Library.Detach();
                welcome.Close();
                if (context.Window is { IsClosed: false } existing)
                    existing.Close();

                Task<bool> opening = WelcomeLauncher.OpenGraphAsync(context, row, null);
                await WaitUntilAsync(() => context.Window is { IsClosed: false } window && SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Any());

                Assert.True(ShellHost.HasLiveMainWindow);
                MainWindow created = context.Window!;
                Dismiss(Assert.Single(SheetHost.For(created).GetVisualDescendants().OfType<AlertSheet>()));
                Assert.False(await opening);
                Assert.True(created.IsClosed);
                Assert.Null(context.Window);
                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Null(WelcomeLauncher.Current);
            } finally {
                CloseWelcome();
            }
        }

        private static string WithUnknownPreset(string json) =>
            System.Text.RegularExpressions.Regex.Replace(json, "\"SavedPresetName\"\\s*:\\s*\"[^\"]*\"", "\"SavedPresetName\": \"Not An Installed Preset\"");

        private static string WithUnloadableContents(string json) =>
            System.Text.RegularExpressions.Regex.Replace(json, "\"BaseQuality\"\\s*:\\s*\"[^\"]*\"", "\"BaseQuality\": \"missing\"");

        private static PresetSelectionSheet PresetSheetOn(MainWindow window) =>
            SheetHost.For(window).GetVisualDescendants().OfType<PresetSelectionSheet>().Single();

        [AvaloniaFact]
        public async Task OpenGraphAsync_WithTheWelcomeUp_WhenThePresetSheetIsCancelled_LeavesNoMainWindowBehind() {
            try {
                await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                GraphRow row = context.Library.Create("Elsewhere", context.Settings.CurrentPresetName, WithUnknownPreset(await VanillaGraphJsonWithASupplierAsync()), 1, null);
                context.Library.Detach();

                Task<bool> opening = WelcomeLauncher.OpenGraphAsync(context, row, null);
                await WaitUntilAsync(() => context.Window is { } window && SheetHost.For(window).GetVisualDescendants().OfType<PresetSelectionSheet>().Any());
                MainWindow created = context.Window!;
                Assert.True(ShellHost.HasLiveMainWindow);
                PresetSheetOn(created).SimulateCancelClick();

                Assert.False(await opening);
                Assert.True(created.IsClosed);
                Assert.Null(context.Window);
                Assert.False(ShellHost.HasLiveMainWindow);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenGraphAsync_WhenTheWelcomeClosesDuringThePresetSheetAndTheLoadFails_AlertsOnTheMainWindowAndReleasesTheOpen() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                GraphRow row = context.Library.Create("Broken", context.Settings.CurrentPresetName, WithUnloadableContents(WithUnknownPreset(await VanillaGraphJsonWithASupplierAsync())), 1, null);
                context.Library.Detach();

                Task<bool> opening = WelcomeLauncher.OpenGraphAsync(context, row, null);
                await WaitUntilAsync(() => context.Window is { } window && SheetHost.For(window).GetVisualDescendants().OfType<PresetSelectionSheet>().Any());
                MainWindow created = context.Window!;
                welcome.Close();
                PresetSelectionSheet sheet = PresetSheetOn(created);
                sheet.SimulateSelectRow(((List<PresetRow>)sheet.PresetSelectionListViewControl.ItemsSource!).First(r => r.Name == context.Settings.CurrentPresetName).Preset);
                sheet.SimulateConfirmClick();
                await WaitUntilAsync(() => SheetHost.For(created).GetVisualDescendants().OfType<AlertSheet>().Any());

                Dismiss(Assert.Single(SheetHost.For(created).GetVisualDescendants().OfType<AlertSheet>()));
                Assert.False(await opening);
                Assert.False(context.OpenPending);
                Assert.False(ShellHost.Busy);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenPathAsync_WhenAFileIsDroppedWhileAnOpenWaitsOnItsAlert_OpensItOnceTheOpenEnds() {
            using var library = new LibraryFixture();
            MainWindow window = await ShellWindowAsync(library);
            try {
                string dir = library.NewTempDir();
                string file = Path.Combine(dir, "b.fjson");
                File.WriteAllText(file, await EmptyVanillaGraphJsonAsync());

                Task<bool> opening = WelcomeLauncher.OpenPathAsync(ShellHost.Context!, Path.Combine(dir, "missing.fjson"));
                await WaitUntilAsync(() => AlertsOn(window).Count == 1);
                await Bounded(ShellHost.OpenFilesAsync([file]));

                Assert.DoesNotContain(library.Service.All(), r => r.Name == "b");
                AlertsOn(window)[0].Cancel();
                Assert.False(await opening);
                await WaitUntilAsync(() => library.Service.All().Any(r => r.Name == "b"));
            } finally {
                ShellHost.Context = null;
                ShellHost.ResetQueue();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_WhenTheFileFailsToLoadOverAnOpenGraph_KeepsTheOpenGraphAutosaving() {
            using var library = new LibraryFixture();
            MainWindow window = await ShellWindowAsync(library);
            try {
                DataCache cache = window.DataCache!;
                window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), System.Drawing.Point.Empty);
                library.Clock.Advance(TimeSpan.FromSeconds(2));
                Dispatcher.UIThread.RunJobs();
                Guid id = library.Service.OpenGraphId!.Value;
                string broken = System.Text.RegularExpressions.Regex.Replace(library.Store.Get(id)!.Data, "\"BaseQuality\"\\s*:\\s*\"[^\"]*\"", "\"BaseQuality\": \"missing\"");
                string bad = Path.Combine(library.NewTempDir(), "bad.fjson");
                File.WriteAllText(bad, broken);

                Task opening = Bounded(ShellHost.OpenFilesAsync([bad]));
                await WaitUntilAsync(() => SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Any());
                Assert.Single(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>()).YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await opening;
                window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["copper-plate"], cache.DefaultQuality!), new System.Drawing.Point(300, 0));
                library.Clock.Advance(TimeSpan.FromSeconds(2));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(id, library.Service.OpenGraphId);
                Assert.Equal(2, library.Store.Get(id)!.NodeCount);
                Assert.Equal(2, window.GraphCanvas.Viewer.NodeElements.Count);
            } finally {
                ShellHost.Context = null;
                ShellHost.ResetQueue();
            }
        }

        private static async Task<WelcomeWindow> BootWelcomeAsync(SettingsService settingsService, Func<Preset, Task<DataCache?>> loader, string presetName = "Factorio 2.0 Vanilla", PresetResolver? resolver = null) {
            WelcomeLauncher.CacheLoaderOverride = loader;
            AppSettings settings = settingsService.Load();
            settings.CurrentPresetName = presetName;
            await Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                settings,
                resolver ?? ResolverWithoutUserPresets(),
                (_, _, _) => Task.FromResult<(DataCache, Preset)?>(null),
                (_, _, _) => Task.CompletedTask,
                () => { },
                () => Task.CompletedTask));
            return Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
        }

        private static string WriteBrokenGraphFile(DataCache cache) {
            var graph = new Foreman.ProductionGraph();
            graph.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), System.Drawing.Point.Empty);
            string valid = Foreman.Serialization.GraphSaveCodec.WriteViewerDocumentToString(new Foreman.Serialization.GraphViewerSaveDocument {
                Version = Foreman.Serialization.GraphSaveFormat.SaveFormatVersion,
                SavedPresetName = cache.PresetName,
                IncludedMods = new Dictionary<string, string>(cache.IncludedMods),
                ProductionGraph = Foreman.Serialization.GraphSaveCodec.BuildProductionGraph(graph),
            });
            string broken = System.Text.RegularExpressions.Regex.Replace(valid, "\"BaseQuality\"\\s*:\\s*\"[^\"]*\"", "\"BaseQuality\": \"missing\"");
            Assert.NotEqual(valid, broken);
            string bad = Path.Combine(NewTempHome(), "bad.fjson");
            File.WriteAllText(bad, broken);
            return bad;
        }

        private static IReadOnlyList<AlertSheet> AlertsOn(Window window) => [.. SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>()];

        [AvaloniaFact]
        public async Task OpenFilesAsync_DuringAWelcomeRowOpen_HoldsTheFileUntilTheRowOpenEnds() {
            var settingsService = new SettingsService(NewTempHome());
            string file = Path.Combine(NewTempHome(), "b.fjson");
            File.WriteAllText(file, await EmptyVanillaGraphJsonAsync());
            DataCache cache = await LoadedVanillaCacheAsync();
            var store = new GraphLibraryStore(settingsService.DatabasePath);
            store.Create("Gone", "Gone Preset", await EmptyVanillaGraphJsonAsync(), 0, null, DateTime.UtcNow);
            try {
                WelcomeWindow welcome = await BootWelcomeAsync(settingsService, _ => Task.FromResult<DataCache?>(cache));
                await welcome.ViewModel.LoadTask;
                WelcomeRow row = Assert.Single(welcome.ViewModel.Rows);
                Task opening = welcome.OpenRowAsync(row);
                await WaitUntilAsync(() => AlertsOn(welcome).Count == 1);

                await Bounded(ShellHost.OpenFilesAsync([file]));

                Assert.DoesNotContain(store.ListByLastOpened(), r => r.Name == "b");
                Assert.Same(welcome, WelcomeLauncher.Current);
                Assert.False(ShellHost.HasLiveMainWindow);
                AlertsOn(welcome)[0].Cancel();
                await opening;
                await WaitUntilAsync(() => store.ListByLastOpened().Any(r => r.Name == "b"));
                GraphRow opened = Assert.Single(store.ListByLastOpened(), r => r.Name == "b");
                Assert.Equal(file, opened.SourcePath);
                Assert.Equal(opened.Id, ShellHost.Context!.Library.OpenGraphId);
                CloseWithoutWelcome(ShellHost.Context.Window!);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_DuringAnOpenFromTheMainWindow_HoldsTheFileUntilItEnds() {
            using var library = new LibraryFixture();
            MainWindow window = await ShellWindowAsync(library);
            try {
                string dir = library.NewTempDir();
                string file = Path.Combine(dir, "b.fjson");
                File.WriteAllText(file, await EmptyVanillaGraphJsonAsync());
                window.OpenFileStub = () => Task.FromResult<(string, string)?>(("{}", Path.Combine(dir, "unreadable.fjson")));

                Task opening = window.OpenGraphFileAsync();
                await WaitUntilAsync(() => AlertsOn(window).Count == 1);
                await Bounded(ShellHost.OpenFilesAsync([file]));

                Assert.DoesNotContain(library.Service.All(), r => r.Name == "b");
                AlertsOn(window)[0].Cancel();
                await opening;
                await WaitUntilAsync(() => library.Service.All().Any(r => r.Name == "b"));
                Assert.Equal(file, Assert.Single(library.Service.All(), r => r.Name == "b").SourcePath);
            } finally {
                ShellHost.Context = null;
                ShellHost.ResetQueue();
            }
        }

        [AvaloniaFact]
        public async Task ReopenAsync_WithoutALoadedCache_LoadsThePresetBeforeBuildingTheWindow() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            try {
                WelcomeWindow failed = await BootWelcomeAsync(settingsService, _ => Task.FromResult<DataCache?>(null));
                await failed.ViewModel.LoadTask;
                ShellHost.Context!.Settings.ShowWelcomeAtLaunch = false;
                failed.Close();
                var gate = new TaskCompletionSource<DataCache?>();
                WelcomeLauncher.CacheLoaderOverride = _ => gate.Task;
                IReadOnlyList<NativeMenuItemBase> items = ShellHost.BuildNoWindowItems();

                Task reopening = ShellHost.ReopenAsync();

                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.True(ShellHost.Context.Loading);
                Assert.Equal(WelcomeLoadPhase.Loading, Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current).ViewModel.Phase);
                Assert.All(items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator && i.Header != "Welcome to Foreman"), i => Assert.False(i.IsEnabled));
                gate.SetResult(cache);
                await reopening;

                Assert.True(ShellHost.Context.CacheLoaded);
                Assert.True(ShellHost.HasLiveMainWindow);
                Assert.Null(WelcomeLauncher.Current);
                Assert.Same(cache, ShellHost.Context.Window!.DataCache);
                CloseWithoutWelcome(ShellHost.Context.Window);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task LiveWindowAsync_WhileAnOpenIsPendingOnAHiddenWindow_ReturnsNull() {
            try {
                WelcomeWindow welcome = await BootedWelcomeAsync();
                ShellContext context = ShellHost.Context!;
                welcome.Close();
                if (MainWindow.MostRecentlyOpened is { IsClosed: false } stale)
                    stale.CloseAbandoned();
                Assert.Null(WelcomeLauncher.Current);
                MainWindow hidden = WelcomeLauncher.EnsureWindow(context, deferShow: true);
                context.OpenPending = true;

                MainWindow? live = await ShellHost.LiveWindowAsync();

                Assert.Null(live);
                Assert.False(hidden.IsClosed);
                Assert.True(hidden.ShowDeferred);
                context.OpenPending = false;
                hidden.CloseAbandoned();
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task LiveWindowAsync_WithoutALoadedCache_LoadsThePresetFirst() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            try {
                WelcomeWindow failed = await BootWelcomeAsync(settingsService, _ => Task.FromResult<DataCache?>(null));
                await failed.ViewModel.LoadTask;
                failed.Close();
                WelcomeLauncher.CacheLoaderOverride = _ => Task.FromResult<DataCache?>(cache);

                MainWindow? live = await ShellHost.LiveWindowAsync();

                Assert.NotNull(live);
                Assert.Same(cache, live.DataCache);
                Assert.True(ShellHost.Context!.CacheLoaded);
                CloseWithoutWelcome(live);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task ReopenAsync_WhenTheWelcomeClosedMidLoad_AttachesToTheRunningLoad() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            var gate = new TaskCompletionSource<DataCache?>();
            int calls = 0;
            try {
                WelcomeWindow first = await BootWelcomeAsync(settingsService, _ => { calls++; return gate.Task; });
                Assert.Equal(WelcomeLoadPhase.Loading, first.ViewModel.Phase);
                first.Close();

                await ShellHost.ReopenAsync();

                WelcomeWindow second = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Assert.Equal(1, calls);
                gate.SetResult(cache);
                await WaitUntilAsync(() => second.ViewModel.ActionsEnabled);
                Assert.Equal(1, calls);
                Assert.Same(cache, ShellHost.Context!.Cache);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_WithTheWelcomeUp_AndTheNodesFailToImport_AlertsOnTheWelcomeAndLeavesNoMainWindow() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            string bad = WriteBrokenGraphFile(cache);
            MainWindow? before = MainWindow.MostRecentlyOpened;
            var store = new GraphLibraryStore(settingsService.DatabasePath);
            try {
                WelcomeWindow welcome = await BootWelcomeAsync(settingsService, _ => Task.FromResult<DataCache?>(cache));
                await welcome.ViewModel.LoadTask;

                Task opening = Bounded(ShellHost.OpenFilesAsync([bad]));
                await WaitUntilAsync(() => AlertsOn(welcome).Count == 1);

                Assert.Equal("Couldn’t open “bad”.", AlertsOn(welcome)[0].FrameControl.Headline);
                Assert.False(ShellHost.HasLiveMainWindow);
                await ShellHost.ReopenAsync();
                Assert.False(ShellHost.Context!.Window!.IsVisible);
                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Same(welcome, WelcomeLauncher.Current);
                AlertsOn(welcome)[0].YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await opening;

                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.Same(before, MainWindow.MostRecentlyOpened);
                Assert.DoesNotContain(store.ListByLastOpened(), r => r.Name == "bad");
                Assert.Same(welcome, WelcomeLauncher.Current);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task ReopenAsync_WithFreshSettings_AttachesToTheRunningLoadOfTheResolvedDefault() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            var gate = new TaskCompletionSource<DataCache?>();
            List<string> names = [];
            try {
                WelcomeWindow first = await BootWelcomeAsync(settingsService, p => { names.Add(p.Name); return gate.Task; }, presetName: "");
                first.Close();

                await ShellHost.ReopenAsync();

                WelcomeWindow second = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Assert.Equal(["Factorio 2.1 Vanilla"], names);
                gate.SetResult(cache);
                await WaitUntilAsync(() => second.ViewModel.ActionsEnabled);
                Assert.Equal(["Factorio 2.1 Vanilla"], names);
                Assert.Equal("Factorio 2.1 Vanilla", ShellHost.Context!.Settings.CurrentPresetName);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task LiveWindowAsync_WithFreshSettings_LoadsTheResolvedDefaultOnce() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            List<string> names = [];
            try {
                WelcomeWindow failed = await BootWelcomeAsync(settingsService, _ => Task.FromResult<DataCache?>(null), presetName: "");
                await failed.ViewModel.LoadTask;
                failed.Close();
                WelcomeLauncher.CacheLoaderOverride = p => { names.Add(p.Name); return Task.FromResult<DataCache?>(cache); };

                MainWindow? live = await ShellHost.LiveWindowAsync();

                Assert.NotNull(live);
                Assert.Equal(["Factorio 2.1 Vanilla"], names);
                Assert.Same(cache, live.DataCache);
                CloseWithoutWelcome(live);
            } finally {
                CloseWelcome();
            }
        }

        private static ShellContext ContextWithMissingDefaultPreset(SettingsService settingsService, AppSettings settings) {
            string empty = NewTempHome();
            var resolver = new PresetResolver(empty, NewTempHome());
            var library = new GraphLibraryService(new GraphLibraryStore(settingsService.DatabasePath), TimeProvider.System);
            return new ShellContext(library, settings, new DataCache(false), settingsService, resolver) { CacheLoaded = false };
        }

        private static (ShellContext Context, List<string> Warnings) MissingDefaultWithWarnings() {
            var settingsService = new SettingsService(NewTempHome());
            AppSettings settings = settingsService.Load();
            settings.CurrentPresetName = "Gone Preset";
            ShellContext context = ContextWithMissingDefaultPreset(settingsService, settings);
            List<string> warnings = [];
            WelcomeLauncher.WarningOverride = (_, _, message) => { warnings.Add(message); return Task.CompletedTask; };
            return (context, warnings);
        }

        [AvaloniaFact]
        public async Task NoWindowAction_WhenNoPresetResolvesMidSession_WarnsOnceAndOpensNothing() {
            (ShellContext context, List<string> warnings) = MissingDefaultWithWarnings();
            try {
                bool opened = await WelcomeLauncher.NewGraphAsync(context);

                Assert.False(opened);
                Assert.Single(warnings);
                Assert.Contains(PresetResolver.DefaultPresetName, warnings[0]);
                Assert.Null(WelcomeLauncher.Current);
                Assert.Null(context.Window);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task Show_WhenNoPresetResolvesMidSession_WarnsOnceAndOpensNothing() {
            (ShellContext context, List<string> warnings) = MissingDefaultWithWarnings();
            try {
                WelcomeWindow? shown = WelcomeLauncher.Show(context);
                await WaitUntilAsync(() => warnings.Count == 1);
                Dispatcher.UIThread.RunJobs();

                Assert.Null(shown);
                Assert.Single(warnings);
                Assert.Contains(PresetResolver.DefaultPresetName, warnings[0]);
                Assert.Null(WelcomeLauncher.Current);
                Assert.Null(context.Window);
            } finally {
                CloseWelcome();
            }
        }

        private static (ShellContext Context, List<string> Warnings, TaskCompletionSource Dismiss) MissingDefaultWithHeldWarning() {
            (ShellContext context, List<string> warnings) = MissingDefaultWithWarnings();
            var dismiss = new TaskCompletionSource();
            WelcomeLauncher.WarningOverride = (_, _, message) => { warnings.Add(message); return dismiss.Task; };
            return (context, warnings, dismiss);
        }

        [AvaloniaFact]
        public async Task NoWindowAction_WhileTheMissingDefaultWarningIsUp_IsDropped_AndWarnsAgainOnceItIsDismissed() {
            (ShellContext context, List<string> warnings, TaskCompletionSource dismiss) = MissingDefaultWithHeldWarning();
            try {
                Task<bool> first = WelcomeLauncher.NewGraphAsync(context);
                Assert.Single(warnings);

                Task<bool> secondNew = WelcomeLauncher.NewGraphAsync(context);
                Task<bool> secondOpen = WelcomeLauncher.OpenPathAsync(context, Path.Combine(NewTempHome(), "none.fjson"));
                Assert.Null(WelcomeLauncher.Show(context));
                Assert.True(secondNew.IsCompletedSuccessfully);
                Assert.False(secondNew.Result);
                Assert.True(secondOpen.IsCompletedSuccessfully);
                Assert.False(secondOpen.Result);
                Assert.Single(warnings);

                dismiss.SetResult();
                Assert.False(await first);
                Assert.False(await WelcomeLauncher.NewGraphAsync(context));
                Assert.Equal(2, warnings.Count);
            } finally {
                dismiss.TrySetResult();
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task Show_WhileTheMissingDefaultWarningIsUp_IsDropped_AndWarnsAgainOnceItIsDismissed() {
            (ShellContext context, List<string> warnings, TaskCompletionSource dismiss) = MissingDefaultWithHeldWarning();
            try {
                Assert.Null(WelcomeLauncher.Show(context));
                Assert.Single(warnings);

                Assert.Null(WelcomeLauncher.Show(context));
                Task<bool> second = WelcomeLauncher.NewGraphAsync(context);
                Assert.True(second.IsCompletedSuccessfully);
                Assert.False(second.Result);
                Assert.Single(warnings);

                dismiss.SetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.False(context.WarningPending);
                Assert.Null(WelcomeLauncher.Show(context));
                Assert.Equal(2, warnings.Count);
            } finally {
                dismiss.TrySetResult();
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task BootAsync_WithAStaleCurrentPresetName_LoadsTheResolvedDefaultExactlyOnce() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            List<string> names = [];
            try {
                WelcomeWindow welcome = await BootWelcomeAsync(settingsService, p => { names.Add(p.Name); return Task.FromResult<DataCache?>(cache); }, presetName: "Gone Preset");
                await welcome.ViewModel.LoadTask;

                Assert.Equal([PresetResolver.DefaultPresetName], names);
                Assert.Equal(PresetResolver.DefaultPresetName, ShellHost.Context!.Settings.CurrentPresetName);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task EnsureLoadedAsync_WhenTheWelcomeItOpenedIsClosedWhileLoading_CancelsTheAction() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            var gate = new TaskCompletionSource<DataCache?>();
            try {
                WelcomeWindow booted = await BootWelcomeAsync(settingsService, _ => gate.Task);
                booted.Close();
                ShellContext context = ShellHost.Context!;

                Task<bool> action = WelcomeLauncher.NewGraphAsync(context);
                WelcomeWindow opened = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                opened.Close();
                gate.SetResult(cache);
                bool ran = await action;
                Dispatcher.UIThread.RunJobs();

                Assert.False(ran);
                Assert.True(context.CacheLoaded);
                Assert.Null(context.Window);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task Show_AfterAWelcomeWithAnInProgressPickIsClosed_AttachesToThatPick() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            string userDir = NewTempHome();
            File.WriteAllText(Path.Combine(userDir, "Other Preset.pjson"), "");
            File.WriteAllText(Path.Combine(userDir, "Other Preset.dat"), "");
            var resolver = new PresetResolver(presetsDirectoryOverride: null, userPresetsDirectoryOverride: userDir);
            var gate = new TaskCompletionSource<DataCache?>();
            int otherLoads = 0;
            try {
                WelcomeWindow first = await BootWelcomeAsync(settingsService, p => {
                    if (p.Name != "Other Preset")
                        return Task.FromResult<DataCache?>(cache);
                    otherLoads++;
                    return gate.Task;
                }, resolver: resolver);
                await first.ViewModel.LoadTask;
                Preset other = first.ViewModel.Presets.Single(p => p.Name == "Other Preset");
                _ = first.ViewModel.SelectPresetAsync(other);
                first.Close();

                WelcomeWindow second = WelcomeLauncher.Show(ShellHost.Context!)!;

                Assert.Equal("Other Preset", second.ViewModel.SelectedPreset!.Name);
                Assert.Equal(WelcomeLoadPhase.Loading, second.ViewModel.Phase);
                gate.SetResult(cache);
                await WaitUntilAsync(() => second.ViewModel.ActionsEnabled);
                Assert.Equal("Other Preset", second.ViewModel.LoadedPresetName);
                Assert.Equal(1, otherLoads);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task LoadAsync_WhenANewerLoadReplacedIt_WritesNothing() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cacheA = await LoadedVanillaCacheAsync();
            DataCache cacheB = await LoadedVanillaCacheAsync();
            string userDir = NewTempHome();
            File.WriteAllText(Path.Combine(userDir, "Other Preset.pjson"), "");
            File.WriteAllText(Path.Combine(userDir, "Other Preset.dat"), "");
            var resolver = new PresetResolver(presetsDirectoryOverride: null, userPresetsDirectoryOverride: userDir);
            var gates = new Dictionary<string, TaskCompletionSource<DataCache?>> {
                ["Factorio 2.0 Vanilla"] = new(),
                ["Other Preset"] = new(),
            };
            try {
                WelcomeWindow first = await BootWelcomeAsync(settingsService, p => gates[p.Name].Task, resolver: resolver);
                Assert.Equal(WelcomeLoadPhase.Loading, first.ViewModel.Phase);
                first.Close();
                await ShellHost.ReopenAsync();
                WelcomeWindow second = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Preset other = second.ViewModel.Presets.Single(p => p.Name == "Other Preset");

                Task selecting = second.ViewModel.SelectPresetAsync(other);
                gates["Other Preset"].SetResult(cacheB);
                await selecting;
                await WaitUntilAsync(() => second.ViewModel.ActionsEnabled);
                gates["Factorio 2.0 Vanilla"].SetResult(cacheA);
                await first.ViewModel.LoadTask;
                Dispatcher.UIThread.RunJobs();

                Assert.Same(cacheB, ShellHost.Context!.Cache);
                Assert.Equal("Other Preset", ShellHost.Context.Settings.CurrentPresetName);
                Assert.Equal("Other Preset", second.ViewModel.LoadedPresetName);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task ReopenAsync_WithoutALoadedCache_AndAFailingLoad_LeavesTheWelcomeOnItsErrorPanel() {
            var settingsService = new SettingsService(NewTempHome());
            try {
                WelcomeWindow failed = await BootWelcomeAsync(settingsService, _ => Task.FromResult<DataCache?>(null));
                await failed.ViewModel.LoadTask;
                ShellHost.Context!.Settings.ShowWelcomeAtLaunch = false;
                failed.Close();

                await ShellHost.ReopenAsync();

                Assert.False(ShellHost.HasLiveMainWindow);
                WelcomeWindow welcome = Assert.IsType<WelcomeWindow>(WelcomeLauncher.Current);
                Assert.Equal(WelcomeLoadPhase.Failed, welcome.ViewModel.Phase);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task NoWindowItems_ResyncWhenAClosedWelcomesLoadFinishes() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            var gate = new TaskCompletionSource<DataCache?>();
            try {
                WelcomeWindow welcome = await BootWelcomeAsync(settingsService, _ => gate.Task);
                welcome.Close();
                IReadOnlyList<NativeMenuItemBase> items = ShellHost.BuildNoWindowItems();
                NativeMenuItem newGraph = items.OfType<NativeMenuItem>().Single(i => i.Header == "New Graph");
                Assert.False(newGraph.IsEnabled);

                gate.SetResult(cache);
                await WaitUntilAsync(() => newGraph.IsEnabled);

                Assert.True(newGraph.IsEnabled);
            } finally {
                CloseWelcome();
            }
        }

        [AvaloniaFact]
        public async Task OpenGraphFileAsync_HoldsTheBusyScopeWhileThePickerIsUp() {
            using var library = new LibraryFixture();
            MainWindow window = await ShellWindowAsync(library);
            try {
                var picker = new TaskCompletionSource<(string, string)?>();
                window.OpenFileStub = () => picker.Task;

                Task opening = window.OpenGraphFileAsync();

                Assert.True(ShellHost.Busy);
                picker.SetResult(null);
                await opening;
                Assert.False(ShellHost.Busy);
            } finally {
                ShellHost.Context = null;
                ShellHost.ResetQueue();
            }
        }

        [AvaloniaFact]
        public async Task OpenFilesAsync_WithTheWelcomeUpBesideADocument_AlertsOnTheWelcome() {
            var settingsService = new SettingsService(NewTempHome());
            DataCache cache = await LoadedVanillaCacheAsync();
            string bad = WriteBrokenGraphFile(cache);
            try {
                WelcomeWindow first = await BootWelcomeAsync(settingsService, _ => Task.FromResult<DataCache?>(cache));
                await first.ViewModel.LoadTask;
                Assert.True(await first.NewGraphFromMenuAsync());
                MainWindow document = ShellHost.Context!.Window!;
                WelcomeWindow welcome = WelcomeLauncher.Show(ShellHost.Context)!;

                Task opening = Bounded(ShellHost.OpenFilesAsync([bad]));
                await WaitUntilAsync(() => AlertsOn(welcome).Count == 1);

                Assert.Empty(AlertsOn(document));
                AlertsOn(welcome)[0].YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await opening;
                CloseWithoutWelcome(document);
            } finally {
                CloseWelcome();
            }
        }

        private static async Task<MainWindow> ShellWindowAsync(LibraryFixture library) {
            DataCache cache = await LoadedVanillaCacheAsync();
            MainWindow window = library.Track(new MainWindow());
            window.Show();
            window.GraphCanvas.Viewport.SetSize(800, 800);
            window.DataCache = cache;
            window.Settings = new AppSettings { CurrentPresetName = cache.PresetName!, ShowWelcomeAtLaunch = false };
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: library.NewTempDir());
            window.SettingsService = new SettingsService(library.NewTempDir());
            window.Library = library.Service;
            window.ApplyLoadedSettings();
            await window.NewGraphAsync();
            ShellHost.Context = window.ShellContext;
            return window;
        }

        [AvaloniaFact]
        public async Task OpenLibraryGraphAsync_WhenTheWindowClosesDuringTheOpen_EndsQuietlyAndReturnsFalse() {
            using var library = new LibraryFixture();
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempHome());
            ErrorLogging.ClearLog();
            MainWindow window = await ShellWindowAsync(library);
            try {
                GraphRow stored = library.Service.Create("Stored", window.DataCache!.PresetName!, await VanillaGraphJsonWithASupplierAsync(), 1, null);
                library.Service.Detach();

                var loadFinished = new TaskCompletionSource<DataLoadResult>();
                bool loadStarted = false;
                window.DataLoadStub = (_, _) => { loadStarted = true; return loadFinished.Task; };
                Task<bool> opening = window.OpenLibraryGraphAsync(stored.Id, new Preset("Another Preset", true, true));
                Async.Fire(opening, nameof(MainWindow.OpenLibraryGraphAsync));
                Dispatcher.UIThread.RunJobs();
                Assert.True(loadStarted);
                window.Close();
                loadFinished.SetResult(new DataLoadResult(window.DataCache, DataLoadOutcome.Loaded));

                Assert.False(await opening);
                Assert.True(window.IsClosed);
                Assert.False(ShellHost.HasLiveMainWindow);
                Assert.False(File.Exists(ErrorLogging.LogFilePath) && File.ReadAllText(ErrorLogging.LogFilePath).Length > 0);
            } finally {
                CloseWelcome();
            }
        }

        //---- solver warmup seam (perf-packaging-reference.md §1c: first-node freeze) ----

        [AvaloniaFact]
        public async Task BootAsync_InvokesWarmSolverWithoutAwaitingItsCompletion() {
            var settingsService = new SettingsService(NewTempHome());
            bool warmupInvoked = false;

            var stopwatch = Stopwatch.StartNew();
            var window = await Bounded(ShellBootstrapper.BootAsync(
                settingsService,
                WelcomeOff(settingsService),
                new PresetResolver(),
                ShellBootstrapper.LoadPresetAsync,
                (_, _, _) => Task.CompletedTask,
                () => { },
                () => { warmupInvoked = true; return new TaskCompletionSource().Task; }));
            stopwatch.Stop();

            Assert.NotNull(window);
            Assert.True(warmupInvoked);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"boot waited on the warmup task ({stopwatch.Elapsed})");
            CloseWithoutWelcome(window);
        }
    }
}
