using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers io-reference.md §5's SaveFileLoadForm contract (phase 6 Task 5, moved onto SheetHost by C2c
    //Task 2) at the sheet level: the file-picker cancel path, wiring SaveFileReader.Result onto
    //Outcome/SaveFileInfo/dialogs, the mod-mismatch confirm gate and the §§-pseudo-recipe/save-driven/
    //transitive-derive enabled-object pipeline (ProcessSaveData), and the Cancel button's honest
    //non-preemptive behavior (docs/io-reference.md §4 caution). SaveFileReader's own process-pipeline
    //correctness (exe discovery, crash/another-instance/missing-marker detection, P0 parse) is covered
    //separately against StubFactorioHarness under ForemanTest (Foreman.Core), where that harness lives -
    //LoadPipelineStub stands in for it here.
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
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>(null),
                WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn"),
                ConfirmDialogStub = (_, _) => throw new InvalidOperationException("should not confirm"),
                LoadPipelineStub = (_, _) => throw new InvalidOperationException("should not run the pipeline"),
            };

            await sheet.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Cancel, sheet.Outcome);
            Assert.Null(sheet.SaveFileInfo);
        }

        //---- presenting the sheet triggers RunAsync (reference §5: "runs immediately on show") -----------

        //Every other test in this file drives RunAsync directly through LoadFromSaveDialogStub or a direct
        //call - none of them exercise the constructor's own "Loaded += (_, _) => _ = RunAsync();" line. A
        //real SheetHost presentation is the only way to prove that wiring still fires.
        [AvaloniaFact]
        public async Task Presenting_FiresLoadedWhichTriggersRunAsync_ReachesTheStubbedOutcome() {
            var saveInfo = new SaveFileInfo();
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = saveInfo },
            };
            var window = new Window { Width = 800, Height = 600 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            Task<SaveFileLoadOutcome?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            SaveFileLoadOutcome? outcome = await task;

            Assert.Equal(SaveFileLoadOutcome.Ok, outcome);
            Assert.Equal(SaveFileLoadOutcome.Ok, sheet.Outcome);
            Assert.Same(saveInfo, sheet.SaveFileInfo);
            Assert.Equal("/tmp/saves", sheet.ResolvedSaveFileLocation);
        }

        //---- wiring SaveFileReader.Result onto Outcome/SaveFileInfo/dialogs (reference §5 steps 3-6) ----

        [AvaloniaFact]
        public async Task RunAsync_PipelineReturnsOk_AppliesResultAndResolvesTheSaveLocation() {
            var saveInfo = new SaveFileInfo();
            saveInfo.Recipes["iron-plate"] = true;
            string savePath = "/tmp/saves/mysave.zip";
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>(savePath),
                LoadPipelineStub = (path, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = saveInfo },
                WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn"),
            };

            await sheet.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Ok, sheet.Outcome);
            Assert.Same(saveInfo, sheet.SaveFileInfo);
            Assert.Equal("/tmp/saves", sheet.ResolvedSaveFileLocation);
        }

        [AvaloniaFact]
        public async Task RunAsync_PipelineReturnsAbortWithAWarningMessage_ShowsItVerbatimAndDoesNotProcess() {
            string? capturedMessage = null;
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result {
                    Outcome = SaveFileLoadOutcome.Abort,
                    WarningMessage = "Factorio crashed while reading the save file.",
                },
                WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; },
            };

            await sheet.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Abort, sheet.Outcome);
            Assert.Equal("Factorio crashed while reading the save file.", capturedMessage);
            Assert.Null(sheet.ResolvedSaveFileLocation);
        }

        [AvaloniaFact]
        public async Task RunAsync_PipelineReturnsCancelWithNoMessage_ClosesWithoutAnyDialog() {
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Cancel },
                WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn"),
            };

            await sheet.RunAsync();

            Assert.Equal(SaveFileLoadOutcome.Cancel, sheet.Outcome);
        }

        //---- Cancel button honesty (reference §4 caution: Run isn't preemptive) -------------------------

        [AvaloniaFact]
        public async Task SimulateCancelClick_SetsCancelOutcomeImmediately_WithoutWaitingForTheInFlightRun() {
            var pipelineStarted = new SemaphoreSlim(0, 1);
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, token) => {
                    pipelineStarted.Release();
                    Thread.Sleep(500); //stands in for FactorioBenchmarkRunner.Run's non-preemptive ReadToEnd loop.
                    return new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = new SaveFileInfo() };
                },
            };

            Task runTask = sheet.RunAsync();
            await pipelineStarted.WaitAsync();

            sheet.SimulateCancelClick();

            Assert.Equal(SaveFileLoadOutcome.Cancel, sheet.Outcome);
            Assert.Null(sheet.SaveFileInfo);
            Assert.True(sheet.Disposed);
            await runTask; //let the still-running pipeline finish so nothing leaks past the test.
            Assert.Equal(SaveFileLoadOutcome.Cancel, sheet.Outcome); //the late Ok result must not overwrite it.
        }

        //Fix round 1, item 1: SheetHost's Escape handling calls the exact same (now virtual) Cancel()
        //SimulateCancelClick/the Cancel button call, so driving it through a real Escape keypress on a
        //presented sheet must cancel the in-flight pipeline's token too, not just resolve the Task.
        [AvaloniaFact]
        public async Task Escape_WhilePipelineInFlight_CancelsTheTokenAndDisposesTheSheet() {
            var pipelineStarted = new SemaphoreSlim(0, 1);
            var pipelineGate = new TaskCompletionSource();
            CancellationToken capturedToken = default;
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, token) => {
                    capturedToken = token;
                    pipelineStarted.Release();
                    pipelineGate.Task.Wait(CancellationToken.None);
                    return new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = new SaveFileInfo() };
                },
            };
            var window = new Window { Width = 800, Height = 600 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            Task<SaveFileLoadOutcome?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            await pipelineStarted.WaitAsync();

            CanvasKeys.KeyDownOn(sheet.CancelButtonControl, Key.Escape);

            Assert.Equal(SaveFileLoadOutcome.Cancel, await task);
            Assert.True(capturedToken.IsCancellationRequested);
            Assert.True(sheet.Disposed);

            pipelineGate.TrySetResult(); //let the still-blocked background thread finish so nothing leaks past the test.
        }

        //---- ProcessSaveData: mod-mismatch gate + §§/save-recipe/transitive-derive pipeline (reference §5) -

        [AvaloniaFact]
        public async Task ProcessSaveDataAsync_ModsMismatchAndConfirmDeclined_LeavesEnabledObjectsEmpty() {
            DataCache cache = await GetVanillaCacheAsync();
            var enabledObjects = new HashSet<IDataObjectBase>();
            var sheet = new SaveFileLoadSheet(cache, enabledObjects) {
                SaveFileInfo = new SaveFileInfo(),
            };
            string? capturedTitle = null;
            string? capturedMessage = null;
            sheet.ConfirmDialogStub = (title, message) => { capturedTitle = title; capturedMessage = message; return Task.FromResult(false); };

            await sheet.ProcessSaveDataAsync();

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
            var sheet = new SaveFileLoadSheet(cache, enabledObjects) {
                SaveFileInfo = saveInfo,
                ConfirmDialogStub = (_, _) => throw new InvalidOperationException("mods match - no mismatch dialog expected"),
            };

            await sheet.ProcessSaveDataAsync();

            Assert.Contains(pseudoRecipe, enabledObjects);
            Assert.Contains(normalRecipe, enabledObjects);
            Assert.DoesNotContain(unmentionedRecipe, enabledObjects);
            Assert.Contains(matchingAssembler, enabledObjects);
            if (cache.PlayerAssembler is not null)
                Assert.Contains(cache.PlayerAssembler, enabledObjects);
        }

        //---- fix round 1 (item 3c): the mod-mismatch confirm stacks on top of the still-open sheet instead
        //---- of queuing behind its own dismissal - a real end-to-end proof there's no deadlock. -----------

        [AvaloniaFact]
        public async Task ProcessSaveDataAsync_ModsMismatch_RealConfirmStacksOnTheOpenSheet_AndResolvesWithoutDeadlock() {
            DataCache cache = await GetVanillaCacheAsync();
            var enabledObjects = new HashSet<IDataObjectBase>();
            var sheet = new SaveFileLoadSheet(cache, enabledObjects) {
                SaveFileInfo = new SaveFileInfo(),
                OpenSaveFilePathStub = () => new TaskCompletionSource<string?>().Task, //never resolves - keeps RunAsync from racing ProcessSaveDataAsync below
            };
            var window = new Window { Width = 800, Height = 600 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            Task<SaveFileLoadOutcome?> outerTask = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsOpen);

            Task processTask = sheet.ProcessSaveDataAsync();
            Dispatcher.UIThread.RunJobs();
            if (processTask.IsFaulted)
                throw processTask.Exception!;

            AlertSheet confirm = host.GetVisualDescendants().OfType<AlertSheet>().Single();
            confirm.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await processTask; //no deadlock: the stacked confirm resolves and ProcessSaveDataAsync completes on its own
            Assert.NotEmpty(enabledObjects);
            Assert.False(outerTask.IsCompleted); //the outer sheet is still open - its own RunAsync never got a save path
        }

        //---- SheetHost contract (C2c Task 2, fix round 1): width from the sheets table. There is no default
        //button here (Cancel is the only user-facing action while the reader pipeline runs, same as before)
        //so Enter has no counterpart to assert. Escape now routes through this sheet's own overridden
        //Cancel() (fix round 1, item 1), the same method the Cancel button calls, so it carries the same
        //cts.Cancel()/Outcome bookkeeping. -----------------------------------------------------------------

        [AvaloniaFact]
        public void SheetWidth_Is300() {
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>(null),
            };

            Assert.Equal(300, sheet.SheetWidth);
        }

        [AvaloniaFact]
        public async Task Escape_ResolvesTheSheetWithTheCancelOutcome() {
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => new TaskCompletionSource<string?>().Task, //never resolves - the picker stays open
            };
            var window = new Window { Width = 800, Height = 600 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            Task<SaveFileLoadOutcome?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet.CancelButtonControl, Key.Escape);

            Assert.Equal(SaveFileLoadOutcome.Cancel, await task);
            Assert.False(host.IsOpen);
            Assert.True(sheet.Disposed);
        }
    }
}
