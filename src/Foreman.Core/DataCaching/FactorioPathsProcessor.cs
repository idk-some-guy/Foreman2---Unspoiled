using System;
using System.Collections.Generic;
using System.IO;

namespace Foreman.DataCaching {
    public static class FactorioPathsProcessor {
        public static List<string> GetFactorioInstallLocations(string? homeOverride = null, bool? isMacOsOverride = null) {
            //check default folders for a factorio installation (to fill in the path as the 'default')
            var factorioPaths = new List<string>();
            string home = homeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool isMacOs = isMacOsOverride ?? OperatingSystem.IsMacOS();

            if (isMacOs) {
                //app bundle installs. config-path.cfg is a Windows-only concept; a real macOS bundle is identified by its executable.
                foreach (string appContentsPath in new[] {
                    "/Applications/factorio.app/Contents",
                    Path.Combine(home, "Applications", "factorio.app", "Contents")
                }) {
                    if (File.Exists(GetExecutablePath(appContentsPath, isMacOs)))
                        factorioPaths.Add(appContentsPath);
                }

                AddSteamInstalls(factorioPaths, Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "libraryfolders.vdf"), isMacOs);
            } else {
                //Linux: a standalone install is whatever self-contained folder the user extracted the official
                //tarball to - ~/.factorio is the conventional default (wiki.factorio.com/Application_directory).
                string standaloneDir = Path.Combine(home, ".factorio");
                if (File.Exists(GetExecutablePath(standaloneDir, isMacOs)))
                    factorioPaths.Add(standaloneDir);

                //Steam on Linux has three common library-file locations depending on how the client itself was
                //installed: the native package's own data dir, and two ~/.steam symlink layouts (older "steam",
                //newer "root") that both point at the real client install.
                foreach (string steamRoot in new[] {
                    Path.Combine(home, ".local", "share", "Steam"),
                    Path.Combine(home, ".steam", "steam"),
                    Path.Combine(home, ".steam", "root"),
                })
                    AddSteamInstalls(factorioPaths, Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), isMacOs);
            }

            return factorioPaths;
        }

        private static void AddSteamInstalls(List<string> factorioPaths, string steamLibsVdf, bool isMacOs) {
            if (!File.Exists(steamLibsVdf))
                return;

            string[] steamLSettings = Utf8File.ReadAllLines(steamLibsVdf);
            foreach (string line in steamLSettings) {
                if (!line.Contains("\"path\""))
                    continue;

                string libraryPath = line[..line.LastIndexOf('"')];
                libraryPath = libraryPath[(libraryPath.LastIndexOf('"') + 1)..];
                //Steam's own game folder is lowercase "factorio" on Linux; APFS is case-insensitive by default
                //but not always on macOS, so try both there too.
                foreach (string gameFolder in new[] { "Factorio", "factorio" }) {
                    string installDir = Path.Combine(libraryPath, "steamapps", "common", gameFolder);
                    string contentsPath = isMacOs ? Path.Combine(installDir, "factorio.app", "Contents") : installDir;
                    if (File.Exists(GetExecutablePath(contentsPath, isMacOs))) {
                        factorioPaths.Add(contentsPath);
                        break;
                    }
                }
            }
        }

        public static string GetFactorioUserPath(string installPath, bool verboseFail = false, string? homeOverride = null, bool? isMacOsOverride = null) {
            bool isMacOs = isMacOsOverride ?? OperatingSystem.IsMacOS();

            //find config-path.cfg, read it, and use it to find config.ini
            string configPath = Path.Combine(installPath, "config-path.cfg");
            if (!File.Exists(configPath))
                return GetFactorioUserPathFromDefaultLocation(installPath, verboseFail, homeOverride, isMacOs);

            string config = Utf8File.ReadAllText(configPath);
            string configIniPath = Path.Combine(ProcessPathString(config[12..config.IndexOf('\n')], installPath, homeOverride, isMacOs), "config.ini");

            //read config.ini file
            if (!File.Exists(configIniPath)) {
                if (verboseFail)
                    ErrorLogging.LogLine("config.ini could not be found. Factorio setup is corrupted?");
                ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "config.ini file was not found at {0}. config-path.cfg was at {1} and linked here.", configIniPath, configPath));
                return "";
            }
            return ReadWriteDataFromConfigIni(configIniPath, installPath, homeOverride, isMacOs);
        }

        //Neither a macOS app bundle (Steam or otherwise) nor a self-contained Linux install (tarball or Steam)
        //ships config-path.cfg; that's a Windows/standalone concept. Factorio still writes user data to a fixed
        //per-user folder on both, so fall back to reading its config.ini directly from that default location.
        private static string GetFactorioUserPathFromDefaultLocation(string installPath, bool verboseFail, string? homeOverride, bool isMacOs) {
            string home = homeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string defaultFactorioDir = isMacOs
                ? Path.Combine(home, "Library", "Application Support", "factorio")
                : Path.Combine(home, ".factorio");
            string configIniPath = Path.Combine(defaultFactorioDir, "config", "config.ini");
            if (!File.Exists(configIniPath)) {
                if (verboseFail)
                    ErrorLogging.LogLine("config-path.cfg missing from the install location, and no default user data folder was found. Maybe run Factorio once to ensure all files are there?\nAlternatively a reinstall might be required.");
                ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "config-path.cfg was not found at {0}, and no config.ini was found at the default location {1}.", Path.Combine(installPath, "config-path.cfg"), configIniPath));
                return "";
            }
            return ReadWriteDataFromConfigIni(configIniPath, installPath, homeOverride, isMacOs);
        }

        private static string ReadWriteDataFromConfigIni(string configIniPath, string installPath, string? homeOverride, bool isMacOs) {
            string[] configIni = Utf8File.ReadAllLines(configIniPath);
            string writePath = "";
            foreach (string line in configIni)
                if (line.Contains("write-data", StringComparison.Ordinal) && !line.StartsWith(';'))
                    writePath = line[(line.IndexOf("write-data", StringComparison.Ordinal) + 11)..];

            return ProcessPathString(writePath, installPath, homeOverride, isMacOs);
        }

        private static string ProcessPathString(string input, string installPath, string? homeOverride, bool isMacOs) {
            if (input.StartsWith(".factorio", StringComparison.Ordinal)) {
                string path = installPath;
                string folder = (input == ".factorio") ? "" : input[9..];
                if (folder.Length > 0)
                    folder = folder[1..];
                while (folder.Contains("..", StringComparison.Ordinal)) {
                    path = Path.GetDirectoryName(path) ?? path;
                    folder = folder[(folder.IndexOf("..", StringComparison.Ordinal) + 2)..];
                    if (folder.Length > 0)
                        folder = folder[1..];
                }
                return string.IsNullOrEmpty(folder) ? path : Path.Combine(path, folder);
            } else if (input.StartsWith("__PATH__executable__", StringComparison.Ordinal)) {
                string path = isMacOs ? Path.Combine(installPath, "MacOS") : Path.Combine(installPath, "bin", "x64");
                string folder = string.Equals(input, "__PATH__executable__", StringComparison.Ordinal) ? "" : input[20..];
                if (folder.Length > 0)
                    folder = folder[1..];
                while (folder.Contains("..", StringComparison.Ordinal)) {
                    path = Path.GetDirectoryName(path) ?? path;
                    folder = folder[(folder.IndexOf("..", StringComparison.Ordinal) + 2)..];
                    if (folder.Length > 0)
                        folder = folder[1..];
                }
                return string.IsNullOrEmpty(folder) ? path : Path.Combine(path, folder);
            } else if (input.StartsWith("__PATH__system-write-data__", StringComparison.Ordinal)) {
                //macOS resolves this to the user's Application Support folder plus "factorio". Linux has no
                //such token documented on the wiki, but community reports of the engine's own resolution
                //(forums.factorio.com/viewtopic.php?t=100520, quoting getpwuid(getuid())->pw_dir + "/.factorio")
                //and the wiki's own default Linux data location (wiki.factorio.com/Application_directory,
                //"~/.factorio") agree it's $HOME/.factorio directly, not an XDG path - unverified against real
                //engine source (no Linux host here), so treat as best-effort pending a real install to check.
                if (isMacOs) {
                    string path = Path.Combine(homeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
                    string macFolder = string.Equals(input, "__PATH__system-write-data__", StringComparison.Ordinal) ? "" : input[27..];
                    if (macFolder.Length > 0)
                        macFolder = macFolder[1..];
                    while (macFolder.Contains("..", StringComparison.Ordinal)) {
                        path = Path.GetDirectoryName(path) ?? path;
                        macFolder = macFolder[(macFolder.IndexOf("..", StringComparison.Ordinal) + 2)..];
                        if (macFolder.Length > 0)
                            macFolder = macFolder[1..];
                    }
                    return string.IsNullOrEmpty(macFolder) ? Path.Combine(path, "factorio") : Path.Combine(path, "factorio", macFolder);
                }

                string linuxPath = homeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string linuxFolder = string.Equals(input, "__PATH__system-write-data__", StringComparison.Ordinal) ? "" : input[27..];
                if (linuxFolder.Length > 0)
                    linuxFolder = linuxFolder[1..];
                while (linuxFolder.Contains("..", StringComparison.Ordinal)) {
                    linuxPath = Path.GetDirectoryName(linuxPath) ?? linuxPath;
                    linuxFolder = linuxFolder[(linuxFolder.IndexOf("..", StringComparison.Ordinal) + 2)..];
                    if (linuxFolder.Length > 0)
                        linuxFolder = linuxFolder[1..];
                }
                return string.IsNullOrEmpty(linuxFolder) ? Path.Combine(linuxPath, ".factorio") : Path.Combine(linuxPath, ".factorio", linuxFolder);
            } else
                ErrorLogging.LogLine("path string (from one of the config files) did not start as expected (.factorio || __PATH__executable__ || __PATH__system-write-data__). Path string:" + input);

            return installPath; //something weird must have happened to end up here. Honesty these path conversions are a bit of a mess - not enough examples to be sure its correct (works with all case 'I' have...)
        }

        public static string GetExecutablePath(string installPath, bool? isMacOsOverride = null) =>
            (isMacOsOverride ?? OperatingSystem.IsMacOS())
                ? Path.Combine(installPath, "MacOS", "factorio")
                : Path.Combine(installPath, "bin", "x64", "factorio");

        public static bool TryNormalizeInstallPath(string selectedPath, out string installRoot, bool? isMacOsOverride = null) {
            bool isMacOs = isMacOsOverride ?? OperatingSystem.IsMacOS();
            installRoot = selectedPath;
            if (File.Exists(GetExecutablePath(selectedPath, isMacOs)))
                return true;
            //macOS-only fallback: a user can pick the .app itself rather than its Contents folder.
            if (isMacOs && File.Exists(Path.Combine(selectedPath, "Contents", "MacOS", "factorio"))) {
                installRoot = Path.Combine(selectedPath, "Contents");
                return true;
            }
            return false;
        }
    }
}
