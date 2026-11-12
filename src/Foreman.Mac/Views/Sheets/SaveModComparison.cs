using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Foreman.Mac.Views.Sheets {
    internal static class SaveModComparison {
        public static (IReadOnlyList<string> NotInSave, IReadOnlyList<string> DifferentVersion, IReadOnlyList<string> NotInPreset) Compare(
            IReadOnlyDictionary<string, string> presetMods, IReadOnlyDictionary<string, string> saveMods) {
            var notInSave = new List<string>();
            var differentVersion = new List<string>();
            var notInPreset = new List<string>();

            foreach (KeyValuePair<string, string> mod in presetMods) {
                if (mod.Key is "foremanexport" or "foremansavereader" or "core")
                    continue;
                if (!saveMods.ContainsKey(mod.Key))
                    notInSave.Add(mod.Key);
                else if (saveMods[mod.Key] != mod.Value)
                    differentVersion.Add(mod.Key);
            }
            foreach (KeyValuePair<string, string> mod in saveMods)
                if (mod.Key is not ("foremanexport" or "foremansavereader" or "core") && !presetMods.ContainsKey(mod.Key))
                    notInPreset.Add(mod.Key);

            return (notInSave, differentVersion, notInPreset);
        }

        internal static string MismatchLines(IReadOnlyList<string> notInSave, IReadOnlyList<string> differentVersion, IReadOnlyList<string> notInPreset) {
            var lines = new StringBuilder();
            foreach ((string label, IReadOnlyList<string> mods) in new[] { ("Not in the save: ", notInSave), ("Different version: ", differentVersion), ("Not in the preset: ", notInPreset) })
                if (mods.Count > 0)
                    lines.Append('\n').Append(label).Append(NameList(mods));
            return lines.ToString();
        }

        internal static string NameList(IReadOnlyList<string> names) {
            const int Shown = 5;
            List<string> items = [.. names.Take(Shown)];
            if (names.Count > Shown)
                items.Add(string.Create(DisplayCulture.Format, $"{names.Count - Shown} more"));
            return items.Count switch {
                1 => items[0],
                2 => items[0] + " and " + items[1],
                _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1],
            };
        }
    }
}
