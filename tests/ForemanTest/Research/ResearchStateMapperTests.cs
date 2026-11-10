using Foreman.DataCaching;
using Foreman.Models.Research;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace ForemanTest.Research {
    [TestClass]
    public class ResearchStateMapperTests {
        private static readonly DateTime SyncedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ResearchTechnology MapOne(string name, bool researched, SaveTechnologyLevel level) {
            var info = new SaveFileInfo();
            info.Technologies[name] = researched;
            info.TechnologyLevels[name] = level;
            return ResearchStateMapper.FromSave(info, "/tmp/x/saves/Megabase.zip", SyncedAt).Technologies.Single();
        }

        [TestMethod]
        [DataRow("2.0")]
        [DataRow("2.1")]
        public void FromSave_ResearchedFixture_MapsEveryReportedField(string line) {
            SaveFileInfo info = SaveFileReader.ParseP0Export(File.ReadAllText(TestAssets.ResolvePath($"save-reader-{line}-researched.txt")));

            ResearchState state = ResearchStateMapper.FromSave(info, "/tmp/x/saves/Megabase.zip", SyncedAt);

            Assert.IsTrue(state.MiningProductivity > 0);
            Assert.IsTrue(state.RecipeProductivity["steel-plate"] > 0);
            Assert.AreEqual("Megabase", state.Source!.SaveName);
            Assert.AreEqual("/tmp/x/saves/Megabase.zip", state.Source.SavePath);
            Assert.AreEqual(SyncedAt, state.Source.SyncedAtUtc);
            Assert.AreEqual(0, state.HandEditCount);
            foreach (ResearchField field in ResearchFields.All)
                Assert.IsTrue(state.Values.ContainsKey(field), field.ToString());
        }

        [TestMethod]
        public void FromSave_AbsentKey_IsNull() {
            string output =
                "<<<START-EXPORT-P0>>>\n" +
                "{\"research\":{\"mining_drill_productivity_bonus\":0.1,\"laboratory_speed_modifier\":0,\"laboratory_productivity_bonus\":0," +
                "\"inserter_stack_size_bonus\":1,\"bulk_inserter_capacity_bonus\":1,\"worker_robots_speed_modifier\":0," +
                "\"worker_robots_storage_bonus\":0,\"worker_robots_battery_modifier\":0,\"train_braking_force_bonus\":0}," +
                "\"mods\":[],\"technologies\":[],\"recipes\":[]}\n" +
                "<<<END-EXPORT-P0>>>\n";

            ResearchState state = ResearchStateMapper.FromSave(SaveFileReader.ParseP0Export(output), "/tmp/x/saves/Megabase.zip", SyncedAt);

            Assert.IsNull(state.BeltStackBonus);
            Assert.IsNull(state.GetSynced(ResearchField.BeltStackBonus));
            Assert.IsFalse(state.Values.ContainsKey(ResearchField.BeltStackBonus));
            Assert.IsNotNull(state.MiningProductivity);
        }

        [TestMethod]
        public void FromSave_InfiniteMaxLevel_IsUnlimited() {
            ResearchTechnology tech = MapOne("mining-productivity-3", false, new SaveTechnologyLevel(13, 3, 4294967295));

            Assert.AreEqual(10, tech.LevelsDone);
            Assert.AreEqual(3, tech.MinLevel);
            Assert.AreEqual(ResearchTechnology.Unlimited, tech.MaxLevel);
            Assert.IsTrue(tech.IsUnlimited);
            Assert.IsTrue(tech.IsPartlyResearched);
        }

        [TestMethod]
        public void FromSave_UntouchedInfinite_LevelsDoneIsZero() {
            ResearchTechnology tech = MapOne("mining-productivity-3", false, new SaveTechnologyLevel(3, 3, 4294967295));

            Assert.AreEqual(0, tech.LevelsDone);
            Assert.IsFalse(tech.IsPartlyResearched);
        }

        [TestMethod]
        public void FromSave_LeveledResearched_LevelsDoneIsMax() {
            ResearchTechnology tech = MapOne("worker-robots-speed-4", true, new SaveTechnologyLevel(7, 1, 7));

            Assert.AreEqual(7, tech.LevelsDone);
            Assert.AreEqual(7, tech.LevelCount);
        }

        [TestMethod]
        public void FromSave_LeveledFromThree_CountsFromMinLevel() {
            ResearchTechnology tech = MapOne("worker-robots-speed-3", false, new SaveTechnologyLevel(5, 3, 7));

            Assert.AreEqual(2, tech.LevelsDone);
            Assert.AreEqual(5, tech.LevelCount);
            Assert.IsTrue(tech.IsPartlyResearched);
        }

        [TestMethod]
        public void FromSave_NoLevelEntry_IsSingleLevel() {
            var info = new SaveFileInfo();
            info.Technologies["steel-processing"] = true;
            info.Technologies["rocket-silo"] = false;

            ResearchState state = ResearchStateMapper.FromSave(info, "/tmp/x/saves/Megabase.zip", SyncedAt);

            ResearchTechnology steel = state.Technologies.Single(t => t.Name == "steel-processing");
            Assert.AreEqual(1, steel.LevelsDone);
            Assert.AreEqual(1, steel.MinLevel);
            Assert.AreEqual(1, steel.MaxLevel);
            Assert.IsFalse(steel.IsLeveled);
            ResearchTechnology silo = state.Technologies.Single(t => t.Name == "rocket-silo");
            Assert.AreEqual(0, silo.LevelsDone);
            Assert.AreEqual(1, silo.MinLevel);
            Assert.AreEqual(1, silo.MaxLevel);
            Assert.IsFalse(silo.IsPartlyResearched);
            CollectionAssert.AreEqual(new[] { "rocket-silo", "steel-processing" }, state.Technologies.Select(t => t.Name).ToArray());
        }

        [TestMethod]
        public void FromSave_NoTicksPlayed_SourceTicksIsNull() {
            var info = new SaveFileInfo { TicksPlayed = null };

            ResearchState state = ResearchStateMapper.FromSave(info, "/tmp/x/saves/Megabase.zip", SyncedAt);

            Assert.IsNull(state.Source!.TicksPlayed);
        }
    }
}
