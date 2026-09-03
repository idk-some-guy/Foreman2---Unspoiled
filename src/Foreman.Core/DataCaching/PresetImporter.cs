using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.DataCaching {
    public enum PresetImportOutcome { Ok, Cancel, Failed }

    //Ports PresetImportForm.ProcessPreset's background-task body (reference io-reference.md §4, upstream
    //PresetImportForm.cs:176-426) as a UI-independent pipeline: creates a throwaway Factorio save, runs the
    //bundled foremanexport mod against it, parses the marker-delimited output, and writes the new preset's
    //.pjson/.json/.dat. Lives in Foreman.Core so it's driven against StubFactorioHarness under plain MSTest,
    //the same way SaveFileReader (§5) already is. PresetImportWindow (Foreman.Mac) wraps this with the form
    //fields, live validation, and the interactive icon-failure confirmation this pipeline can't show itself.
    internal static class PresetImporter {
        //Only Factorio 2.x installs reach this pipeline (FactorioInstallValidator's major==2 gate), and the
        //repo only ships the 2.0.0 build of the bundled export mod - upstream instead derives this string
        //from FileVersionInfo.ProductMajorPart at call time, which this port has no PE-resource source for
        //anyway (docs/upstream-divergences.md).
        private const string ForemanExportModName = "foremanexport_2.0.0";
        private const string AnotherInstanceMessage =
            "Foreman export could not be completed because this instance of Factorio is currently running. Please stop expanding the factory for just a brief moment and let the export commence in peace!";

        private static readonly char[] ExtraNameChars = ['(', ')', '-', '_', '.', ' '];

        //Ports PresetNameTextBox_TextChanged's character filter (reference §4 step 8).
        public static string FilterName(string text) =>
            string.Concat(text.Where(c => char.IsLetterOrDigit(c) || ExtraNameChars.Contains(c)));

        public sealed class Result {
            public required PresetImportOutcome Outcome { get; init; }
            public string NewPresetName { get; init; } = "";
            public string? WarningMessage { get; init; }
        }

        public static async Task<Result> ProcessPreset(
            string installPath,
            string modsPath,
            string scratchDirectory,
            string newPresetName,
            string userPresetsDirectory,
            IProgress<KeyValuePair<int, string>> progress,
            Func<int, int, Task<bool>> confirmContinueWithMissingIconsAsync,
            CancellationToken token) {

            string exePath = FactorioPathsProcessor.GetExecutablePath(installPath);
            Directory.CreateDirectory(scratchDirectory);
            string tempSavePath = Path.Combine(scratchDirectory, "temp-save.zip");
            string presetBasePath = Path.Combine(userPresetsDirectory, newPresetName);

            //considering that we got here with factorio.exe checks (the caller's own pre-flight validation),
            //this is a bit redundant. but whatevs. (upstream's own comment, kept verbatim)
            if (!File.Exists(exePath)) {
                CleanupFailedImport(tempSavePath);
                return Fail("factorio.exe not found...");
            }

            try {
                if (!Directory.Exists(modsPath))
                    Directory.CreateDirectory(modsPath);
                if (Directory.Exists(Path.Combine(modsPath, ForemanExportModName)))
                    Directory.Delete(Path.Combine(modsPath, ForemanExportModName));
            } catch (Exception e) {
                CleanupFailedImport(tempSavePath, modsPath);
                if (e is UnauthorizedAccessException) {
                    ErrorLogging.LogException(e, "insufficient access to factorio mods folder");
                    return Fail("Insufficient access to the factorio mods folder. Please ensure factorio mods are in an accessible folder, or launch Foreman with Administrator privileges.");
                }
                ErrorLogging.LogException(e, "error while accessing factorio mods folder");
                return Fail("Unknown error trying to access factorio mods folder. Sorry");
            }

            progress.Report(new(10, "Running Factorio - creating test save."));
            FactorioRunResult createRun = FactorioBenchmarkRunner.Run(
                exePath,
                string.Format(CultureInfo.InvariantCulture, "--mod-directory \"{0}\" --create temp-save.zip", modsPath),
                scratchDirectory,
                token,
                () => CleanupFailedImport(tempSavePath, modsPath));

            if (string.IsNullOrEmpty(createRun.Output) && token.IsCancellationRequested)
                return Cancelled();

            if (FactorioBenchmarkRunner.IsAnotherInstanceRunning(createRun.Output)) {
                CleanupFailedImport(tempSavePath, modsPath);
                return Fail(AnotherInstanceMessage);
            }

            if (CheckCrash(createRun, "creating the test save for preset export", scratchDirectory, tempSavePath, modsPath) is Result createCrashResult)
                return createCrashResult;

            if (!File.Exists(tempSavePath)) {
                ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Foreman preset export: temp-save.zip missing after --create (exit code {0}).", createRun.ExitCode));
                WriteExportFailureLog(scratchDirectory, createRun.Output);
                CleanupFailedImport(tempSavePath, modsPath);
                return Fail(
                    "Factorio did not create the test save (temp-save.zip) needed for preset export.\n\n" +
                    "Factorio may have crashed or exited early. Check factorio-current.log in your Factorio user data folder " +
                    "and try disabling mods until you can create a new game with the same mod list.");
            }

            FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: true, removeFromListWhenDisabled: false);

            try {
                FactorioBundledModHelper.CopyToModsFolder(ForemanExportModName, modsPath, "info.json", "instrument-after-data.lua", "instrument-control.lua");
            } catch (Exception e) {
                CleanupFailedImport(tempSavePath, modsPath);
                if (e is UnauthorizedAccessException) {
                    ErrorLogging.LogException(e, "copying of foreman export mod files failed - insufficient access");
                    return Fail("Insufficient access to copy foreman export mod files (Mods/" + ForemanExportModName + "/) to the factorio mods folder. Please ensure factorio mods are in an accessible folder, or launch Foreman with Administrator privileges.");
                }
                ErrorLogging.LogException(e, "copying of foreman export mod files failed");
                return Fail("could not copy foreman export mod files (Mods/" + ForemanExportModName + "/) to the factorio mods folder. Reinstall foreman?");
            }

            progress.Report(new(20, "Running Factorio - foreman export scripts."));
            FactorioRunResult exportRun = FactorioBenchmarkRunner.Run(
                exePath,
                string.Format(CultureInfo.InvariantCulture, "--mod-directory \"{0}\" --instrument-mod foremanexport --benchmark temp-save.zip --benchmark-ticks 1 --benchmark-runs 1", modsPath),
                scratchDirectory,
                token,
                () => CleanupFailedImport(tempSavePath, modsPath));

            string resultString = exportRun.Output;
            if (string.IsNullOrEmpty(resultString) && token.IsCancellationRequested)
                return Cancelled();

            if (CheckCrash(exportRun, "running the preset export scripts", scratchDirectory, tempSavePath, modsPath) is Result exportCrashResult)
                return exportCrashResult;

            if (File.Exists(tempSavePath))
                File.Delete(tempSavePath);
            if (Directory.Exists(Path.Combine(modsPath, ForemanExportModName)))
                Directory.Delete(Path.Combine(modsPath, ForemanExportModName), true);

            progress.Report(new(25, "Processing mod files."));

            if (FactorioBenchmarkRunner.IsAnotherInstanceRunning(resultString)) {
                CleanupFailedImport(tempSavePath, modsPath);
                return Fail(AnotherInstanceMessage);
            }
            if (!resultString.Contains("<<<END-EXPORT-P1>>>", StringComparison.Ordinal) || !resultString.Contains("<<<END-EXPORT-P2>>>", StringComparison.Ordinal)) {
                string failureMessage = resultString.Contains("temp-save.zip does not exist", StringComparison.Ordinal)
                    ? "Foreman export could not finish because Factorio could not load the test save (temp-save.zip). " +
                      "The save may not have been created in the previous step; check factorio-current.log for crashes or errors."
                    : "Foreman export could not be completed - possible mod conflict detected. Please run Factorio and ensure it can successfully load to menu before retrying.";
                ErrorLogging.LogLine("Foreman export failed partway. Consult errorExporting.json for full output (and search for <<<END-EXPORT-P1>>> or <<<END-EXPORT-P2>>>, at least one of which is missing)");
                WriteExportFailureLog(scratchDirectory, resultString);
                CleanupFailedImport(tempSavePath, modsPath);
                return Fail(failureMessage);
            }

            //Marker slicing (docs/upstream-divergences.md): upstream's fixed byte offsets (marker index + 23,
            //end index - 1) only produce valid JSON on CRLF-terminated output. This finds each marker's real
            //length and trims the extracted span instead, the same fix SaveFileReader.ParseP0Export (§5) made.
            string lnamesString = ExtractSection(resultString, "<<<START-EXPORT-LN>>>", "<<<END-EXPORT-LN>>>")
                .Replace("\n", "").Replace("\r", "").Replace("<#~#>", "\n");
            string iconString = ExtractSection(resultString, "<<<START-EXPORT-P1>>>", "<<<END-EXPORT-P1>>>");
            string dataString = ExtractSection(resultString, "<<<START-EXPORT-P2>>>", "<<<END-EXPORT-P2>>>");

            string[] lnames = lnamesString.Split('\n'); //keep empties - we know where they are!
            var localisedNames = new Dictionary<string, string>();
            for (int i = 0; i < lnames.Length / 2; i++)
                localisedNames.Add('$' + i.ToString(CultureInfo.InvariantCulture), lnames[(i * 2) + 1].Replace("Unknown key: \"", "").Replace("\"", ""));

            JsonObject iconJObject;
            JsonObject dataJObject;
            try {
                iconJObject = PresetJson.ParseObject(iconString);
                dataJObject = PresetJson.ParseObject(dataString);
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "json parsing of export mod output failed (" + ForemanExportModName + "); consult _iconJObjectOut.json and _dataJObjectOut.json");
                try {
                    Utf8File.WriteAllText(Path.Combine(scratchDirectory, "_iconJObjectOut.json"), iconString);
                    Utf8File.WriteAllText(Path.Combine(scratchDirectory, "_dataJObjectOut.json"), dataString);
                } catch (Exception dumpEx) {
                    ErrorLogging.LogException(dumpEx, "Foreman preset export: failed to write _iconJObjectOut.json/_dataJObjectOut.json");
                }
                CleanupFailedImport(tempSavePath, modsPath);
                return Fail("Foreman export could not be completed - unknown json parsing error.\nSorry");
            }

            //trawl over the dataJObject entities and replace any 'lid' with 'localised_name'
            foreach (string groupName in PresetJson.GetObjectPropertyNames(dataJObject)) {
                if (dataJObject[groupName] is not JsonArray set)
                    continue;
                foreach (JsonNode? obj in set) {
                    if (obj is JsonObject jobject && PresetJson.GetString(jobject, "lid") is string lid) {
                        jobject["localised_name"] = localisedNames[lid];
                        jobject.Remove("lid");
                    }
                }
            }

            Directory.CreateDirectory(userPresetsDirectory);
            Utf8File.WriteAllText(presetBasePath + ".pjson", PresetJson.WriteIndented(dataJObject));
            File.Copy(Path.Combine(AppPaths.ExecutableDirectory, "baseCustom.json"), presetBasePath + ".json", true);

            if (token.IsCancellationRequested) {
                CleanupFailedImport(tempSavePath, modsPath, presetBasePath);
                return Cancelled();
            }

            var modSet = new Dictionary<string, string>();
            foreach (JsonNode objJToken in PresetJson.EnumerateArray(dataJObject, "mods"))
                if (PresetJson.GetString(objJToken, "name") is string name && PresetJson.GetString(objJToken, "version") is string version)
                    modSet.Add(name.ToLowerInvariant(), version);

            using (var icProcessor = new IconCacheProcessor()) {
                if (!icProcessor.PrepareModPaths(modSet, modsPath, Path.Combine(installPath, "data"), token)) {
                    CleanupFailedImport(tempSavePath, modsPath, presetBasePath);
                    if (token.IsCancellationRequested)
                        return Cancelled();
                    ErrorLogging.LogLine("Mod parsing failed - the list of mods provided could not be mapped to the existing mod folders & zip files.");
                    return Fail("Mod inconsistency detected. Try to see if launching Factorio gives an error?");
                }

                if (!await icProcessor.CreateIconCache(iconJObject, presetBasePath + ".dat", progress, 30, 100, token).ConfigureAwait(false)) {
                    if (token.IsCancellationRequested) {
                        CleanupFailedImport(tempSavePath, modsPath, presetBasePath);
                        return Cancelled();
                    }
                    ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "{0}/{1} images were not found while processing icons.", icProcessor.FailedPathCount, icProcessor.TotalPathCount));
                    bool proceed = await confirmContinueWithMissingIconsAsync(icProcessor.FailedPathCount, icProcessor.TotalPathCount).ConfigureAwait(false);
                    if (!proceed) {
                        CleanupFailedImport(tempSavePath, modsPath, presetBasePath);
                        return Cancelled();
                    }
                }
            }

            FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: false, removeFromListWhenDisabled: true);
            return new Result { Outcome = PresetImportOutcome.Ok, NewPresetName = newPresetName };
        }

        //Ports CleanupFailedImport (reference §4 step 8). Real fix over upstream (docs/upstream-divergences.md):
        //upstream's own version takes a foremanModName parameter it never actually threads through from any
        //of ProcessPreset's own call sites (every call passes at most modsPath/presetPath), so the mod-folder
        //delete and every .pjson/.json/.dat delete are unreachable dead code there - this port drops that
        //dead parameter and gates those deletes on modsPath/presetBasePath alone, so a failed import actually
        //leaves no debris behind.
        public static void CleanupFailedImport(string tempSavePath, string modsPath = "", string presetBasePath = "") {
            if (!string.IsNullOrEmpty(modsPath))
                FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled: false, removeFromListWhenDisabled: true);

            if (File.Exists(tempSavePath))
                File.Delete(tempSavePath);

            if (!string.IsNullOrEmpty(modsPath)) {
                string modFolder = Path.Combine(modsPath, ForemanExportModName);
                if (Directory.Exists(modFolder))
                    Directory.Delete(modFolder, true);
            }

            if (!string.IsNullOrEmpty(presetBasePath)) {
                foreach (string extension in new[] { ".pjson", ".json", ".dat" }) {
                    string path = presetBasePath + extension;
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
        }

        private static Result? CheckCrash(FactorioRunResult run, string phaseDescription, string scratchDirectory, string tempSavePath, string modsPath) {
            if (!run.Crashed)
                return null;

            ErrorLogging.LogLine("Foreman preset export: Factorio crash during " + phaseDescription + " (exit code " + run.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
            WriteExportFailureLog(scratchDirectory, run.Output);
            CleanupFailedImport(tempSavePath, modsPath);
            return Fail(
                "Factorio crashed while " + phaseDescription + ".\n\n" +
                "This is usually caused by a bug in one of your enabled mods, not by Foreman. " +
                "Open factorio-current.log in your Factorio user data folder for details, " +
                "then try disabling mods until Factorio can start a new game with the same mod list.");
        }

        //A failure log must never itself throw and mask the original failure it was trying to record.
        private static void WriteExportFailureLog(string scratchDirectory, string output) {
            try {
                Utf8File.WriteAllText(Path.Combine(scratchDirectory, "errorExporting.json"), output);
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Foreman preset export: failed to write errorExporting.json");
            }
        }

        private static string ExtractSection(string resultString, string startMarker, string endMarker) {
            int start = resultString.IndexOf(startMarker, StringComparison.Ordinal) + startMarker.Length;
            int end = resultString.IndexOf(endMarker, start, StringComparison.Ordinal);
            return resultString[start..end].Trim();
        }

        private static Result Fail(string message) => new() { Outcome = PresetImportOutcome.Failed, WarningMessage = message };
        private static Result Cancelled() => new() { Outcome = PresetImportOutcome.Cancel };
    }
}
