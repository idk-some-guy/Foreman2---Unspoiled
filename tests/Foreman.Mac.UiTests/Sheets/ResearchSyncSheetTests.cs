using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using Foreman.Mac.Views.Sheets;
using Foreman.Models.Research;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Sheets {
    public class ResearchSyncSheetTests {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private const string SavePath = "/tmp/saves/Megabase.zip";
        private const string RunningStrip = "Reading “Megabase” — running Factorio.";
        private const string HeadlineMegabase = "Research synced from “Megabase”.";
        private const long PlayTicks = 8_881_200;
        private static readonly DateTime EarlierSync = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Rig : IDisposable {
            private readonly ShellFixture shell = ShellFixture.Open();

            public ProductionGraph Graph => shell.Graph;
            public DataCache Cache => shell.Window.DataCache!;
            public Window Window => shell.Window;

            public List<RecipeNode> AddTyped(params string[] recipes) {
                var nodes = new List<RecipeNode>();
                foreach (string recipe in recipes) {
                    RecipeNode node = Graph.CreateRecipeNode(new RecipeQualityPair(Cache.Recipes[recipe], Cache.DefaultQuality!), Point.Empty);
                    node.ExtraProductivityBonus = 0.1;
                    nodes.Add(node);
                }
                return nodes;
            }

            public ResearchSyncSheet Sheet(Func<string, CancellationToken, SaveFileReader.Result> load, DataCache? cache = null) =>
                new(Graph, cache ?? Cache, SavePath) { LoadPipelineStub = load };

            public void Dispose() => shell.Dispose();
        }

        private static SaveFileInfo Info(Dictionary<ResearchField, double> bonuses, Dictionary<string, double>? recipes = null,
                int technologies = 0, Dictionary<string, string>? mods = null) {
            var info = new SaveFileInfo { TicksPlayed = PlayTicks };
            foreach ((ResearchField field, double value) in bonuses)
                info.ResearchBonuses[ResearchFields.ExportKey(field)] = value;
            foreach ((string recipe, double value) in recipes ?? [])
                info.RecipeProductivity[recipe] = value;
            for (int i = 0; i < technologies; i++)
                info.Technologies["tech-" + i] = true;
            foreach ((string mod, string version) in mods ?? [])
                info.Mods[mod] = version;
            return info;
        }

        private static ResearchState StateOf(SaveFileInfo info) => ResearchStateMapper.FromSave(info, "/tmp/saves/Megabase.zip", EarlierSync);

        private static SaveFileReader.Result Ok(SaveFileInfo info) => new() { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = info };

        private static SaveFileInfo AfterInfo(Dictionary<string, string>? mods = null) => Info(new() {
            [ResearchField.MiningProductivity] = 1.5,
            [ResearchField.LabSpeed] = 0.8,
            [ResearchField.LabProductivity] = 0.2,
            [ResearchField.InserterStackBonus] = 3,
            [ResearchField.BeltStackBonus] = 1,
        }, new() { ["steel-plate"] = 0.2, ["electronic-circuit"] = 0.2 }, mods: mods);

        private static ResearchState EightChangesBefore(bool mining, bool labSpeed, bool steel) {
            ResearchState state = StateOf(Info(new() {
                [ResearchField.MiningProductivity] = 1.0,
                [ResearchField.LabSpeed] = 0.5,
                [ResearchField.LabProductivity] = 0.1,
                [ResearchField.InserterStackBonus] = 1,
                [ResearchField.RobotBattery] = 0.3,
            }, new() { ["steel-plate"] = 0.1, ["electronic-circuit"] = 0.1 }));
            if (mining)
                state = state.WithValue(ResearchField.MiningProductivity, 1.2);
            if (labSpeed)
                state = state.WithValue(ResearchField.LabSpeed, 0.6);
            if (steel)
                state = state.WithRecipe("steel-plate", 0.15);
            return state;
        }

        private static async Task UntilAsync(Func<bool> done) {
            DateTime end = DateTime.UtcNow + Timeout;
            while (!done()) {
                if (DateTime.UtcNow > end)
                    throw new TimeoutException("The condition never held.");
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(5);
            }
        }

        private static DataCache BlankCache(string preset, FactorioGameVersion? version) {
            DataCache cache = CacheVersions.Create(0, version);
            typeof(DataCache).GetProperty(nameof(DataCache.PresetName))!.SetValue(cache, preset);
            return cache;
        }

        [AvaloniaFact]
        public async Task SyncSheet_Running_CancelReplacesNothing() {
            using var rig = new Rig();
            using var gate = new ManualResetEventSlim();
            using var entered = new ManualResetEventSlim();
            ResearchState before = rig.Graph.Research;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => {
                entered.Set();
                gate.Wait(Timeout, CancellationToken.None);
                return Ok(AfterInfo());
            });

            (Avalonia.Controls.Window window, _, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);

            Assert.Equal("Sync research", sheet.Title);
            Assert.Equal("Foreman runs Factorio in the background to read the save. This can take a minute.", sheet.LedeText);
            Assert.Equal(RunningStrip, sheet.StripText);
            Assert.Equal("Sync", sheet.DefaultButtonControl.Content);
            Assert.False(sheet.DefaultButtonControl.IsEnabled);
            Assert.True(entered.Wait(Timeout));

            SheetFixture.Click(window, sheet.CancelButtonControl);

            Assert.Equal(ResearchSyncOutcome.Cancelled, await result.WaitAsync(Timeout));
            gate.Set();
            await sheet.LastRun.WaitAsync(Timeout);
            Assert.Same(before, rig.Graph.Research);
        }

        [AvaloniaFact]
        public async Task SyncSheet_ReaderFinishesAfterCancel_ReplacesNothing() {
            using var rig = new Rig();
            using var gate = new ManualResetEventSlim();
            using var entered = new ManualResetEventSlim();
            List<RecipeNode> typed = rig.AddTyped("iron-gear-wheel");
            ResearchState before = rig.Graph.Research;
            bool noticeShown = false;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => {
                entered.Set();
                gate.Wait(Timeout, CancellationToken.None);
                return Ok(AfterInfo());
            });
            sheet.FirstSyncStub = _ => { noticeShown = true; return Task.FromResult(FirstSyncChoice.Clear); };

            Task run = sheet.RunAsync();
            Assert.True(entered.Wait(Timeout));
            sheet.Cancel();
            gate.Set();
            await run.WaitAsync(Timeout);

            Assert.Equal(ResearchSyncOutcome.Cancelled, await sheet.Result.WaitAsync(Timeout));
            Assert.Same(before, rig.Graph.Research);
            Assert.False(noticeShown);
            Assert.Equal(0.1, typed[0].ExtraProductivityBonus);
        }

        [AvaloniaFact]
        public async Task SyncSheet_GraphWindowClosesDuringRun_ReplacesNothing() {
            using var rig = new Rig();
            using var gate = new ManualResetEventSlim();
            using var entered = new ManualResetEventSlim();
            List<RecipeNode> typed = rig.AddTyped("iron-gear-wheel", "copper-cable");
            ResearchState before = rig.Graph.Research;
            CancellationToken seen = default;
            bool noticeShown = false;
            ResearchSyncSheet sheet = rig.Sheet((_, token) => {
                seen = token;
                entered.Set();
                gate.Wait(Timeout, CancellationToken.None);
                return Ok(AfterInfo());
            });
            sheet.FirstSyncStub = _ => { noticeShown = true; return Task.FromResult(FirstSyncChoice.Clear); };

            Task<ResearchSyncOutcome> result = SheetHost.For(rig.Window).ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            Assert.True(entered.Wait(Timeout));

            rig.Window.Close();

            Assert.True(seen.IsCancellationRequested);
            Assert.Equal(ResearchSyncOutcome.Cancelled, await result.WaitAsync(Timeout));
            gate.Set();
            await sheet.LastRun.WaitAsync(Timeout);
            Assert.Same(before, rig.Graph.Research);
            Assert.All(typed, node => Assert.Equal(0.1, node.ExtraProductivityBonus));
            Assert.False(noticeShown);
        }

        [AvaloniaFact]
        public async Task SyncSheet_Announcements() {
            using var rig = new Rig();
            using var gate = new ManualResetEventSlim();
            var clock = new ManualTimeProvider();
            var heard = new List<string>();
            ResearchSyncSheet sheet = rig.Sheet((_, _) => {
                gate.Wait(Timeout, CancellationToken.None);
                return Ok(AfterInfo());
            });
            sheet.Clock = clock;
            sheet.Announce = heard.Add;

            Task run = sheet.RunAsync();
            Assert.Equal([RunningStrip], heard);

            clock.Advance(TimeSpan.FromSeconds(1.9));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(heard);

            clock.Advance(TimeSpan.FromSeconds(0.1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([RunningStrip, RunningStrip], heard);

            clock.Advance(TimeSpan.FromSeconds(1.9));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, heard.Count);

            gate.Set();
            await run.WaitAsync(Timeout);
            Assert.Equal(HeadlineMegabase, heard[^1]);
            Assert.Equal(0, clock.ActiveTimers);
        }

        [AvaloniaFact]
        public async Task SyncSheet_Success_ListsChanges() {
            using var rig = new Rig();
            rig.Graph.Research = EightChangesBefore(true, true, true);
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo(new() { ["base"] = "2.0.0" })), BlankCache("Vanilla 2.1", null));

            (_, SheetHost host, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);
            await sheet.LastRun.WaitAsync(Timeout);

            Assert.Equal(HeadlineMegabase, sheet.HeadlineText);
            Assert.Equal(HeadlineMegabase, sheet.Title);
            Assert.Equal(HeadlineMegabase, AutomationProperties.GetName(SheetFixture.FrameOf(host, sheet)));
            Assert.Equal("41 h 7 min played. Eight values changed, and three of them were edits made by hand.", sheet.LedeText);
            Assert.Equal(6, sheet.ChangeRows.Count);
            Assert.Equal("and 2 more", sheet.MoreText);
            Assert.Equal(("Mining productivity", "120 % → 150 %", true), sheet.ChangeRows[0]);
            Assert.Equal(("Lab speed", "60 % → 80 %", true), sheet.ChangeRows[1]);
            Assert.Equal(("Inserter capacity", "+1 → +3", false), sheet.ChangeRows[3]);
            Assert.Equal(("Belt stack size", "none → +1", false), sheet.ChangeRows[4]);
            Assert.Equal(("Robot battery", "30 % → none", false), sheet.ChangeRows[5]);
            Assert.Equal(2, sheet.ChangeRows.Count(row => row.ReplacedEdit));
            Assert.Equal(3, sheet.Report!.ReplacedEditCount);
            Assert.True(sheet.ShowResearchButtonControl.IsVisible);
            Assert.False(sheet.CancelButtonControl.IsVisible);
            Assert.Equal("Done", sheet.DefaultButtonControl.Content);
            Assert.Same(sheet.DefaultButton, sheet.DefaultButtonControl);
            Assert.Contains("replaced your edit", Texts(sheet));

            sheet.DefaultButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ResearchSyncOutcome.Synced, await result.WaitAsync(Timeout));
        }

        [AvaloniaFact]
        public async Task SyncSheet_Success_ShowResearchClosesWithShowResearch() {
            using var rig = new Rig();
            rig.Graph.Research = EightChangesBefore(false, false, false);
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));

            (_, _, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);
            await sheet.LastRun.WaitAsync(Timeout);
            sheet.ShowResearchButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(ResearchSyncOutcome.ShowResearch, await result.WaitAsync(Timeout));
        }

        [AvaloniaFact]
        public async Task SyncSheet_Success_EscapeAfterSyncIsDone() {
            using var rig = new Rig();
            rig.Graph.Research = EightChangesBefore(false, false, false);
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));

            await sheet.RunAsync().WaitAsync(Timeout);
            sheet.Cancel();

            Assert.Equal(ResearchSyncOutcome.Synced, await sheet.Result.WaitAsync(Timeout));
        }

        [AvaloniaFact]
        public async Task SyncSheet_Success_LedeCountsAndSingularForms() {
            using var rig = new Rig();

            rig.Graph.Research = EightChangesBefore(false, false, false);
            ResearchSyncSheet none = rig.Sheet((_, _) => Ok(AfterInfo()));
            await none.RunAsync().WaitAsync(Timeout);
            Assert.Equal("41 h 7 min played. Eight values changed.", none.LedeText);

            rig.Graph.Research = EightChangesBefore(true, false, false);
            ResearchSyncSheet one = rig.Sheet((_, _) => Ok(AfterInfo()));
            await one.RunAsync().WaitAsync(Timeout);
            Assert.Equal("41 h 7 min played. Eight values changed, and one of them was an edit made by hand.", one.LedeText);

            SaveFileInfo near = AfterInfo();
            SaveFileInfo older = AfterInfo();
            older.ResearchBonuses[ResearchFields.ExportKey(ResearchField.LabSpeed)] = 0.5;
            rig.Graph.Research = StateOf(older);
            ResearchSyncSheet single = rig.Sheet((_, _) => Ok(near));
            await single.RunAsync().WaitAsync(Timeout);
            Assert.Equal("41 h 7 min played. One value changed.", single.LedeText);
            Assert.Empty(single.MoreText);
        }

        [AvaloniaFact]
        public async Task SyncSheet_Success_UnknownPlayTimeDropsItsPart() {
            using var rig = new Rig();
            SaveFileInfo after = AfterInfo();
            after.TicksPlayed = null;
            rig.Graph.Research = StateOf(after);
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(after));

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal("Nothing changed since the last sync.", sheet.LedeText);
        }

        [AvaloniaFact]
        public async Task SyncSheet_FirstSync_ReadCounts() {
            using var rig = new Rig();
            SaveFileInfo info = Info(
                ResearchFields.All.ToDictionary(field => field, _ => 0.5),
                new() { ["a"] = 0.1, ["b"] = 0.2, ["c"] = 0.3 }, technologies: 214);
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(info));

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal("Read 10 bonuses, 3 recipe productivity values, and 214 technologies.", sheet.LedeText);
            Assert.Empty(sheet.ChangeRows);
            Assert.True(sheet.Report!.FirstSync);
        }

        [AvaloniaFact]
        public async Task SyncSheet_NoChanges() {
            using var rig = new Rig();
            SaveFileInfo info = AfterInfo();
            rig.Graph.Research = StateOf(info);
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(info));

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal("41 h 7 min played. Nothing changed since the last sync.", sheet.LedeText);
            Assert.Empty(sheet.ChangeRows);
        }

        [AvaloniaFact]
        public async Task SyncSheet_Failure_NothingReplaced() {
            using var rig = new Rig();
            ResearchState before = rig.Graph.Research;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => new SaveFileReader.Result {
                Outcome = SaveFileLoadOutcome.Abort,
                Failure = FactorioRunFailure.Crashed,
                CrashPhase = FactorioCrashPhase.ReadingSave,
            });

            (_, _, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);
            await sheet.LastRun.WaitAsync(Timeout);

            Assert.Equal("Couldn’t read “Megabase”, so research is unchanged. " + FailureSentences.Reason(FactorioRunFailure.Crashed, FactorioCrashPhase.ReadingSave, null), sheet.StripText);
            Assert.True(sheet.ShowLogButtonControl.IsVisible);
            Assert.Equal("Try Again", sheet.DefaultButtonControl.Content);
            Assert.Same(sheet.DefaultButton, sheet.DefaultButtonControl);
            Assert.True(sheet.DefaultButtonControl.IsEnabled);
            Assert.Same(before, rig.Graph.Research);
            Assert.False(result.IsCompleted);
        }

        [AvaloniaFact]
        public async Task SyncSheet_Failure_TryAgainReadsAgainAndSyncs() {
            using var rig = new Rig();
            int reads = 0;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => ++reads == 1
                ? new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Abort, Failure = FactorioRunFailure.AnotherInstanceRunning }
                : Ok(AfterInfo()));

            (_, _, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);
            await sheet.LastRun.WaitAsync(Timeout);
            Assert.Equal("Try Again", sheet.DefaultButtonControl.Content);

            sheet.DefaultButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await sheet.LastRun.WaitAsync(Timeout);

            Assert.Equal(2, reads);
            Assert.Equal(HeadlineMegabase, sheet.HeadlineText);
            Assert.Equal("Done", sheet.DefaultButtonControl.Content);
            Assert.False(sheet.ShowLogButtonControl.IsVisible);
            Assert.False(result.IsCompleted);
        }

        [AvaloniaFact]
        public async Task SyncSheet_Failure_ShowLogOpensTheLog() {
            using var rig = new Rig();
            int shown = 0;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Abort, Failure = FactorioRunFailure.Crashed });
            sheet.ShowLogStub = () => { shown++; return Task.CompletedTask; };

            await sheet.RunAsync().WaitAsync(Timeout);
            sheet.ShowLogButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, shown);
        }

        [AvaloniaFact]
        public async Task SyncSheet_SaveMissing_ChooseSaveDefault() {
            using var rig = new Rig();
            int reads = 0;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => { reads++; return Ok(AfterInfo()); });
            sheet.StoredSaveMissing = true;

            (_, _, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);
            await sheet.LastRun.WaitAsync(Timeout);

            Assert.Equal("Couldn’t find “Megabase”. It was moved or deleted after the last sync.", sheet.StripText);
            Assert.True(sheet.CancelButtonControl.IsVisible);
            Assert.False(sheet.ShowLogButtonControl.IsVisible);
            Assert.Equal("Cancel", sheet.CancelButtonControl.Content);
            Assert.Equal("Choose Save…", sheet.DefaultButtonControl.Content);
            Assert.Same(sheet.DefaultButton, sheet.DefaultButtonControl);

            sheet.DefaultButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(ResearchSyncOutcome.ChooseAnotherSave, await result.WaitAsync(Timeout));
            Assert.Equal(0, reads);
        }

        [AvaloniaFact]
        public async Task SyncSheet_ModMismatch_WarningBlockNoConfirm() {
            using var rig = new Rig();
            DataCache preset = BlankCache("Vanilla 2.1", null);
            rig.Graph.Research = EightChangesBefore(false, false, false);
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo(new() { ["krastorio2"] = "1.3.0" })), preset);

            (_, SheetHost host, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);
            await sheet.LastRun.WaitAsync(Timeout);

            Assert.Equal("The mods in “Megabase” don’t match the preset “Vanilla 2.1”. Research for recipes the preset doesn’t have is kept but has no effect.", sheet.MismatchText);
            Assert.Equal("Not in the preset: krastorio2", sheet.MismatchLinesText);
            Assert.Equal(HeadlineMegabase, sheet.HeadlineText);
            SheetFixture.AssertNoAlert(host);
            Assert.False(result.IsCompleted);
        }

        [AvaloniaFact]
        public async Task SyncSheet_NoMismatch_HidesTheWarningBlock() {
            using var rig = new Rig();
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo(new Dictionary<string, string>(rig.Cache.IncludedMods))));

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Empty(sheet.MismatchText);
            Assert.Empty(sheet.MismatchLinesText);
        }

        [AvaloniaFact]
        public async Task SyncSheet_NewerGame_Note() {
            using var rig = new Rig();
            DataCache preset = BlankCache("Vanilla 2.1", new FactorioGameVersion(2, 1, 24));
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo(new() { ["base"] = "2.1.30" })), preset);

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal("“Megabase” comes from Factorio 2.1.30; the preset “Vanilla 2.1” is from 2.1.24. To match them, import the preset again in Settings > Presets.", sheet.VersionNoteText);
        }

        [AvaloniaFact]
        public async Task SyncSheet_SameOrOlderGame_NoNote() {
            using var rig = new Rig();
            DataCache preset = BlankCache("Vanilla 2.1", new FactorioGameVersion(2, 1, 30));
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo(new() { ["base"] = "2.1.30" })), preset);

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Empty(sheet.VersionNoteText);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_NotShownOnceTheGraphHasSynced() {
            using var rig = new Rig();
            List<RecipeNode> typed = rig.AddTyped("iron-gear-wheel");
            rig.Graph.Research = EightChangesBefore(false, false, false);
            bool shown = false;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));
            sheet.FirstSyncStub = _ => { shown = true; return Task.FromResult(FirstSyncChoice.Clear); };

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.False(shown);
            Assert.Equal(0.1, typed[0].ExtraProductivityBonus);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_NotShownWithoutTypedProductivity() {
            using var rig = new Rig();
            bool shown = false;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));
            sheet.FirstSyncStub = _ => { shown = true; return Task.FromResult(FirstSyncChoice.Clear); };

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.False(shown);
            Assert.Equal(HeadlineMegabase, sheet.HeadlineText);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_FourNodes() {
            using var rig = new Rig();
            rig.AddTyped("electronic-circuit", "copper-cable", "plastic-bar", "steel-plate");
            string headline = "", text = "";
            string[] labels = [];
            string defaultLabel = "";
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));
            sheet.FirstSyncStub = notice => {
                headline = notice.HeadlineText;
                text = notice.InformativeText;
                labels = [(string)notice.KeepButtonControl.Content!, (string)notice.CancelButtonControl.Content!, (string)notice.ClearButtonControl.Content!];
                defaultLabel = (string)((Button)notice.DefaultButton!).Content!;
                return Task.FromResult(FirstSyncChoice.Keep);
            };

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal("Four nodes already have extra productivity typed in.", headline);
            Assert.Equal("Research from the save now adds productivity by itself. Clear the typed values so it isn’t counted twice, or keep them as extras on top of research.\n\nElectronic circuit, Copper cable, Plastic bar, and Steel plate", text);
            Assert.Equal(["Keep Them", "Cancel", "Clear Them"], labels);
            Assert.Equal("Clear Them", defaultLabel);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_OneNode() {
            using var rig = new Rig();
            rig.AddTyped("iron-gear-wheel");
            string headline = "", text = "";
            string[] labels = [];
            bool clearIsDefault = false, keepIsDefault = true, cancelIsDefault = true;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));
            sheet.FirstSyncStub = notice => {
                headline = notice.HeadlineText;
                text = notice.InformativeText;
                labels = [(string)notice.KeepButtonControl.Content!, (string)notice.CancelButtonControl.Content!, (string)notice.ClearButtonControl.Content!];
                clearIsDefault = notice.ClearButtonControl.IsDefault;
                keepIsDefault = notice.KeepButtonControl.IsDefault;
                cancelIsDefault = notice.CancelButtonControl.IsDefault;
                return Task.FromResult(FirstSyncChoice.Keep);
            };

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal("One node already has extra productivity typed in.", headline);
            Assert.Equal("Research from the save now adds productivity by itself. Clear the typed value so it isn’t counted twice, or keep it as an extra on top of research.\n\nIron gear wheel", text);
            Assert.Equal(["Keep It", "Cancel", "Clear It"], labels);
            Assert.True(clearIsDefault);
            Assert.False(keepIsDefault);
            Assert.False(cancelIsDefault);
        }

        [AvaloniaFact]
        public void FirstSync_Notice_TwelveNodes_DigitsAndFiveNamesThenTheRest() {
            string[] names = [.. Enumerable.Range(1, 12).Select(i => "Recipe " + i)];
            var notice = new FirstSyncNoticeSheet(names);

            Assert.StartsWith("12 nodes", notice.HeadlineText);
            Assert.EndsWith("\n\nRecipe 1, Recipe 2, Recipe 3, Recipe 4, Recipe 5, and 7 more", notice.InformativeText);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_ClearZeroesTypedValues() {
            using var rig = new Rig();
            List<RecipeNode> typed = rig.AddTyped("iron-gear-wheel", "copper-cable");
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));
            sheet.FirstSyncStub = _ => Task.FromResult(FirstSyncChoice.Clear);

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.All(typed, node => Assert.Equal(0, node.ExtraProductivityBonus));
            Assert.Equal(2, sheet.Report!.ClearedNodeCount);
            Assert.NotNull(rig.Graph.Research.Source);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_KeepLeavesTypedValues() {
            using var rig = new Rig();
            List<RecipeNode> typed = rig.AddTyped("iron-gear-wheel", "copper-cable");
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));
            sheet.FirstSyncStub = _ => Task.FromResult(FirstSyncChoice.Keep);

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.All(typed, node => Assert.Equal(0.1, node.ExtraProductivityBonus));
            Assert.Equal(0, sheet.Report!.ClearedNodeCount);
            Assert.NotNull(rig.Graph.Research.Source);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_CancelClosesTheSheetAndReplacesNothing() {
            using var rig = new Rig();
            List<RecipeNode> typed = rig.AddTyped("iron-gear-wheel");
            ResearchState before = rig.Graph.Research;
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));
            sheet.FirstSyncStub = _ => Task.FromResult(FirstSyncChoice.Cancel);

            await sheet.RunAsync().WaitAsync(Timeout);

            Assert.Equal(ResearchSyncOutcome.Cancelled, await sheet.Result.WaitAsync(Timeout));
            Assert.Same(before, rig.Graph.Research);
            Assert.Equal(0.1, typed[0].ExtraProductivityBonus);
            Assert.Null(sheet.Report);
        }

        [AvaloniaFact]
        public async Task FirstSync_Notice_StacksOnTheSheetAndClearIsApplied() {
            using var rig = new Rig();
            List<RecipeNode> typed = rig.AddTyped("iron-gear-wheel");
            ResearchSyncSheet sheet = rig.Sheet((_, _) => Ok(AfterInfo()));

            (_, SheetHost host, Task<ResearchSyncOutcome> result) = SheetFixture.Present(sheet);
            await UntilAsync(() => host.GetVisualDescendants().OfType<FirstSyncNoticeSheet>().Any());
            FirstSyncNoticeSheet notice = host.GetVisualDescendants().OfType<FirstSyncNoticeSheet>().Single();
            Assert.Same(notice.ClearButtonControl, notice.DefaultButton);
            Assert.Equal("One node already has extra productivity typed in.", AutomationProperties.GetName(notice.FrameControl));

            notice.ClearButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await sheet.LastRun.WaitAsync(Timeout);

            Assert.Equal(0, typed[0].ExtraProductivityBonus);
            Assert.Equal(HeadlineMegabase, sheet.HeadlineText);
            Assert.False(result.IsCompleted);
        }

        private static List<string> Texts(Control root) => [.. root.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? "")];
    }
}
