using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Models;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //C2c Task 4: Settings applies live now - there's no more Confirm/Cancel cascade to test (that whole
    //buffer-everything-then-commit-once model is gone). This covers MainWindow.ShowSettings's own seams:
    //seeding the new window from live AppSettings, reusing the one instance, and SettingsSession's
    //save/resolve reaction to a real property change (theme/scale immediacy is SettingsSessionTests.cs's
    //job, not this file's - it needs no live graph).
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

        //Drives ShowSettings through its stub seam (skips the real Show(this)) and hands back the
        //fully-wired SettingsWindow instance.
        private static SettingsWindow OpenSettings(MainWindow window) {
            SettingsWindow? captured = null;
            window.SettingsDialogStub = w => captured = w;
            window.ShowSettings();
            return captured!;
        }

        [AvaloniaFact]
        public void ShowSettings_SeedsGeneralTabFromTheLiveAppSettingsInstance() {
            var settings = new AppSettings { QualitySteps = 7, SmartNodeDirection = false };
            var window = NewWindowReadyForSettings(settings);

            SettingsWindow opened = OpenSettings(window);

            Assert.Equal(7m, opened.ViewModel.QualityStepsValue);
            Assert.False(opened.ViewModel.SmartNodeDirectionChecked);
        }

        [AvaloniaFact]
        public void ShowSettings_CalledAgainWhileOneIsOpen_ReusesTheSameInstance() {
            var settings = new AppSettings();
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow first = OpenSettings(window);

            window.ShowSettings(); //no stub reset - a reused instance never calls the stub again

            Assert.Same(first, window.CurrentSettingsWindow);
        }

        //R5: every change writes onto AppSettings at once; the save itself coalesces 300ms - FlushAsync is
        //the deterministic seam, no real timer wait needed.
        [AvaloniaFact]
        public async Task TogglingSmartDirection_ChangesSettingsAtOnce_AndSavesExactlyOnceAfterFlush() {
            var settings = new AppSettings { SmartNodeDirection = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);

            opened.ViewModel.SmartNodeDirectionChecked = false;

            Assert.False(settings.SmartNodeDirection);
            Assert.Equal(0, window.CurrentSettingsSession!.SaveCallCount);

            await window.CurrentSettingsSession!.FlushAsync();

            Assert.Equal(1, window.CurrentSettingsSession!.SaveCallCount);
        }

        //R5's named solver-affecting field: a re-solve is requested (debounced) and, once flushed, actually
        //runs against the newly-committed value.
        [AvaloniaFact]
        public async Task ChangingDefaultAssemblerOption_RequestsExactlyOneReSolveAfterFlush() {
            var settings = new AppSettings { DefaultAssemblerOption = AssemblerSelector.Style.Worst };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);

            int solveCount = 0;
            AssemblerSelector.Style styleAtSolve = AssemblerSelector.Style.Worst;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => {
                solveCount++;
                styleAtSolve = window.GraphCanvas.Viewer.Graph.AssemblerSelector.DefaultSelectionStyle;
            };

            opened.ViewModel.AssemblerSelectorStyleIndex = (int)AssemblerSelector.Style.Best;
            Assert.Equal(0, window.CurrentSettingsSession!.ResolveCallCount);

            await window.CurrentSettingsSession!.FlushAsync();

            Assert.Equal(1, window.CurrentSettingsSession!.ResolveCallCount);
            Assert.Equal(AssemblerSelector.Style.Best, settings.DefaultAssemblerOption);
            Assert.Equal(1, solveCount);
            Assert.Equal(AssemblerSelector.Style.Best, styleAtSolve); //re-solve must run after the new value is already live
        }

        //Review fix round 1, critical: closing the window used to null the session straight away, dropping
        //whatever save/resolve was still mid-debounce. OnClosing now flushes first - this proves it without
        //the test ever calling FlushAsync itself.
        [AvaloniaFact]
        public async Task ClosingWithinTheCoalesceWindow_StillSavesExactlyOnceWithTheNewValue() {
            var settings = new AppSettings { SmartNodeDirection = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);
            SettingsSession session = window.CurrentSettingsSession!;

            opened.ViewModel.SmartNodeDirectionChecked = false; //schedules the 300ms coalesced save, not yet run

            var closed = new TaskCompletionSource();
            opened.Closed += (_, _) => closed.SetResult();
            opened.Close();
            await closed.Task;

            Assert.False(settings.SmartNodeDirection);
            Assert.Equal(1, session.SaveCallCount);
            Assert.False(window.SettingsService!.Load().SmartNodeDirection);
        }

        [AvaloniaFact]
        public async Task ClosingWithAPendingReSolve_StillResolvesExactlyOnceBeforeClosing() {
            var settings = new AppSettings { DefaultAssemblerOption = AssemblerSelector.Style.Worst };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);
            SettingsSession session = window.CurrentSettingsSession!;

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            opened.ViewModel.AssemblerSelectorStyleIndex = (int)AssemblerSelector.Style.Best; //schedules the debounced re-solve, not yet run

            var closed = new TaskCompletionSource();
            opened.Closed += (_, _) => closed.SetResult();
            opened.Close();
            await closed.Task;

            Assert.Equal(1, session.ResolveCallCount);
            Assert.Equal(1, solveCount);
            Assert.Equal(AssemblerSelector.Style.Best, settings.DefaultAssemblerOption);
        }

        //UseRecipeBWfilters changes which recipes even exist, so it can't be live-applied in place -
        //OnSettingsChanged special-cases it to close/reload/reopen instead (reference upstream MainForm.cs
        //413-415's old presetReloaded three-way OR, now split into its own path). With no preset on disk to
        //actually reload (NewTempDir's PresetResolver finds none), HandlePresetReloadAsync's reload branch
        //is a no-op and the whole close+reopen runs synchronously within this call - no wait needed.
        [AvaloniaFact]
        public void TogglingLoadBarreling_ClosesAndReopensAFreshSettingsWindow() {
            var settings = new AppSettings { UseRecipeBWfilters = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow first = OpenSettings(window);
            bool firstClosed = false;
            first.Closed += (_, _) => firstClosed = true;

            first.ViewModel.LoadBarrelingChecked = !first.ViewModel.LoadBarrelingChecked;

            Assert.True(firstClosed);
            Assert.False(settings.UseRecipeBWfilters); //LoadBarrelingChecked starts true (UseRecipeBWfilters true); one flip inverts it
            Assert.NotSame(first, window.CurrentSettingsWindow);
        }

        //Phase 7 deferred-minors sweep: the non-live-apply Enabled-Objects sync used to index
        //cache.Assemblers["rocket-silo"] directly, a KeyNotFoundException on any preset without a rocket
        //silo entity. RocketAssembler itself is a synthetic pseudo-assembler DataCacheBootstrap always
        //creates regardless of preset content, so its own null-conditional assignment never short-circuits
        //the indexer away - EnableAll's live write (SettingsWindow.ApplyEnabledObjectsLiveFromOptions) must
        //still guard the same lookup.
        private static DataCache CacheWithRocketAssemblerButNoSilo() {
            DataCache cache = MinimalCache();
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.RocketAssembler = new AssemblerPrototype(cache, "§§a:rocket-assembler", "Rocket", EntityType.Rocket, EnergySource.Void);
            return cache;
        }

        [AvaloniaFact]
        public void EnableAll_OnASiloLessPreset_DoesNotThrow() {
            var settings = new AppSettings();
            var window = new MainWindow();
            window.Show();
            window.DataCache = CacheWithRocketAssemblerButNoSilo();
            window.Settings = settings;
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();
            SettingsWindow opened = OpenSettings(window);

            opened.SimulateEnableAllClick();

            Assert.False(window.DataCache!.RocketAssembler!.Enabled);
        }
    }
}
