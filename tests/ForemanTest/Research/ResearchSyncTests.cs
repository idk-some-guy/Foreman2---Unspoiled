using Foreman;
using Foreman.DataCaching;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Models.Research;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ForemanTest.Research {
    [TestClass]
    public class ResearchSyncTests : ForemanTestBase {
        private const string SavePath = "/tmp/x/saves/Megabase.zip";
        private static readonly DateTime SyncedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class FixedClock(DateTimeOffset now) : TimeProvider {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private static SaveFileReader.Result FixtureResult() =>
            new() {
                Outcome = SaveFileLoadOutcome.Ok,
                SaveFileInfo = SaveFileReader.ParseP0Export(File.ReadAllText(TestAssets.ResolvePath("save-reader-2.1-researched.txt"))),
            };

        private static ResearchState Synced(double mining, double steel, params (string Recipe, double Value)[] extra) {
            var info = new SaveFileInfo();
            info.ResearchBonuses[ResearchFields.ExportKey(ResearchField.MiningProductivity)] = mining;
            info.RecipeProductivity["steel-plate"] = steel;
            foreach ((string recipe, double value) in extra)
                info.RecipeProductivity[recipe] = value;
            return ResearchStateMapper.FromSave(info, SavePath, SyncedAt);
        }

        private static ProductionGraph TwoRecipeGraph() {
            var builder = GraphBuilder.Create();
            builder.Link(
                builder.Supply("Ore"),
                builder.Recipe().Input("Ore", 1).Output("Plate", 1),
                builder.Recipe().Input("Plate", 1).Output("Gear", 1),
                builder.Consumer("Gear").Target(10));
            return builder.Build().Graph;
        }

        [TestMethod]
        public async Task ReadAsync_ResearchedFixture_BuildsStateWithoutTouchingTheGraph() {
            ProductionGraph graph = TwoRecipeGraph();
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 10, 14, 5, 0, TimeSpan.Zero));

            ResearchRead read = await ResearchSync.ReadAsync(SavePath, CancellationToken.None, (_, _) => FixtureResult(), clock);

            Assert.IsTrue(read.Succeeded);
            Assert.AreEqual(new DateTime(2026, 11, 10, 14, 5, 0, DateTimeKind.Utc), read.State!.Source!.SyncedAtUtc);
            Assert.AreSame(ResearchState.None, graph.Research);
        }

        [TestMethod]
        public async Task ReadAsync_SaveWithBaseMod_ReportsItsGameVersion() {
            SaveFileReader.Result result = FixtureResult();
            result.SaveFileInfo!.Mods["base"] = "2.1.4";

            ResearchRead read = await ResearchSync.ReadAsync(SavePath, CancellationToken.None, (_, _) => result);

            Assert.AreEqual(new FactorioGameVersion(2, 1, 4), read.SaveGameVersion);
            Assert.AreEqual("2.1.4", read.SaveMods["base"]);
        }

        [TestMethod]
        public async Task ReadAsync_Crash_CarriesFailureAndNoState() {
            var crashed = new SaveFileReader.Result {
                Outcome = SaveFileLoadOutcome.Abort,
                Failure = FactorioRunFailure.Crashed,
                CrashPhase = FactorioCrashPhase.ReadingSave,
            };

            ResearchRead read = await ResearchSync.ReadAsync(SavePath, CancellationToken.None, (_, _) => crashed);

            Assert.IsFalse(read.Succeeded);
            Assert.IsNull(read.State);
            Assert.AreEqual(FactorioRunFailure.Crashed, read.Failure);
            Assert.AreEqual(FactorioCrashPhase.ReadingSave, read.CrashPhase);
        }

        [TestMethod]
        public async Task ReadAsync_CancelledBeforeFinish_IsCancel() {
            using var source = new CancellationTokenSource();
            source.Cancel();

            ResearchRead read = await ResearchSync.ReadAsync(SavePath, source.Token,
                (_, token) => token.IsCancellationRequested ? new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Cancel } : FixtureResult());

            Assert.AreEqual(SaveFileLoadOutcome.Cancel, read.Outcome);
            Assert.IsNull(read.State);
            Assert.IsFalse(read.Succeeded);
        }

        [TestMethod]
        public void Apply_FirstSync_ReportsCountsAndNoChangeList() {
            ProductionGraph graph = TwoRecipeGraph();
            ResearchState state = ResearchStateMapper.FromSave(FixtureResult().SaveFileInfo!, SavePath, SyncedAt);

            ResearchSyncReport report = ResearchSync.Apply(graph, state, false);

            Assert.IsTrue(report.FirstSync);
            Assert.AreEqual(0, report.Changes.Count);
            Assert.AreEqual(0, report.ReplacedEditCount);
            Assert.AreEqual(state.Values.Count, report.BonusCount);
            Assert.AreEqual(state.RecipeProductivity.Count, report.RecipeCount);
            Assert.AreEqual(state.Technologies.Count, report.TechnologyCount);
            Assert.AreSame(state, graph.Research);
        }

        [TestMethod]
        public void Apply_SecondSync_IsNotFirstSync() {
            ProductionGraph graph = TwoRecipeGraph();
            ResearchSync.Apply(graph, Synced(1.0, 0.1), false);

            ResearchSyncReport report = ResearchSync.Apply(graph, Synced(1.0, 0.1), false);

            Assert.IsFalse(report.FirstSync);
        }

        [TestMethod]
        public void Apply_SecondSync_ListsChangedFieldsInOrder() {
            ProductionGraph graph = TwoRecipeGraph();
            ResearchSync.Apply(graph, Synced(1.0, 0.1), false);

            ResearchSyncReport report = ResearchSync.Apply(graph, Synced(1.2, 0.2), false);

            Assert.AreEqual(2, report.Changes.Count);
            Assert.AreEqual(new ResearchChange(ResearchField.MiningProductivity, null, 1.0, 1.2, false), report.Changes[0]);
            Assert.AreEqual(new ResearchChange(null, "steel-plate", 0.1, 0.2, false), report.Changes[1]);
        }

        [TestMethod]
        public void Apply_ReplacesHandEditAndFlagsIt() {
            ProductionGraph graph = TwoRecipeGraph();
            ResearchSync.Apply(graph, Synced(1.0, 0.1), false);
            graph.Research = graph.Research.WithValue(ResearchField.MiningProductivity, 5.0);

            ResearchSyncReport report = ResearchSync.Apply(graph, Synced(1.2, 0.1), false);

            Assert.AreEqual(1, report.ReplacedEditCount);
            Assert.IsTrue(report.Changes[0].ReplacedEdit);
            Assert.AreEqual(5.0, report.Changes[0].Old);
            Assert.AreEqual(1.2, report.Changes[0].New);
        }

        [TestMethod]
        public void Apply_HandAddedRecipeMissingFromSave_ReportedAsReplacedEdit() {
            ProductionGraph graph = TwoRecipeGraph();
            ResearchSync.Apply(graph, Synced(1.0, 0.1), false);
            graph.Research = graph.Research.WithRecipe("plastic-bar", 0.1);

            ResearchSyncReport report = ResearchSync.Apply(graph, Synced(1.0, 0.1), false);

            Assert.AreEqual(1, report.Changes.Count);
            Assert.AreEqual(new ResearchChange(null, "plastic-bar", 0.1, null, true), report.Changes[0]);
            Assert.AreEqual(1, report.ReplacedEditCount);
        }

        [TestMethod]
        public void Apply_NothingChanged_EmptyChanges() {
            ProductionGraph graph = TwoRecipeGraph();
            ResearchSync.Apply(graph, Synced(1.0, 0.1), false);

            ResearchSyncReport report = ResearchSync.Apply(graph, Synced(1.0, 0.1), false);

            Assert.AreEqual(0, report.Changes.Count);
        }

        [TestMethod]
        public void Apply_ClearTypedBonuses_ZeroesThemOnce() {
            ProductionGraph graph = TwoRecipeGraph();
            foreach (RecipeNode node in graph.Nodes.OfType<RecipeNode>())
                node.ExtraProductivityBonus = 0.1;

            ResearchSyncReport report = ResearchSync.Apply(graph, Synced(1.0, 0.1), true);

            Assert.AreEqual(2, report.ClearedNodeCount);
            Assert.IsTrue(graph.Nodes.OfType<RecipeNode>().All(node => node.ExtraProductivityBonus == 0));
        }

        [TestMethod]
        public void Apply_KeepTypedBonuses_LeavesThem() {
            ProductionGraph graph = TwoRecipeGraph();
            foreach (RecipeNode node in graph.Nodes.OfType<RecipeNode>())
                node.ExtraProductivityBonus = 0.1;

            ResearchSyncReport report = ResearchSync.Apply(graph, Synced(1.0, 0.1), false);

            Assert.AreEqual(0, report.ClearedNodeCount);
            Assert.IsTrue(graph.Nodes.OfType<RecipeNode>().All(node => node.ExtraProductivityBonus == 0.1));
        }

        [TestMethod]
        public void NodesWithTypedProductivity_CountsOnlyAboveZero() {
            ProductionGraph graph = TwoRecipeGraph();
            RecipeNode[] nodes = [.. graph.Nodes.OfType<RecipeNode>()];
            nodes[0].ExtraProductivityBonus = 0.1;

            var typed = ResearchSync.NodesWithTypedProductivity(graph);

            Assert.AreEqual(1, typed.Count);
            Assert.AreSame(nodes[0], typed[0]);
        }
    }
}
