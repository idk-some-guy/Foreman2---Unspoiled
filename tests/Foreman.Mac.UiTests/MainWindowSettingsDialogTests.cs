using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Covers OpenSettingsAsync's Graph Options tab wiring (reference §5, upstream MainForm.cs:355-440):
    //population from live GraphViewer/Graph state, the Confirm apply path back onto settings/live graph,
    //and the presetReloaded three-way OR fix (MainForm.cs:413-415's third term).
    public class MainWindowSettingsDialogTests {
        private static DataCache MinimalCache() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            return cache;
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static MainWindow NewWindowReadyForSettings(AppSettings settings) {
            var window = new MainWindow();
            window.Show();
            window.DataCache = MinimalCache();
            window.Settings = settings;
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();
            return window;
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_PopulatesOptionsFromLiveGraphState_ForSolverFields() {
            var settings = new AppSettings { QualitySteps = 7, LowPriorityPower = 3.5m, PullConsumerNodes = true, PullConsumerNodesPower = 2.5m };
            var window = NewWindowReadyForSettings(settings);

            SettingsWindow.SettingsWindowOptions? captured = null;
            window.SettingsDialogStub = options => {
                captured = options;
                return Task.FromResult<bool?>(false);
            };

            await window.OpenSettingsAsync();

            Assert.NotNull(captured);
            Assert.Equal(7u, captured!.QualitySteps);
            Assert.Equal(3.5m, captured.SolverLowPriorityPower);
            Assert.True(captured.SolverPullConsumerNodes);
            Assert.Equal(2.5m, captured.SolverPullConsumerNodesPower);
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_Confirm_AppliesSolverFieldsToSettingsAndLiveGraph() {
            var settings = new AppSettings();
            var window = NewWindowReadyForSettings(settings);

            window.SettingsDialogStub = options => {
                //Nulling SelectedPreset routes this through the reload branch's no-op guard (no preset to
                //reload to) instead of the enabled-objects sync branch, which needs a fuller DataCache
                //(rocket-silo lookup) than this test's minimal one carries - irrelevant to what's under test.
                options.SelectedPreset = null;
                options.QualitySteps = 12;
                options.SolverLowPriorityPower = 6m;
                options.SolverPullConsumerNodes = true;
                options.SolverPullConsumerNodesPower = 4.5m;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(12, settings.QualitySteps);
            Assert.Equal(6m, settings.LowPriorityPower);
            Assert.True(settings.PullConsumerNodes);
            Assert.Equal(4.5m, settings.PullConsumerNodesPower);

            Assert.Equal(12u, window.GraphCanvas.Viewer.Graph.MaxQualitySteps);
            Assert.Equal(6d, window.GraphCanvas.Viewer.Graph.LowPriorityPower);
            Assert.True(window.GraphCanvas.Viewer.Graph.PullOutputNodes);
            Assert.Equal(4.5d, window.GraphCanvas.Viewer.Graph.PullOutputNodesPower);
        }

        //Ports ApplySettingsDialogChanges' re-solve tail (reference §5, upstream MainForm.cs:501-503):
        //Confirm must actually re-run the solver against the newly committed settings, not just write them
        //onto AppSettings/the live graph properties. NodeValuesUpdated only fires from UpdateNodeValues, so
        //counting it also catches the duplicate-subscription leak (BindViewOptionControls, Critical 2) firing
        //an extra solve.
        [AvaloniaFact]
        public async Task OpenSettingsAsync_Confirm_TriggersExactlyOneReSolveWithTheNewSettings() {
            var settings = new AppSettings();
            var window = NewWindowReadyForSettings(settings);

            int solveCount = 0;
            double lowPriorityPowerAtSolve = -1;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => {
                solveCount++;
                lowPriorityPowerAtSolve = window.GraphCanvas.Viewer.Graph.LowPriorityPower;
            };

            window.SettingsDialogStub = options => {
                options.SelectedPreset = null;
                options.SolverLowPriorityPower = 6m;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(1, solveCount);
            Assert.Equal(6d, lowPriorityPowerAtSolve); //re-solve must run after the new value is already live
        }

        //Regression: MainForm.cs:413-415 ORs a third condition into presetReloaded - a barreling/crating
        //(UseRecipeBWfilters) change alone must force the reload path, even with the same preset selected.
        //SelectedPreset is nulled out by the stub so this stays a fast unit-level check (no real
        //DataLoadWindow/preset reload): the only way settings.UseRecipeBWfilters can change here is if
        //presetReloaded's if-branch (MainWindow.axaml.cs) ran at all.
        [AvaloniaFact]
        public async Task OpenSettingsAsync_DevUseRecipeBWFiltersChangedAlone_TriggersPresetReloadedBranch() {
            var settings = new AppSettings { UseRecipeBWfilters = true };
            var window = NewWindowReadyForSettings(settings);

            window.SettingsDialogStub = options => {
                options.SelectedPreset = null;
                options.RequireReload = false;
                options.DevUseRecipeBWFilters = false;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.False(settings.UseRecipeBWfilters);
        }

        //Phase 7 deferred-minors sweep: OpenSettingsAsync's non-preset-reload branch indexed
        //cache.Assemblers["rocket-silo"] directly, a KeyNotFoundException on any preset without a rocket
        //silo entity. RocketAssembler itself is a synthetic pseudo-assembler DataCacheBootstrap always
        //creates regardless of preset content (DataCacheBootstrap.cs:97), so its own null-conditional
        //assignment never short-circuits the indexer away - this needs a cache with a real RocketAssembler
        //but no "rocket-silo" entry, which MinimalCache() (RocketAssembler left null) doesn't reproduce.
        private static DataCache CacheWithRocketAssemblerButNoSilo() {
            DataCache cache = MinimalCache();
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.RocketAssembler = new AssemblerPrototype(cache, "§§a:rocket-assembler", "Rocket", EntityType.Rocket, EnergySource.Void);
            return cache;
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_Confirm_SiloLessPreset_DoesNotThrow() {
            var settings = new AppSettings();
            var window = new MainWindow();
            window.Show();
            window.DataCache = CacheWithRocketAssemblerButNoSilo();
            window.Settings = settings;
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();

            window.SettingsDialogStub = _ => Task.FromResult<bool?>(true); //leaves SelectedPreset at its default so this lands in the enabled-objects sync (else) branch

            await window.OpenSettingsAsync();

            Assert.False(window.DataCache!.RocketAssembler!.Enabled);
        }
    }
}
