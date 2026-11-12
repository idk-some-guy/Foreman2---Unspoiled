using Foreman.DataCaching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PresetRig {
    internal static class Program {
        const int ProbeTicks = 3100;
        const int FinalTickMargin = 20;
        const int ExportTicks = 1;
        static readonly string[] PngFolders = ["core", "base", "elevated-rails", "quality", "recycler", "space-age"];
        static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        static async Task<int> Main(string[] args) {
            if (args.Length == 0)
                return Usage();
            var flags = ParseFlags(args[1..]);
            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
            try {
                return args[0] switch {
                    "probe" => await ProbeAsync(flags, cancel.Token),
                    "export" => await ExportAsync(flags, cancel.Token),
                    "readsave" => await ReadSaveAsync(flags, cancel.Token),
                    "build" => await BuildAsync(flags, cancel.Token),
                    "pngs" => CountPngs(flags),
                    _ => Usage(),
                };
            } catch (OperationCanceledException) {
                Console.Error.WriteLine("The run timed out or was cancelled.");
                return 1;
            } catch (ArgumentException exception) {
                Console.Error.WriteLine(exception.Message);
                return Usage();
            }
        }

        static int Usage() {
            Console.Error.WriteLine("probe --source docker|steam [--image <tag>] [--install <path>] [--binary <path>] --out <file> [--log <file>] [--keep]");
            Console.Error.WriteLine("export --source docker|steam [--image <tag>] [--install <path>] [--binary <path>] --set vanilla|space-age --mod <folder> [--out <file>] [--evidence <file>] [--keep]");
            Console.Error.WriteLine("readsave --source docker|steam [--image <tag>] [--install <path>] [--binary <path>] --set vanilla|space-age --reader <mod folder> [--test-research] --out <file> [--keep]");
            Console.Error.WriteLine("build --export <file> --data <Factorio data folder> --name <preset name> --out <folder>");
            Console.Error.WriteLine("pngs --install <path>");
            return 2;
        }

        static Dictionary<string, string> ParseFlags(string[] args) {
            var flags = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++) {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    continue;
                string key = args[i].Substring(2);
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                flags[key] = hasValue ? args[++i] : "true";
            }
            return flags;
        }

        static RigOptions BuildOptions(Dictionary<string, string> flags, ModSet set) {
            RigSource source = flags.GetValueOrDefault("source", "docker") switch {
                "docker" => RigSource.Docker,
                "steam" => RigSource.Steam,
                string other => throw new ArgumentException("unknown source " + other),
            };
            return new RigOptions(source, flags.GetValueOrDefault("image", RigRun.DefaultImage), flags.GetValueOrDefault("install"), flags.GetValueOrDefault("binary"), flags.ContainsKey("keep"), set);
        }

        static string FindToolsFolder(string leaf) {
            for (string? directory = AppContext.BaseDirectory; directory != null; directory = Path.GetDirectoryName(directory)) {
                string candidate = Path.Combine(directory, "tools", "PresetRig", leaf);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            throw new DirectoryNotFoundException("tools/PresetRig/" + leaf + " not found above " + AppContext.BaseDirectory);
        }

        static async Task<int> ProbeAsync(Dictionary<string, string> flags, CancellationToken token) {
            if (!flags.TryGetValue("out", out string? outPath))
                return Usage();
            RigOptions options = BuildOptions(flags, ModSet.SpaceAge);
            using var run = new RigRun(options);
            ProcessResult create = await run.CreateSaveAsync(token);
            if (!File.Exists(run.SavePath))
                return Fail(run, flags, "Factorio did not create the test save (exit " + create.ExitCode.ToString(CultureInfo.InvariantCulture) + ").", create.Output);

            string modName = run.InstallMod(Path.Combine(FindToolsFolder("probe"), "foremanprobe_2.1.0"));
            run.SetFinalTick(Path.Combine("foremanprobe_2.1.0", "instrument-control.lua"), ProbeTicks - FinalTickMargin);
            ProcessResult instrument = await run.InstrumentAsync(modName, ProbeTicks, token);
            WriteLog(run, flags, create.Output + "\n----\n" + instrument.Output);

            string? block = RigRun.ExtractProbeBlock(instrument.Output);
            JsonObject? probe = null;
            if (block != null) {
                try {
                    probe = JsonNode.Parse(block) as JsonObject;
                } catch (JsonException) {
                }
            }
            if (probe == null)
                return Fail(run, flags, "The probe printed no readable block (exit codes " + create.ExitCode.ToString(CultureInfo.InvariantCulture) + " and " + instrument.ExitCode.ToString(CultureInfo.InvariantCulture) + ").", instrument.Output);

            var ordered = new JsonObject {
                ["headless_instrument"] = new JsonObject {
                    ["image"] = options.Source == RigSource.Docker ? options.Image : null,
                    ["source"] = options.Source.ToString().ToLowerInvariant(),
                    ["binary"] = run.BinaryRelativeToInstall(),
                    ["create_exit_code"] = create.ExitCode,
                    ["instrument_exit_code"] = instrument.ExitCode,
                    ["save_created"] = true,
                    ["probe_block_present"] = true,
                },
            };
            foreach (KeyValuePair<string, JsonNode?> entry in probe.ToList()) {
                probe.Remove(entry.Key);
                ordered[entry.Key] = entry.Value;
            }
            File.WriteAllText(outPath, run.Scrub(ordered.ToJsonString(Indented)) + "\n");
            Console.WriteLine("Wrote " + outPath);
            return 0;
        }

        static async Task<int> ExportAsync(Dictionary<string, string> flags, CancellationToken token) {
            if (!flags.TryGetValue("mod", out string? modFolder))
                return Usage();
            ModSet set = flags.GetValueOrDefault("set", "vanilla") == "space-age" ? ModSet.SpaceAge : ModSet.Vanilla;
            RigOptions options = BuildOptions(flags, set);
            using var run = new RigRun(options);
            ProcessResult create = await run.CreateSaveAsync(token);
            if (!File.Exists(run.SavePath))
                return Fail(run, flags, "Factorio did not create the test save (exit " + create.ExitCode.ToString(CultureInfo.InvariantCulture) + ").", create.Output);

            string modName = run.InstallMod(modFolder);
            ProcessResult instrument = await run.InstrumentAsync(modName, ExportTicks, token);
            string scrubbedOutput = run.Scrub(instrument.Output);
            if (flags.TryGetValue("out", out string? outPath))
                File.WriteAllText(outPath, scrubbedOutput);

            if (flags.TryGetValue("evidence", out string? evidencePath)) {
                JsonObject evidence = File.Exists(evidencePath) ? (JsonNode.Parse(File.ReadAllText(evidencePath)) as JsonObject ?? []) : [];
                var lines = new JsonArray();
                foreach (string line in LegacyLogLines(run.Scrub(run.ReadLog() + "\n" + instrument.Output), modName))
                    lines.Add(line);
                evidence["legacy_mod_load"] = new JsonObject {
                    ["mod"] = modName,
                    ["mod_factorio_version"] = RigRun.ReadModFactorioVersion(modFolder),
                    ["exit_code"] = instrument.ExitCode,
                    ["export_started"] = instrument.Output.Contains("<<<START-EXPORT-LN>>>", StringComparison.Ordinal),
                    ["export_finished"] = instrument.Output.Contains("<<<END-EXPORT-P2>>>", StringComparison.Ordinal),
                    ["log_lines"] = lines,
                };
                File.WriteAllText(evidencePath, evidence.ToJsonString(Indented) + "\n");
                Console.WriteLine("Merged legacy_mod_load into " + evidencePath);
            }
            Console.WriteLine("Export run exit code " + instrument.ExitCode.ToString(CultureInfo.InvariantCulture));
            if (flags.ContainsKey("evidence"))
                return 0;
            if (instrument.Output.Contains("<<<END-EXPORT-P1>>>", StringComparison.Ordinal) && instrument.Output.Contains("<<<END-EXPORT-P2>>>", StringComparison.Ordinal))
                return 0;
            return instrument.ExitCode != 0 ? instrument.ExitCode : 1;
        }

        static async Task<int> ReadSaveAsync(Dictionary<string, string> flags, CancellationToken token) {
            if (!flags.TryGetValue("reader", out string? readerFolder) || !flags.TryGetValue("out", out string? outPath))
                return Usage();
            ModSet set = flags.GetValueOrDefault("set", "vanilla") == "space-age" ? ModSet.SpaceAge : ModSet.Vanilla;
            RigOptions options = BuildOptions(flags, set);
            using var run = new RigRun(options);
            ProcessResult create = await run.CreateSaveAsync(token);
            if (!File.Exists(run.SavePath))
                return Fail(run, flags, "Factorio did not create the test save (exit " + create.ExitCode.ToString(CultureInfo.InvariantCulture) + ").", create.Output);

            string readerName = run.InstallMod(readerFolder);
            run.WriteBoolStartupSetting("foreman-savereader-test-research", flags.ContainsKey("test-research"));
            ProcessResult instrument = await run.InstrumentAsync(readerName, ExportTicks, token);
            const string startMarker = "<<<START-EXPORT-P0>>>";
            const string endMarker = "<<<END-EXPORT-P0>>>";
            int start = instrument.Output.IndexOf(startMarker, StringComparison.Ordinal);
            int end = instrument.Output.IndexOf(endMarker, StringComparison.Ordinal);
            if (start < 0 || end < start)
                return Fail(run, flags, "The reader printed no export block (exit " + instrument.ExitCode.ToString(CultureInfo.InvariantCulture) + ").", instrument.Output);
            File.WriteAllText(outPath, run.Scrub(instrument.Output.Substring(start, end + endMarker.Length - start)) + "\n");
            Console.WriteLine("Wrote " + outPath);
            return 0;
        }

        static async Task<int> BuildAsync(Dictionary<string, string> flags, CancellationToken token) {
            if (!flags.TryGetValue("export", out string? exportPath) || !flags.TryGetValue("data", out string? dataPath)
                || !flags.TryGetValue("name", out string? presetName) || !flags.TryGetValue("out", out string? outFolder))
                return Usage();
            string scratch = Path.Combine(Path.GetTempPath(), "foreman-rig-build-" + Path.GetRandomFileName());
            Directory.CreateDirectory(scratch);
            bool keepScratch = false;
            try {
                using IDisposable isolatedLog = ErrorLogging.UseIsolatedLogDirectory(scratch);
                string modsPath = Path.Combine(scratch, "mods");
                Directory.CreateDirectory(modsPath);
                PresetImporter.Result result = await PresetImporter.WritePresetFromExportOutput(
                    File.ReadAllText(exportPath), dataPath, modsPath, scratch, Path.Combine(outFolder, presetName),
                    new Progress<KeyValuePair<int, string>>(), (failed, total) => Task.FromResult(true), token);
                if (result.Outcome != PresetImportOutcome.Ok) {
                    keepScratch = true;
                    Console.Error.WriteLine(result.WarningMessage ?? "The preset was not written.");
                    Console.Error.WriteLine("Failure dumps and the log are in " + scratch);
                    return 1;
                }
                Console.WriteLine("Wrote " + Path.Combine(outFolder, presetName) + ".pjson, .json and .dat");
                return 0;
            } finally {
                if (!keepScratch)
                    Directory.Delete(scratch, true);
            }
        }

        static IEnumerable<string> LegacyLogLines(string text, string modName) {
            var seen = new HashSet<string>();
            foreach (string raw in text.Split('\n')) {
                string line = raw.TrimEnd('\r');
                bool relevant = line.Contains(modName, StringComparison.OrdinalIgnoreCase)
                    || line.Contains("factorio_version", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("incompatible", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Error", StringComparison.Ordinal);
                if (relevant && seen.Add(line))
                    yield return line;
            }
        }

        static int CountPngs(Dictionary<string, string> flags) {
            string install = RigRun.SteamInstall(new RigOptions(RigSource.Steam, "", flags.GetValueOrDefault("install"), null, false, ModSet.Vanilla));
            string dataFolder = Path.Combine(install, "factorio.app", "Contents", "data");
            int total = 0;
            foreach (string folder in PngFolders) {
                string path = Path.Combine(dataFolder, folder);
                int count = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.png", SearchOption.AllDirectories).Count() : 0;
                total += count;
                Console.WriteLine(folder + ": " + count.ToString(CultureInfo.InvariantCulture));
            }
            Console.WriteLine("total: " + total.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        static void WriteLog(RigRun run, Dictionary<string, string> flags, string text) {
            if (flags.TryGetValue("log", out string? logPath))
                File.WriteAllText(logPath, run.Scrub(text));
        }

        static int Fail(RigRun run, Dictionary<string, string> flags, string message, string output) {
            WriteLog(run, flags, output);
            Console.Error.WriteLine(message);
            Console.Error.WriteLine(run.Scrub(output.Length > 4000 ? output.Substring(output.Length - 4000) : output));
            return 1;
        }
    }
}
