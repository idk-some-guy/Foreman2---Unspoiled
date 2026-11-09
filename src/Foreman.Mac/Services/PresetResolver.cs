using Foreman;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Foreman.Mac.Services {
    public sealed class PresetResolver {
        public const string DefaultPresetName = "Factorio 2.0 Vanilla";

        private readonly string presetsDirectory;
        //File-location policy (docs/upstream-divergences.md): imported presets write to the user's writable
        //Presets directory; this resolver reads both it and the bundle-shipped one, user directory winning
        //on a name collision (Task 7's own import-over-the-active-preset case).
        private readonly string userPresetsDirectory;

        public PresetResolver(string? presetsDirectoryOverride = null, string? userPresetsDirectoryOverride = null) {
            presetsDirectory = presetsDirectoryOverride ?? Path.Combine(AppPaths.ExecutableDirectory, "Presets");
            userPresetsDirectory = userPresetsDirectoryOverride ?? AppPaths.UserPresetsDirectory;
        }

        public Preset Resolve(string? currentPresetName) {
            var existingNames = GetExistingPresetNames();
            if (currentPresetName is not null && existingNames.Contains(currentPresetName))
                return new Preset(currentPresetName, isCurrentlySelected: true, isDefaultPreset: currentPresetName == DefaultPresetName);

            if (!existingNames.Contains(DefaultPresetName))
                throw new DefaultPresetUnavailableException();

            return new Preset(DefaultPresetName, isCurrentlySelected: true, isDefaultPreset: true);
        }

        //Ports MainForm.GetValidPresetsList's list-construction tail (upstream MainForm.cs:325-353) - the
        //warning/exit branches for a missing current or default preset stay boot-time-only, already handled
        //by Resolve above; by the time Settings opens, CurrentPresetName is already known-valid.
        public List<Preset> BuildPresetList(string currentPresetName) {
            var names = GetExistingPresetNames();
            names.Remove(currentPresetName);
            names.Remove(DefaultPresetName);

            var presets = new List<Preset> {
                new(currentPresetName, isCurrentlySelected: true, isDefaultPreset: currentPresetName == DefaultPresetName),
            };
            if (currentPresetName != DefaultPresetName)
                presets.Add(new Preset(DefaultPresetName, isCurrentlySelected: false, isDefaultPreset: true));
            presets.AddRange(names.Select(name => new Preset(name, isCurrentlySelected: false, isDefaultPreset: false)));
            return presets;
        }

        private List<string> GetExistingPresetNames() {
            var names = new HashSet<string>(StringComparer.Ordinal);
            CollectPresetNames(presetsDirectory, names);
            CollectPresetNames(userPresetsDirectory, names);
            var sorted = new List<string>(names);
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }

        private static void CollectPresetNames(string directory, HashSet<string> names) {
            if (!Directory.Exists(directory))
                return;
            foreach (string presetFile in Directory.GetFiles(directory, "*.pjson"))
                if (File.Exists(Path.ChangeExtension(presetFile, "dat")))
                    names.Add(Path.GetFileNameWithoutExtension(presetFile));
        }
    }
}
