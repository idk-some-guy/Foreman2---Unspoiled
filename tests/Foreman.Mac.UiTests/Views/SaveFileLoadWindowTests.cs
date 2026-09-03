using Avalonia.Headless.XUnit;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers io-reference.md §5's SaveFileLoadForm contract (phase 6 Task 5) at the window/UI level: the
    //file-picker cancel path, wiring SaveFileReader.Result onto Outcome/SaveFileInfo/dialogs, the
    //mod-mismatch confirm gate and the §§-pseudo-recipe/save-driven/transitive-derive enabled-object
    //pipeline (ProcessSaveData), and the Cancel button's honest non-preemptive behavior (docs/io-
    //reference.md §4 caution). SaveFileReader's own process-pipeline correctness (exe discovery, crash/
    //another-instance/missing-marker detection, P0 parse) is covered separately against StubFactorioHarness
    //under ForemanTest (Foreman.Core), where that harness lives - LoadPipelineStub stands in for it here.
    public class SaveFileLoadWindowTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetVanillaCacheAsync() {
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

        private static DataCache NewEmptyCache() => new(filterRecipes: false);

        //---- picker cancel (reference §5 step 1's Cancel -> DialogResult.Cancel, no error) ---------------

        [AvaloniaFact]
        public async Task RunAsync_PickerCancelled_ClosesWithCancelOutcomeAndShowsNoDialogs() {
            var window = new SaveFileLoadWindow(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>(null),
                WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn"),
                ConfirmDialogStub = (_, _) => throw new InvalidOperationException("should not confirm"),
                LoadPipelineStub = (_, _) => throw new InvalidOperationException("should not run the pipeline"),
            };

            await window.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Cancel, window.Outcome);
            Assert.Null(window.SaveFileInfo);
        }

        //---- Opened triggers RunAsync (reference §5: "runs immediately on show") -------------------------

        //Every other test in this file drives RunAsync directly through LoadFromSaveDialogStub or a direct
        //call - none of them exercise the constructor's own "Opened += (_, _) => _ = RunAsync();" line. A
        //real Show() is the only way to prove that wiring still fires.
        [AvaloniaFact]
        public async Task Show_FiresOpenedWhichTriggersRunAsync_ReachesTheStubbedOutcome() {
            var saveInfo = new SaveFileInfo();
            var window = new SaveFileLoadWindow(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = saveInfo },
            };
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();

            window.Show();
            await closed.Task;

            Assert.Equal(SaveFileLoadOutcome.Ok, window.Outcome);
            Assert.Same(saveInfo, window.SaveFileInfo);
            Assert.Equal("/tmp/saves", window.ResolvedSaveFileLocation);
        }

        //---- wiring SaveFileReader.Result onto Outcome/SaveFileInfo/dialogs (reference §5 steps 3-6) ----

        [AvaloniaFact]
        public async Task RunAsync_PipelineReturnsOk_AppliesResultAndResolvesTheSaveLocation() {
            var saveInfo = new SaveFileInfo();
            saveInfo.Recipes["iron-plate"] = true;
            string savePath = "/tmp/saves/mysave.zip";
            var window = new SaveFileLoadWindow(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>(savePath),
                LoadPipelineStub = (path, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = saveInfo },
                WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn"),
            };

            await window.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Ok, window.Outcome);
            Assert.Same(saveInfo, window.SaveFileInfo);
            Assert.Equal("/tmp/saves", window.ResolvedSaveFileLocation);
        }

        [AvaloniaFact]
        public async Task RunAsync_PipelineReturnsAbortWithAWarningMessage_ShowsItVerbatimAndDoesNotProcess() {
            string? capturedMessage = null;
            var window = new SaveFileLoadWindow(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result {
                    Outcome = SaveFileLoadOutcome.Abort,
                    WarningMessage = "Factorio crashed while reading the save file.",
                },
                WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; },
            };

            await window.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Abort, window.Outcome);
            Assert.Equal("Factorio crashed while reading the save file.", capturedMessage);
            Assert.Null(window.ResolvedSaveFileLocation);
        }

        [AvaloniaFact]
        public async Task RunAsync_PipelineReturnsCancelWithNoMessage_ClosesWithoutAnyDialog() {
            var window = new SaveFileLoadWindow(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Cancel },
                WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn"),
            };

            await window.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Cancel, window.Outcome);
        }

        //---- Cancel button honesty (reference §4 caution: Run isn't preemptive) -------------------------

        [AvaloniaFact]
        public async Task SimulateCancelClick_SetsCancelOutcomeImmediately_WithoutWaitingForTheInFlightRun() {
            var pipelineStarted = new SemaphoreSlim(0, 1);
            var window = new SaveFileLoadWindow(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, token) => {
                    pipelineStarted.Release();
                    Thread.Sleep(500); //stands in for FactorioBenchmarkRunner.Run's non-preemptive ReadToEnd loop.
                    return new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = new SaveFileInfo() };
                },
            };

            Task runTask = window.RunAsync();
            await pipelineStarted.WaitAsync();

            window.SimulateCancelClick();

            Assert.Equal(SaveFileLoadOutcome.Cancel, window.Outcome);
            Assert.Null(window.SaveFileInfo);
            await runTask; //let the still-running pipeline finish so nothing leaks past the test.
            Assert.Equal(SaveFileLoadOutcome.Cancel, window.Outcome); //the late Ok result must not overwrite it.
        }

        //---- ProcessSaveData: mod-mismatch gate + §§/save-recipe/transitive-derive pipeline (reference §5) -

        [AvaloniaFact]
        public async Task ProcessSaveDataAsync_ModsMismatchAndConfirmDeclined_LeavesEnabledObjectsEmpty() {
            DataCache cache = await GetVanillaCacheAsync();
            var enabledObjects = new HashSet<IDataObjectBase>();
            var window = new SaveFileLoadWindow(cache, enabledObjects) {
                SaveFileInfo = new SaveFileInfo(),
            };
            string? capturedTitle = null;
            string? capturedMessage = null;
            window.ConfirmDialogStub = (title, message) => { capturedTitle = title; capturedMessage = message; return Task.FromResult(false); };

            await window.ProcessSaveDataAsync();

            Assert.Equal("Save file mod inconsistencies found!", capturedTitle);
            Assert.Contains("selected save file mods do not match preset mods; out of {0} mods:", capturedMessage);
            Assert.Empty(enabledObjects);
        }

        [AvaloniaFact]
        public async Task ProcessSaveDataAsync_NoModMismatch_EnablesPseudoRecipesSaveRecipesAndTheirAssemblers() {
            DataCache cache = await GetVanillaCacheAsync();
            IRecipe pseudoRecipe = cache.Recipes.Values.First(r => r.Name.StartsWith("§§", StringComparison.Ordinal));
            IRecipe normalRecipe = cache.Recipes.Values.First(r => !r.Name.StartsWith("§§", StringComparison.Ordinal)
                && cache.Assemblers.Values.Any(a => a.AssociatedItems.Any(i => i.ProductionRecipes.Contains(r))));
            IAssembler matchingAssembler = cache.Assemblers.Values.First(a => a.AssociatedItems.Any(i => i.ProductionRecipes.Contains(normalRecipe)));
            IRecipe unmentionedRecipe = cache.Recipes.Values.First(r => !r.Name.StartsWith("§§", StringComparison.Ordinal) && r != normalRecipe);

            var saveInfo = new SaveFileInfo();
            foreach (KeyValuePair<string, string> mod in cache.IncludedMods)
                saveInfo.Mods[mod.Key] = mod.Value;
            saveInfo.Recipes[normalRecipe.Name] = true;
            saveInfo.Recipes[unmentionedRecipe.Name] = false;

            var enabledObjects = new HashSet<IDataObjectBase>();
            var window = new SaveFileLoadWindow(cache, enabledObjects) {
                SaveFileInfo = saveInfo,
                ConfirmDialogStub = (_, _) => throw new InvalidOperationException("mods match - no mismatch dialog expected"),
            };

            await window.ProcessSaveDataAsync();

            Assert.Contains(pseudoRecipe, enabledObjects);
            Assert.Contains(normalRecipe, enabledObjects);
            Assert.DoesNotContain(unmentionedRecipe, enabledObjects);
            Assert.Contains(matchingAssembler, enabledObjects);
            if (cache.PlayerAssembler is not null)
                Assert.Contains(cache.PlayerAssembler, enabledObjects);
        }

        //Upstream's Designer sets CancelButton to this button (there's no AcceptButton here - Cancel is the
        //only user-facing action while the reader pipeline runs).
        [AvaloniaFact]
        public void CancelButton_IsCancel() {
            var window = new SaveFileLoadWindow(NewEmptyCache(), []);

            Assert.True(window.CancelButtonControl.IsCancel);
        }
    }
}
