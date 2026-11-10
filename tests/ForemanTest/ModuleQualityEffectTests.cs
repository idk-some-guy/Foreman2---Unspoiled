using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class ModuleQualityEffectTests {
        private static readonly string[] TierNames = ["normal", "uncommon", "rare", "epic", "legendary"];
        private static readonly string[] Module3Names = ["speed-module-3", "productivity-module-3", "quality-module-3"];

        private static double[] Effects(IModule module, IQuality quality) =>
            [module.GetConsumptionBonus(quality), module.GetSpeedBonus(quality), module.GetProductivityBonus(quality), module.GetPolutionBonus(quality), module.GetQualityBonus(quality)];

        private static readonly Dictionary<string, double[][]> Expected20 = new() {
            ["speed-module-3"] = [[0.7, 0.5, 0, 0, -0.25], [0.7, 0.65, 0, 0, -0.25], [0.7, 0.8, 0, 0, -0.25], [0.7, 0.95, 0, 0, -0.25], [0.7, 1.25, 0, 0, -0.25]],
            ["productivity-module-3"] = [[0.8, -0.15, 0.1, 0.1, 0], [0.8, -0.15, 0.13, 0.1, 0], [0.8, -0.15, 0.16, 0.1, 0], [0.8, -0.15, 0.19, 0.1, 0], [0.8, -0.15, 0.25, 0.1, 0]],
            ["quality-module-3"] = [[0, -0.05, 0, 0, 0.25], [0, -0.05, 0, 0, 0.32], [0, -0.05, 0, 0, 0.4], [0, -0.05, 0, 0, 0.47], [0, -0.05, 0, 0, 0.62]],
        };

        private static JsonArray ReadModules(string preset) {
            string path = Path.Combine(PresetDataCacheFixture.PresetsDirectory, preset + ".pjson");
            return JsonNode.Parse(File.ReadAllText(path))!["modules"]!.AsArray();
        }

        [TestMethod]
        public async Task SpaceAge20_ModuleBonuses_ByQuality_Unchanged() {
            DataCache cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            foreach (string moduleName in Module3Names)
                for (int level = 0; level < TierNames.Length; level++) {
                    double[] actual = Effects(cache.Modules[moduleName], cache.Qualities[TierNames[level]]);
                    double[] expected = Expected20[moduleName][level];
                    for (int i = 0; i < expected.Length; i++)
                        Assert.AreEqual(expected[i], actual[i], 0, $"{moduleName} {TierNames[level]} effect {i}");
                }
        }

        [TestMethod]
        public async Task SpaceAge21_ModuleBonuses_EqualExportedPerQualityEffects() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            int checkedEntries = 0;
            foreach (JsonNode? moduleNode in ReadModules(PresetDataCacheFixture.SpaceAge21)) {
                string name = moduleNode!["name"]!.GetValue<string>();
                foreach (JsonNode? entry in moduleNode["q_module_effects"]!.AsArray()) {
                    IQuality quality = cache.Qualities[entry!["quality"]!.GetValue<string>()];
                    JsonNode effects = entry["effects"]!;
                    double[] expected = [.. new[] { "consumption", "speed", "productivity", "pollution", "quality" }
                        .Select(key => Math.Round(effects[key]!.GetValue<double>(), 4, MidpointRounding.AwayFromZero))];
                    double[] actual = Effects(cache.Modules[name], quality);
                    for (int i = 0; i < expected.Length; i++)
                        Assert.AreEqual(expected[i], actual[i], 1e-12, $"{name} {quality.Name} effect {i}");
                    checkedEntries++;
                }
            }
            Assert.IsTrue(checkedEntries >= 12 * 5);
        }

        [TestMethod]
        public async Task SpaceAge21_SpeedModule3Legendary_QualityPenaltyIsNotScaled() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            IModule module = cache.Modules["speed-module-3"];
            foreach (string tier in TierNames) {
                Assert.AreEqual(-0.025, module.GetQualityBonus(cache.Qualities[tier]), 1e-12, tier);
                Assert.AreEqual(0.7, module.GetConsumptionBonus(cache.Qualities[tier]), 1e-12, tier);
            }
            Assert.AreEqual(0.5, module.GetSpeedBonus(cache.Qualities["normal"]), 1e-12);
            Assert.AreEqual(1.25, module.GetSpeedBonus(cache.Qualities["legendary"]), 1e-12);
        }

        [TestMethod]
        public async Task Module_QualityMissingFromTable_FallsBackToFormula() {
            DataCache cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            var module = new ModulePrototype(cache, "table-module", "table-module") { SpeedBonus = 0.5, QualityBonus = -0.25 };
            module.QualityEffects = new Dictionary<IQuality, ModuleEffects> {
                [cache.Qualities["legendary"]] = new(0, 2, 0, 0, -0.25),
            };
            Assert.AreEqual(2, module.GetSpeedBonus(cache.Qualities["legendary"]), 0);
            Assert.AreEqual(0.65, module.GetSpeedBonus(cache.Qualities["uncommon"]), 0);
            Assert.AreEqual(0.5, module.GetSpeedBonus(cache.Qualities["normal"]), 0);
            Assert.AreEqual(0.8, module.GetSpeedBonus(2), 0);
        }
    }
}
