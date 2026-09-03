using Avalonia.Headless.XUnit;
using Foreman.DataCaching;
using Foreman.Mac.Services;
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
            File.WriteAllText(Path.Combine(dir, "Some Other Preset.pjson"), "");
            File.WriteAllText(Path.Combine(dir, "Some Other Preset.dat"), "");
            return dir;
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
            string bundleDir = NewPresetsDirectory(); //ships "Factorio 2.0 Vanilla" + "Some Other Preset"
            string userDir = NewEmptyDirectory();
            WritePresetFiles(userDir, "Imported Preset");
            WritePresetFiles(userDir, "Factorio 2.0 Vanilla"); //also present in bundleDir - must not duplicate
            var resolver = new PresetResolver(bundleDir, userDir);

            var names = resolver.BuildPresetList("Factorio 2.0 Vanilla").ConvertAll(p => p.Name);

            Assert.Equal(3, names.Count);
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
            File.WriteAllText(Path.Combine(dir, otherName + ".pjson"), "");
            File.WriteAllText(Path.Combine(dir, otherName + ".dat"), "");
            return dir;
        }

        [AvaloniaFact]
        public async Task BootAsync_RealVanillaPreset_LoadsMainWindowWithDataCache() {
            var settingsService = new SettingsService(NewTempHome());

            var window = await ShellBootstrapper.BootAsync(settingsService, settingsService.Load());

            Assert.NotNull(window);
            Assert.NotNull(window.DataCache);
            Assert.True(window.DataCache!.AvailableRecipes.Any());
        }

        [AvaloniaFact]
        public async Task BootAsync_WindowClosed_SavesResolvedPresetNameToSettings() {
            var settingsService = new SettingsService(NewTempHome());
            var window = await ShellBootstrapper.BootAsync(settingsService, settingsService.Load());

            window!.Close();

            Assert.Equal("Factorio 2.0 Vanilla", settingsService.Load().CurrentPresetName);
        }

        [AvaloniaFact]
        public async Task BootAsync_PassedSettingsInstance_IsTheOneMutatedAndSaved() {
            var settingsService = new SettingsService(NewTempHome());
            var settings = settingsService.Load();
            settings.IconsSize = 200; //an arbitrary in-range marker (ApplyLoadedSettings clamps to [8,256])

            var window = await ShellBootstrapper.BootAsync(settingsService, settings);
            window!.Close();

            var reloaded = settingsService.Load();
            Assert.Equal(200, reloaded.IconsSize);
            Assert.Equal("Factorio 2.0 Vanilla", reloaded.CurrentPresetName);
        }

        [AvaloniaFact]
        public async Task BootAsync_InitialLoadFails_ShowsWarningAndRetriesWithDefaultPreset() {
            var settingsService = new SettingsService(NewTempHome());
            var settings = settingsService.Load();
            settings.CurrentPresetName = "Custom Preset";
            string presetsDir = NewPresetsDirectoryWithVanillaAnd("Custom Preset");
            var warnings = new List<string>();
            bool fatalExitCalled = false;

            var window = await ShellBootstrapper.BootAsync(
                settingsService,
                settings,
                new PresetResolver(presetsDir),
                ShellBootstrapper.LoadPresetAsync,
                (_, _, message) => { warnings.Add(message); return Task.CompletedTask; },
                () => fatalExitCalled = true);

            Assert.NotNull(window);
            Assert.NotNull(window!.DataCache);
            Assert.True(window.DataCache!.AvailableRecipes.Any());
            Assert.False(fatalExitCalled);
            Assert.Single(warnings);
            Assert.Contains("Custom Preset", warnings[0]);
            Assert.Contains("is corrupt", warnings[0]);

            window.Close();
            Assert.Equal("Factorio 2.0 Vanilla", settingsService.Load().CurrentPresetName);
        }

        [AvaloniaFact]
        public async Task BootAsync_DefaultPresetMissingFromDisk_SignalsFatalExitWithoutLoading() {
            var settingsService = new SettingsService(NewTempHome());
            string emptyPresetsDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyPresetsDir);
            bool fatalExitCalled = false;
            bool loadAttempted = false;

            var window = await ShellBootstrapper.BootAsync(
                settingsService,
                settingsService.Load(),
                new PresetResolver(emptyPresetsDir),
                (_, _) => { loadAttempted = true; return Task.FromResult<DataCache?>(null); },
                (_, _, _) => Task.CompletedTask,
                () => fatalExitCalled = true);

            Assert.Null(window);
            Assert.True(fatalExitCalled);
            Assert.False(loadAttempted);
        }

        [AvaloniaFact]
        public async Task BootAsync_DefaultPresetAlsoFailsToLoad_SignalsFatalExit() {
            var settingsService = new SettingsService(NewTempHome());
            string presetsDir = NewPresetsDirectoryWithVanillaAnd("Custom Preset");
            var warnings = new List<string>();
            bool fatalExitCalled = false;

            var window = await ShellBootstrapper.BootAsync(
                settingsService,
                settingsService.Load(),
                new PresetResolver(presetsDir),
                (_, _) => Task.FromResult<DataCache?>(null),
                (_, _, message) => { warnings.Add(message); return Task.CompletedTask; },
                () => fatalExitCalled = true);

            Assert.Null(window);
            Assert.True(fatalExitCalled);
            Assert.Equal(2, warnings.Count);
            Assert.Contains("is corrupt", warnings[1]);
            Assert.Contains("No Preset is loaded", warnings[1]);
        }

        //---- solver warmup seam (perf-packaging-reference.md §1c: first-node freeze) ----

        [AvaloniaFact]
        public async Task BootAsync_InvokesWarmSolverWithoutAwaitingItsCompletion() {
            var settingsService = new SettingsService(NewTempHome());
            bool warmupInvoked = false;

            var stopwatch = Stopwatch.StartNew();
            var window = await ShellBootstrapper.BootAsync(
                settingsService,
                settingsService.Load(),
                new PresetResolver(),
                ShellBootstrapper.LoadPresetAsync,
                (_, _, _) => Task.CompletedTask,
                () => { },
                () => { warmupInvoked = true; return Task.Delay(TimeSpan.FromSeconds(10)); });
            stopwatch.Stop();

            Assert.NotNull(window);
            Assert.True(warmupInvoked);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"boot waited on the warmup task ({stopwatch.Elapsed})");
        }
    }
}
