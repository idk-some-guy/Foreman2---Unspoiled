using Foreman.Models.Research;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace ForemanTest.Research {
    [TestClass]
    public class ResearchStateTests {
        private static ResearchState Synced() => new(
            new ResearchSource("Megabase", "/tmp/x/saves/Megabase.zip", 1000, new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)),
            new Dictionary<ResearchField, double> { [ResearchField.MiningProductivity] = 0.2 },
            new Dictionary<ResearchField, double> { [ResearchField.MiningProductivity] = 0.2 },
            new Dictionary<string, double> { ["steel-plate"] = 0.1 },
            new Dictionary<string, double> { ["steel-plate"] = 0.1 },
            []);

        [TestMethod]
        public void WithValue_DifferentFromSynced_IsHandEdited() {
            ResearchState state = Synced().WithValue(ResearchField.MiningProductivity, 0.5);

            Assert.IsTrue(state.IsHandEdited(ResearchField.MiningProductivity));
            Assert.AreEqual(1, state.HandEditCount);
            Assert.AreEqual(0.5, state.MiningProductivity);
            Assert.AreEqual(0.2, state.GetSynced(ResearchField.MiningProductivity));
        }

        [TestMethod]
        public void WithValue_BackToSynced_IsNotHandEdited() {
            ResearchState state = Synced().WithValue(ResearchField.MiningProductivity, 0.5).WithValue(ResearchField.MiningProductivity, 0.2);

            Assert.IsFalse(state.IsHandEdited(ResearchField.MiningProductivity));
            Assert.AreEqual(0, state.HandEditCount);
        }

        [TestMethod]
        public void WithValue_OnAbsentField_IsHandEditedAndSyncedStaysNull() {
            ResearchState state = Synced().WithValue(ResearchField.BeltStackBonus, 2);

            Assert.IsTrue(state.IsHandEdited(ResearchField.BeltStackBonus));
            Assert.AreEqual(2, state.BeltStackBonus);
            Assert.IsNull(state.GetSynced(ResearchField.BeltStackBonus));
        }

        [TestMethod]
        public void Revert_RestoresSyncedValueAndClearsMark() {
            ResearchState state = Synced().WithValue(ResearchField.MiningProductivity, 0.5).Revert(ResearchField.MiningProductivity);

            Assert.AreEqual(0.2, state.MiningProductivity);
            Assert.IsFalse(state.IsHandEdited(ResearchField.MiningProductivity));
        }

        [TestMethod]
        public void RevertRecipe_HandAdded_RemovesTheRecipe() {
            ResearchState state = Synced().WithRecipe("iron-gear-wheel", 0);

            Assert.IsTrue(state.IsRecipeHandAdded("iron-gear-wheel"));

            state = state.RevertRecipe("iron-gear-wheel");

            Assert.IsFalse(state.RecipeProductivity.ContainsKey("iron-gear-wheel"));
        }

        [TestMethod]
        public void RevertAll_ClearsEveryMark() {
            ResearchState state = Synced()
                .WithValue(ResearchField.MiningProductivity, 0.5)
                .WithValue(ResearchField.BeltStackBonus, 2)
                .WithRecipe("steel-plate", 0.3)
                .WithRecipe("iron-gear-wheel", 0)
                .RevertAll();

            Assert.AreEqual(0, state.HandEditCount);
            CollectionAssert.AreEquivalent(state.SyncedValues.ToList(), state.Values.ToList());
            CollectionAssert.AreEquivalent(state.SyncedRecipeProductivity.ToList(), state.RecipeProductivity.ToList());
        }

        [TestMethod]
        public void None_HasNoSourceNoValuesNoTechnologies() {
            Assert.IsNull(ResearchState.None.Source);
            Assert.AreEqual(0, ResearchState.None.Values.Count);
            Assert.AreEqual(0, ResearchState.None.RecipeProductivity.Count);
            Assert.AreEqual(0, ResearchState.None.Technologies.Count);
        }
    }
}
