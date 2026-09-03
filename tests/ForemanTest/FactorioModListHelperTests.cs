using Foreman;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text.Json.Nodes;

namespace ForemanTest {
    [TestClass]
    public class FactorioModListHelperTests {
        [TestMethod]
        public void SetModState_NoExistingFile_CreatesModListWithEnabledEntry() {
            string modsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(modsPath);

            FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: true);

            JsonNode mods = ReadModList(modsPath);
            Assert.IsTrue(TryGetMod(mods, "foremanexport", out JsonObject? entry));
            Assert.IsTrue((bool)entry!["enabled"]!);
        }

        [TestMethod]
        public void SetModState_DisableWithoutRemove_KeepsEntryDisabled() {
            string modsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(modsPath);
            FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: true);

            FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: false);

            JsonNode mods = ReadModList(modsPath);
            Assert.IsTrue(TryGetMod(mods, "foremanexport", out JsonObject? entry));
            Assert.IsFalse((bool)entry!["enabled"]!);
        }

        [TestMethod]
        public void SetModState_DisableWithRemove_DropsEntryEntirely() {
            string modsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(modsPath);
            FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: true);

            FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: false, removeFromListWhenDisabled: true);

            JsonNode mods = ReadModList(modsPath);
            Assert.IsFalse(TryGetMod(mods, "foremanexport", out _));
        }

        [TestMethod]
        public void SetModState_PreservesOtherModsInTheList() {
            string modsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(modsPath);
            File.WriteAllText(Path.Combine(modsPath, "mod-list.json"),
                "{ \"mods\": [ { \"name\": \"base\", \"enabled\": true } ] }");

            FactorioModListHelper.SetModState(modsPath, "foremansavereader", enabled: true);

            JsonNode mods = ReadModList(modsPath);
            Assert.IsTrue(TryGetMod(mods, "base", out _));
            Assert.IsTrue(TryGetMod(mods, "foremansavereader", out _));
        }

        private static JsonNode ReadModList(string modsPath) =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(modsPath, "mod-list.json")))!;

        private static bool TryGetMod(JsonNode modList, string name, out JsonObject? entry) {
            entry = null;
            foreach (JsonNode? node in modList["mods"]!.AsArray()) {
                if (node is JsonObject obj && (string?)obj["name"] == name) {
                    entry = obj;
                    return true;
                }
            }
            return false;
        }
    }
}
