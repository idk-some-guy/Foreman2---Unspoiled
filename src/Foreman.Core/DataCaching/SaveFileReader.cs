using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;

namespace Foreman.DataCaching {
    public enum SaveFileLoadOutcome { Ok, Cancel, Abort }

    //Ports SaveFileLoadForm.LoadSaveFile's synchronous body (reference io-reference.md §5, upstream
    //SavefileLoadForm.cs:104-197) as a UI-independent pipeline: exe discovery from the save's own
    //factorio-current.log, the foremansavereader mod copy/enable, the benchmark run, and the P0 export
    //parse. Kept out of Foreman.Mac so it can be driven against StubFactorioHarness under plain MSTest,
    //the same way FactorioBenchmarkRunner/FactorioInstallValidator already are (reference §4). SaveFileLoadWindow
    //(Foreman.Mac) wraps this with the file picker and the dialogs it can't show from here.
    internal static class SaveFileReader {
        public sealed class Result {
            public required SaveFileLoadOutcome Outcome { get; init; }
            public SaveFileInfo? SaveFileInfo { get; init; }
            public string? WarningMessage { get; init; }
        }

        public static Result Load(string saveFilePath, CancellationToken token) {
            string modsPath = "";
            try {
                string? userDataPath = FindUserDataDirectory(saveFilePath);
                string currentLog = Path.Combine(userDataPath ?? "", "factorio-current.log");
                string[] currentLogLines = Utf8File.ReadAllLines(currentLog);
                string factorioPath = FindFactorioExecutablePathFromLog(currentLogLines) ?? "";

                if (!FactorioInstallValidator.TryValidateExecutable(factorioPath, out string? factorioVersionError))
                    return new Result { Outcome = SaveFileLoadOutcome.Cancel, WarningMessage = factorioVersionError };

                modsPath = Path.Combine(userDataPath ?? "", "mods");
                if (!Directory.Exists(modsPath))
                    Directory.CreateDirectory(modsPath);
                try {
                    FactorioBundledModHelper.CopyToModsFolder("foremansavereader_2.0.0", modsPath, "info.json", "instrument-control.lua");
                } catch (Exception ex) {
                    ErrorLogging.LogException(ex, "copying of foreman save reader mod files failed");
                    return new Result {
                        Outcome = SaveFileLoadOutcome.Abort,
                        WarningMessage = "could not copy foreman save reader mod files (Mods/foremansavereader_2.0.0/) to the factorio mods folder. Reinstall foreman?",
                    };
                }

                FactorioModListHelper.SetModState(modsPath, "foremansavereader", enabled: true);

                string capturedModsPath = modsPath;
                FactorioRunResult readRun = FactorioBenchmarkRunner.Run(
                    factorioPath,
                    string.Format(CultureInfo.InvariantCulture,
                        "--instrument-mod foremansavereader --benchmark \"{0}\" --benchmark-ticks 1 --benchmark-runs 1",
                        Path.GetFileName(saveFilePath)),
                    Path.GetDirectoryName(factorioPath) ?? "",
                    token,
                    () => {
                        if (Directory.Exists(Path.Combine(capturedModsPath, "foremansavereader_2.0.0")))
                            Directory.Delete(Path.Combine(capturedModsPath, "foremansavereader_2.0.0"), true);
                    });

                string resultString = readRun.Output;
                if (string.IsNullOrEmpty(resultString) && token.IsCancellationRequested)
                    return new Result { Outcome = SaveFileLoadOutcome.Cancel };

                if (Directory.Exists(Path.Combine(modsPath, "foremansavereader_2.0.0")))
                    Directory.Delete(Path.Combine(modsPath, "foremansavereader_2.0.0"), true);

                if (FactorioBenchmarkRunner.IsAnotherInstanceRunning(resultString))
                    return new Result {
                        Outcome = SaveFileLoadOutcome.Cancel,
                        WarningMessage = "File read could not be completed because this instance of Factorio is currently running. Please stop expanding the factory for just a brief moment...",
                    };

                if (readRun.Crashed) {
                    ErrorLogging.LogLine("Foreman save read: Factorio crash (exit code " + readRun.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
                    return new Result {
                        Outcome = SaveFileLoadOutcome.Abort,
                        WarningMessage = "Factorio crashed while reading the save file.\n\n" +
                            "This is usually caused by a mod bug. See factorio-current.log in your Factorio user data folder.",
                    };
                }

                if (!resultString.Contains("<<<END-EXPORT-P0>>>", StringComparison.Ordinal)) {
                    ErrorLogging.LogLine("could not process save file due to export not completing. Mod issue?");
                    return new Result { Outcome = SaveFileLoadOutcome.Abort };
                }

                return new Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = ParseP0Export(resultString) };
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, string.Format(CultureInfo.InvariantCulture, "Error reading save file '{0}'", saveFilePath));
                if (!string.IsNullOrEmpty(modsPath) && Directory.Exists(Path.Combine(modsPath, "foremansavereader_2.0.0")))
                    Directory.Delete(Path.Combine(modsPath, "foremansavereader_2.0.0"), true);
                return new Result { Outcome = SaveFileLoadOutcome.Abort };
            }
        }

        //Ports the constructor's DefaultSaveFileLocation walk (reference §5 step 1): walks up from a
        //candidate path to its "saves" folder, then one level further to the actual user-data directory,
        //trusting it only if that directory still has a factorio-current.log next to it - a stale/moved
        //install falls back to auto-detecting the first installed Factorio's saves folder instead.
        public static string ResolveDefaultSaveFileLocation(string? lastSaveFileLocation, string? factorioHomeOverride = null) {
            string location = lastSaveFileLocation ?? "";
            string? userDataPath = FindUserDataDirectory(location);
            if (!File.Exists(Path.Combine(userDataPath ?? "", "factorio-current.log")))
                location = "";

            if (string.IsNullOrEmpty(location)) {
                List<string> installs = FactorioPathsProcessor.GetFactorioInstallLocations(factorioHomeOverride);
                if (installs.Count > 0) {
                    string userPath = FactorioPathsProcessor.GetFactorioUserPath(installs[0], false, factorioHomeOverride);
                    if (!string.IsNullOrEmpty(userPath))
                        location = Path.Combine(userPath, "saves");
                }
            }
            return location;
        }

        //Ports the "walk up to the saves folder, then one more level" logic shared by the constructor's
        //DefaultSaveFileLocation and LoadSaveFile's userDataPath (reference §5) - upstream repeats this
        //loop verbatim in both places; here it's one method.
        private static string? FindUserDataDirectory(string pathUnderSaves) {
            string? current = pathUnderSaves;
            while (!string.IsNullOrEmpty(current) && !string.Equals(Path.GetFileName(current), "saves", StringComparison.OrdinalIgnoreCase))
                current = Path.GetDirectoryName(current);
            return string.IsNullOrEmpty(current) ? current : Path.GetDirectoryName(current);
        }

        //Ports LoadSaveFile's "Program arguments" scan (reference §5 step 2): the LAST matching line wins,
        //since upstream's loop never breaks - a save's log can carry several launches, most recent last.
        public static string? FindFactorioExecutablePathFromLog(IEnumerable<string> logLines) {
            string? factorioPath = null;
            foreach (string line in logLines) {
                if (line.Contains("Program arguments", StringComparison.OrdinalIgnoreCase)) {
                    string path = line[(line.IndexOf('"', StringComparison.Ordinal) + 1)..];
                    factorioPath = path[..path.IndexOf('"', StringComparison.Ordinal)];
                }
            }
            return factorioPath;
        }

        //Ports LoadSaveFile's P0 parse (reference §5 step 5). Upstream slices with fixed byte offsets
        //(marker index + 23, end index - 1) that only line up with a CRLF-terminated marker line; this
        //trims whitespace around the extracted span instead, so it parses the same JSON regardless of
        //whether the export mod's own stdout uses LF (macOS/Linux Factorio) or CRLF (Windows).
        public static SaveFileInfo ParseP0Export(string resultString) {
            const string startMarker = "<<<START-EXPORT-P0>>>";
            const string endMarker = "<<<END-EXPORT-P0>>>";
            int start = resultString.IndexOf(startMarker, StringComparison.Ordinal) + startMarker.Length;
            int end = resultString.IndexOf(endMarker, start, StringComparison.Ordinal);
            string exportString = resultString[start..end].Trim();
            JsonObject export = PresetJson.ParseObject(exportString);

            var info = new SaveFileInfo();
            foreach (JsonNode node in PresetJson.EnumerateArray(export, "mods"))
                if (PresetJson.GetString(node, "name") is string name && PresetJson.GetString(node, "version") is string version)
                    info.Mods.Add(name, version);
            foreach (JsonNode node in PresetJson.EnumerateArray(export, "technologies"))
                if (PresetJson.GetString(node, "name") is string name && PresetJson.GetBool(node, "enabled") is bool enabled)
                    info.Technologies.Add(name, enabled);
            foreach (JsonNode node in PresetJson.EnumerateArray(export, "recipes"))
                if (PresetJson.GetString(node, "name") is string name && PresetJson.GetBool(node, "enabled") is bool enabled)
                    info.Recipes.Add(name, enabled);
            return info;
        }
    }
}
