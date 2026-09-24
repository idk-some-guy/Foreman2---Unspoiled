using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers the save-file load sheet (io-reference.md §5): the picker cancel path, SaveFileReader.Result onto
    //Outcome and SaveFileInfo, the failure strip and its retry, the mod-mismatch confirm, the enabled-object
    //derivation, and Cancel's honest non-preemptive behaviour. SaveFileReader's own pipeline is covered against
    //StubFactorioHarness in ForemanTest; LoadPipelineStub stands in for it here.
    public class SaveFileLoadWindowTests {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

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
                ConfirmDialogStub = (_, _) => throw new InvalidOperationException("should not confirm"),
                LoadPipelineStub = (_, _) => throw new InvalidOperationException("should not run the pipeline"),
            };

            await sheet.RunAsync().WaitAsync(Timeout);

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
            };

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal(SaveFileLoadOutcome.Ok, sheet.Outcome);
            Assert.Same(saveInfo, sheet.SaveFileInfo);
            Assert.Equal("/tmp/saves", sheet.ResolvedSaveFileLocation);
        }

        [AvaloniaFact]
        public async Task RunAsync_ReadFails_ShowsTheFailureStrip_KeepsTheSheetOpen_AndTryAgainRereadsTheSamePath() {
            int picks = 0;
            var paths = new List<string>();
            var results = new Queue<SaveFileReader.Result>([
                new SaveFileReader.Result {
                    Outcome = SaveFileLoadOutcome.Abort,
                    Failure = FactorioRunFailure.Crashed,
                    CrashPhase = FactorioCrashPhase.ReadingSave,
                    WarningMessage = "Factorio crashed while reading the save file.",
                },
                new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = new SaveFileInfo() },
            ]);
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => { picks++; return Task.FromResult<string?>("/tmp/saves/Megabase v3.zip"); },
                LoadPipelineStub = (path, _) => { paths.Add(path); return results.Dequeue(); },
                ConfirmDialogStub = (_, _) => Task.FromResult(true),
            };
            (Window window, SheetHost host, Task<SaveFileLoadOutcome?> task) = SheetFixture.Present(sheet);
            await WaitForAsync(() => sheet.Activity?.Kind == SheetActivityKind.Failed);
            AnimationClock.Settle();

            SheetFixture.AssertNoAlert(host);
            Assert.False(task.IsCompleted);
            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Error, strip.Shape);
            Assert.Equal("Couldn’t read “Megabase v3”. Factorio crashed while reading the save file. A mod is the usual cause; see factorio-current.log.", strip.Sentence);
            Assert.Equal("Try Again", sheet.LoadButtonControl.Content);
            Assert.True(sheet.LoadButtonControl.IsEffectivelyEnabled);
            Assert.True(sheet.ShowLogButtonControl.IsEffectivelyVisible);
            Assert.Null(sheet.ResolvedSaveFileLocation);

            SheetFixture.Click(window, sheet.LoadButtonControl);
            Assert.Equal(SaveFileLoadOutcome.Ok, await task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, picks);
            Assert.Equal(["/tmp/saves/Megabase v3.zip", "/tmp/saves/Megabase v3.zip"], paths);
        }

        [AvaloniaFact]
        public async Task RunAsync_InstallTooOld_ReadsTheValidatorSentenceInTheStrip() {
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result {
                    Outcome = SaveFileLoadOutcome.Cancel,
                    Failure = FactorioRunFailure.InvalidInstall,
                    InstallCheck = new FactorioInstallCheck(FactorioInstallProblem.TooOld, "1.1.0"),
                    WarningMessage = "Factorio Version below 2.0 can not be used with this version of Foreman.",
                },
            };
            (_, SheetHost host, Task<SaveFileLoadOutcome?> task) = SheetFixture.Present(sheet);
            await WaitForAsync(() => sheet.Activity?.Kind == SheetActivityKind.Failed);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Couldn’t read “mysave”. Factorio 1.1.0 is too old for this Foreman. Use Factorio 2.0.7 or later.", sheet.Activity!.Sentence);
            Assert.False(task.IsCompleted);
        }

        [AvaloniaFact]
        public async Task ShowLog_AfterAFailure_OpensTheLog() {
            int opened = 0;
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Abort, Failure = FactorioRunFailure.Unmapped },
                ShowLogStub = () => { opened++; return Task.CompletedTask; },
            };
            (Window window, SheetHost host, _) = SheetFixture.Present(sheet);
            await WaitForAsync(() => sheet.Activity?.Kind == SheetActivityKind.Failed);
            AnimationClock.Settle();

            Assert.Equal("Couldn’t read “mysave”. The details are in Foreman’s log.", SheetFixture.StripOf(host).Sentence);
            SheetFixture.Click(window, sheet.ShowLogButtonControl);
            Assert.Equal(1, opened);
        }

        private static async Task WaitForAsync(Func<bool> condition) {
            for (int i = 0; i < 200 && !condition(); i++) {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(condition());
        }

        [AvaloniaFact]
        public async Task RunAsync_PipelineReturnsCancelWithNoMessage_ClosesWithoutAnyDialog() {
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Cancel },
            };

            await sheet.RunAsync().WaitAsync(Timeout);

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

        //Escape and the Cancel button share Cancel(), so Escape cancels the in-flight pipeline's token too.
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
                SavePath = "/tmp/saves/Megabase v3.zip",
            };
            string? capturedTitle = null;
            string? capturedMessage = null;
            sheet.ConfirmDialogStub = (title, message) => { capturedTitle = title; capturedMessage = message; return Task.FromResult(false); };

            await sheet.ProcessSaveDataAsync().WaitAsync(Timeout);

            Assert.Equal("Use this save anyway?", capturedTitle);
            Assert.StartsWith($"The mods in “Megabase v3” don’t match the preset “{cache.PresetName}”. Only recipes the preset contains are enabled.\nNot in the save: ", capturedMessage);
            Assert.DoesNotContain("!", capturedTitle + capturedMessage);
            Assert.DoesNotContain("Are you sure", capturedMessage);
            Assert.Empty(enabledObjects);
        }

        [Fact]
        public void MismatchLines_NameEachSideInOrder_WithASerialAnd_CappedAtFiveNames() {
            Assert.Equal("", SaveFileLoadSheet.MismatchLines([], [], []));
            Assert.Equal("\nNot in the save: space-exploration and krastorio2",
                SaveFileLoadSheet.MismatchLines(["space-exploration", "krastorio2"], [], []));
            Assert.Equal("\nNot in the save: a\nDifferent version: flib\nNot in the preset: b, c, and d",
                SaveFileLoadSheet.MismatchLines(["a"], ["flib"], ["b", "c", "d"]));
            Assert.Equal("\nNot in the preset: m1, m2, m3, m4, m5, and 3 more",
                SaveFileLoadSheet.MismatchLines([], [], ["m1", "m2", "m3", "m4", "m5", "m6", "m7", "m8"]));
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

            await sheet.ProcessSaveDataAsync().WaitAsync(Timeout);

            Assert.Contains(pseudoRecipe, enabledObjects);
            Assert.Contains(normalRecipe, enabledObjects);
            Assert.DoesNotContain(unmentionedRecipe, enabledObjects);
            Assert.Contains(matchingAssembler, enabledObjects);
            if (cache.PlayerAssembler is not null)
                Assert.Contains(cache.PlayerAssembler, enabledObjects);
        }

        //---- the mod-mismatch confirm stacks on the still-open sheet instead of queuing behind it -----------

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
            Assert.Equal(["Cancel", "Use Save"], confirm.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.Same(confirm.YesButtonControl, confirm.Buttons.ResolveDefaultButton());
            AnimationClock.Settle();
            Border alertPanel = confirm.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");
            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), alertPanel.Bounds.Width, 0.5);
            confirm.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await processTask; //no deadlock: the stacked confirm resolves and ProcessSaveDataAsync completes on its own
            Assert.NotEmpty(enabledObjects);
            Assert.False(outerTask.IsCompleted); //the outer sheet is still open - its own RunAsync never got a save path
        }

        //---- the kit sheet: named width, the reading strip, Show Log… · Cancel · Load ---------------------------

        [AvaloniaFact]
        public void SaveFileLoad_Width_IsTheNamedSheetWidth() {
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => new TaskCompletionSource<string?>().Task,
            };
            (_, SheetHost host, _) = SheetFixture.Present(sheet);

            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Load a Factorio save", SheetFixture.FrameOf(host, sheet).Title);
            Assert.Equal(["Show Log…", "Cancel", "Load"], sheet.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.False(sheet.ShowLogButtonControl.IsVisible);
            Assert.Same(sheet.LoadButtonControl, sheet.Buttons.ResolveDefaultButton());
        }

        [AvaloniaFact]
        public async Task SaveFileLoad_WhileReading_ShowsTheStageSentence_DisablesLoad_AndKeepsCancelLive() {
            var pipelineStarted = new SemaphoreSlim(0, 1);
            var pipelineGate = new TaskCompletionSource();
            var sheet = new SaveFileLoadSheet(NewEmptyCache(), []) {
                OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip"),
                LoadPipelineStub = (_, _) => {
                    pipelineStarted.Release();
                    pipelineGate.Task.Wait(CancellationToken.None);
                    return new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Cancel };
                },
            };
            (_, SheetHost host, Task<SaveFileLoadOutcome?> task) = SheetFixture.Present(sheet);
            await pipelineStarted.WaitAsync();
            AnimationClock.Settle(500);

            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Loading, strip.Shape);
            Assert.Equal("Reading “mysave” — running Factorio.", strip.Sentence);
            Assert.False(sheet.LoadButtonControl.IsEnabled);
            Assert.True(sheet.CancelButtonControl.IsEffectivelyEnabled);

            sheet.CancelButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SaveFileLoadOutcome.Cancel, await task);
            pipelineGate.TrySetResult();
        }
    }
}
