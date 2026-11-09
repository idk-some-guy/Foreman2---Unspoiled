using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PresetRig {
    internal enum RigSource { Docker, Steam }

    internal enum ModSet { Vanilla, SpaceAge }

    internal sealed record RigOptions(RigSource Source, string Image, string? Install, string? Binary, bool Keep, ModSet Set);

    internal sealed record ProcessResult(int ExitCode, string Output);

    internal sealed class RigRun : IDisposable {
        public const string DefaultImage = "factoriotools/factorio:2.1.24";
        public const string DockerBinary = "/opt/homebrew/bin/docker";
        public const string ImageFactorioBinary = "/opt/factorio/bin/arm64/factorio";
        const string ContainerRoot = "/rig";
        const string StartMarker = "<<<START-PROBE>>>";
        const string EndMarker = "<<<END-PROBE>>>";
        static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(15);

        readonly RigOptions options;
        readonly string containerName = "rig-" + Guid.NewGuid().ToString("N");

        public string Folder { get; }
        public string ModsFolder => Path.Combine(Folder, "mods");
        public string WriteFolder => Path.Combine(Folder, "write");
        public string ConfigPath => Path.Combine(Folder, "config.ini");

        public RigRun(RigOptions options) {
            this.options = options;
            Folder = Path.Combine(Path.GetTempPath(), "rig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ModsFolder);
            Directory.CreateDirectory(WriteFolder);
            File.WriteAllText(ConfigPath, BuildConfig());
            File.WriteAllText(Path.Combine(ModsFolder, "mod-list.json"), BuildModList(null));
        }

        string BuildConfig() {
            string readData = options.Source == RigSource.Docker ? "__PATH__executable__/../../data" : "__PATH__executable__/../data";
            string write = options.Source == RigSource.Docker ? ContainerRoot + "/write" : WriteFolder;
            return "[path]\nread-data=" + readData + "\nwrite-data=" + write + "\n\n[general]\nlocale=\n";
        }

        string BuildModList(string? extraMod) {
            var names = new List<string> { "base" };
            if (options.Set == ModSet.SpaceAge)
                names.AddRange(["elevated-rails", "quality", "recycler", "space-age"]);
            if (extraMod != null)
                names.Add(extraMod);
            var mods = new JsonArray();
            foreach (string name in names)
                mods.Add(new JsonObject { ["name"] = name, ["enabled"] = true });
            return new JsonObject { ["mods"] = mods }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        public static string ReadModName(string modFolder) {
            using JsonDocument info = JsonDocument.Parse(File.ReadAllText(Path.Combine(modFolder, "info.json")));
            return info.RootElement.GetProperty("name").GetString() ?? throw new InvalidDataException("info.json has no name");
        }

        public static string ReadModFactorioVersion(string modFolder) {
            using JsonDocument info = JsonDocument.Parse(File.ReadAllText(Path.Combine(modFolder, "info.json")));
            return info.RootElement.TryGetProperty("factorio_version", out JsonElement value) ? value.GetString() ?? "" : "";
        }

        public void SetFinalTick(string fileInMods, int tick) {
            string path = Path.Combine(ModsFolder, fileInMods);
            string text = File.ReadAllText(path);
            var pattern = new Regex(@"^local FINAL_TICK = \d+", RegexOptions.Multiline);
            if (!pattern.IsMatch(text))
                throw new InvalidDataException("no FINAL_TICK in " + fileInMods);
            File.WriteAllText(path, pattern.Replace(text, "local FINAL_TICK = " + tick.ToString(CultureInfo.InvariantCulture), 1));
        }

        public string InstallMod(string sourceFolder) {
            string name = ReadModName(sourceFolder);
            CopyDirectory(sourceFolder, Path.Combine(ModsFolder, Path.GetFileName(sourceFolder.TrimEnd('/'))));
            File.WriteAllText(Path.Combine(ModsFolder, "mod-list.json"), BuildModList(name));
            return name;
        }

        static void CopyDirectory(string source, string target) {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }

        string RigPath(string file) => options.Source == RigSource.Docker ? ContainerRoot + "/" + file : Path.Combine(Folder, file);

        public string SavePath => Path.Combine(Folder, "temp-save.zip");

        public Task<ProcessResult> CreateSaveAsync(CancellationToken token) =>
            RunFactorioAsync(["--config", RigPath("config.ini"), "--mod-directory", RigPath("mods"), "--create", RigPath("temp-save.zip")], token);

        public Task<ProcessResult> InstrumentAsync(string modName, int ticks, CancellationToken token) =>
            RunFactorioAsync([
                "--config", RigPath("config.ini"), "--mod-directory", RigPath("mods"),
                "--instrument-mod", modName, "--benchmark", RigPath("temp-save.zip"),
                "--benchmark-ticks", ticks.ToString(CultureInfo.InvariantCulture), "--benchmark-runs", "1"
            ], token);

        async Task<ProcessResult> RunFactorioAsync(IReadOnlyList<string> factorioArguments, CancellationToken token) {
            var startInfo = new ProcessStartInfo {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            if (options.Source == RigSource.Docker) {
                startInfo.FileName = DockerBinary;
                foreach (string argument in new[] { "run", "--rm", "--name", containerName, "-v", Folder + ":" + ContainerRoot, "--entrypoint", options.Binary ?? ImageFactorioBinary, options.Image })
                    startInfo.ArgumentList.Add(argument);
            } else {
                startInfo.FileName = options.Binary ?? Path.Combine(SteamInstall(options), "factorio.app", "Contents", "MacOS", "factorio");
            }
            foreach (string argument in factorioArguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            void Append(string? line) {
                if (line == null)
                    return;
                lock (output)
                    output.AppendLine(line);
            }
            process.OutputDataReceived += (_, e) => Append(e.Data);
            process.ErrorDataReceived += (_, e) => Append(e.Data);
            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(RunTimeout);
            try {
                await process.WaitForExitAsync(timeout.Token);
                process.WaitForExit();
            } catch (OperationCanceledException) {
                if (options.Source == RigSource.Docker)
                    await StopContainerAsync();
                process.Kill(true);
                throw;
            }
            lock (output)
                return new ProcessResult(process.ExitCode, output.ToString());
        }

        async Task StopContainerAsync() {
            using var stop = Process.Start(new ProcessStartInfo(DockerBinary, ["rm", "-f", containerName]) { RedirectStandardOutput = true, RedirectStandardError = true });
            if (stop != null)
                await stop.WaitForExitAsync();
        }

        public static string SteamInstall(RigOptions options) =>
            options.Install ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam", "steamapps", "common", "Factorio");

        public static string? ExtractProbeBlock(string output) {
            int start = output.IndexOf(StartMarker, StringComparison.Ordinal);
            int end = output.IndexOf(EndMarker, StringComparison.Ordinal);
            if (start < 0 || end < start)
                return null;
            return output.Substring(start + StartMarker.Length, end - start - StartMarker.Length).Trim();
        }

        public string BinaryRelativeToInstall() {
            string binary = options.Binary ?? (options.Source == RigSource.Docker ? ImageFactorioBinary : "factorio.app/Contents/MacOS/factorio");
            const string imageInstall = "/opt/factorio/";
            return binary.StartsWith(imageInstall, StringComparison.Ordinal) ? binary.Substring(imageInstall.Length) : binary;
        }

        public string Scrub(string text) {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string scrubbed = text;
            foreach (string folder in new[] { Folder, "/private" + Folder, Folder.StartsWith("/private", StringComparison.Ordinal) ? Folder.Substring("/private".Length) : Folder })
                scrubbed = scrubbed.Replace(folder, "<run>", StringComparison.Ordinal);
            if (options.Source == RigSource.Docker)
                scrubbed = Regex.Replace(scrubbed, Regex.Escape(ContainerRoot) + "(?=[/\\s\"']|$)", "<run>");
            return scrubbed.Replace(home, "~", StringComparison.Ordinal);
        }

        public string ReadLog() {
            string log = Path.Combine(WriteFolder, "factorio-current.log");
            return File.Exists(log) ? File.ReadAllText(log) : "";
        }

        public void Dispose() {
            if (options.Keep)
                return;
            try {
                if (Directory.Exists(Folder))
                    Directory.Delete(Folder, true);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }
    }
}
