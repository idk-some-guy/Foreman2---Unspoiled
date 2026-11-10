using Foreman.DataCaching;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Foreman {
    public enum FactorioInstallProblem { None, ExecutableMissing, VersionUnreadable, TooOld, TooNew }

    public sealed record FactorioInstallCheck(FactorioInstallProblem Problem, string VersionText) {
        public FactorioGameVersion? GameVersion => FactorioGameVersion.TryParse(VersionText, out FactorioGameVersion version) ? version : null;
    }

    public static class FactorioInstallValidator {
        public static FactorioInstallCheck CheckExecutable(string factorioExePath) {
            if (!File.Exists(factorioExePath))
                return Rejected(new FactorioInstallCheck(FactorioInstallProblem.ExecutableMissing, ""));

            if (!TryGetVersion(factorioExePath, out FactorioGameVersion version, out string versionText)) {
                ErrorLogging.LogLine("FactorioInstallValidator: could not determine version for " + factorioExePath);
                return Rejected(new FactorioInstallCheck(FactorioInstallProblem.VersionUnreadable, ""));
            }

            FactorioInstallProblem problem = SupportedGameVersions.Classify(version);
            switch (problem) {
                case FactorioInstallProblem.TooOld when version.Major < 2:
                    ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Factorio version 0.x or 1.x instead of 2.x - use Foreman dev.13 or below for these factorio installs. ({0})", versionText));
                    break;
                case FactorioInstallProblem.TooOld:
                    ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Factorio version was too old. {0} instead of {1}+", versionText, SupportedGameVersions.MinimumFor(version)));
                    break;
                case FactorioInstallProblem.TooNew when version.Major > 2:
                    ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Factorio version 3.x+ isnt supported. ({0})", versionText));
                    break;
                case FactorioInstallProblem.TooNew:
                    ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Factorio version {0} is newer than this Foreman supports.", versionText));
                    break;
            }
            return problem == FactorioInstallProblem.None
                ? new FactorioInstallCheck(problem, versionText)
                : Rejected(new FactorioInstallCheck(problem, versionText));
        }

        private static FactorioInstallCheck Rejected(FactorioInstallCheck check) {
            ErrorLogging.LogLine("FactorioInstallValidator: " + UserMessage(check));
            return check;
        }

        internal static string? UserMessage(FactorioInstallCheck check) => check.Problem switch {
            FactorioInstallProblem.None => null,
            FactorioInstallProblem.ExecutableMissing => "Could not find factorio.exe. Please select a valid Factorio install location.",
            FactorioInstallProblem.VersionUnreadable => "Could not determine the Factorio version. Please select a valid Factorio install location.",
            FactorioInstallProblem.TooNew when check.GameVersion is { Major: <= 2 } =>
                "Factorio version (" + check.VersionText + ") is newer than this Foreman supports. Sit tight and wait for update...",
            FactorioInstallProblem.TooNew => "Factorio Version 3.x+ can not be used with this version of Foreman. Sit tight and wait for update...\nYou can also try to msg me on discord (u\\DanielKotes) if for some reason I am not already aware of this.",
            _ when check.GameVersion is { Major: < 2 } =>
                "Factorio Version below 2.0 can not be used with this version of Foreman. Please use Factorio 2.0 or newer. Alternatively download dev.13 or under of foreman 2.0 for pre factorio 2.0.",
            _ => "Factorio version (" + check.VersionText + ") can not be used with Foreman. Please use Factorio " + (check.GameVersion is { } version ? SupportedGameVersions.MinimumFor(version) : SupportedGameVersions.Lines[0].Minimum) + " or newer.",
        };

        private static bool TryGetVersion(string factorioExePath, out FactorioGameVersion version, out string versionText) {
            if (TryReadStdoutVersion(factorioExePath, out versionText) && FactorioGameVersion.TryParse(versionText, out version))
                return true;

            if (TryReadBundleVersion(factorioExePath, out versionText) && FactorioGameVersion.TryParse(versionText, out version))
                return true;

            //non-bundle layout (e.g. a Windows factorio.exe with real PE version resources).
            return TryParseProductVersion(FileVersionInfo.GetVersionInfo(factorioExePath).ProductVersion, out version, out versionText);
        }

        internal static bool TryParseProductVersion(string? productVersion, out FactorioGameVersion version, out string versionText) {
            versionText = productVersion ?? "";
            return FactorioGameVersion.TryParse(versionText, out version);
        }

        //primary source: run the executable itself with --version and read its own stdout.
        private static bool TryReadStdoutVersion(string factorioExePath, out string versionText) {
            versionText = "";
            FactorioRunResult result;
            try {
                string workingDirectory = Path.GetDirectoryName(factorioExePath) ?? "";
                result = FactorioBenchmarkRunner.Run(factorioExePath, "--version", workingDirectory, CancellationToken.None);
            } catch (Exception ex) when (ex is Win32Exception or IOException) {
                return false;
            }
            if (result.ExitCode != 0)
                return false;

            foreach (string line in result.Output.Split('\n')) {
                Match match = Regex.Match(line, @"^Version:\s*(\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                if (match.Success) {
                    versionText = match.Groups[1].Value;
                    return true;
                }
            }
            return false;
        }

        //a macOS bundle's executable lives at Contents/MacOS/<name>; FileVersionInfo reads PE resources and
        //is always empty for Mach-O, so read the version straight out of Contents/Info.plist instead.
        private static bool TryReadBundleVersion(string factorioExePath, out string versionText) {
            versionText = "";
            string? macOsDir = Path.GetDirectoryName(factorioExePath);
            string? contentsDir = macOsDir is null ? null : Path.GetDirectoryName(macOsDir);
            if (contentsDir is null)
                return false;

            string plistPath = Path.Combine(contentsDir, "Info.plist");
            if (!File.Exists(plistPath))
                return false;

            try {
                XElement? dict = XDocument.Load(plistPath).Root?.Element("dict");
                return dict is not null && (
                    TryReadPlistString(dict, "CFBundleShortVersionString", out versionText) ||
                    TryReadPlistString(dict, "CFBundleVersion", out versionText));
            } catch (Exception ex) when (ex is IOException or XmlException) {
                ErrorLogging.LogException(ex, "FactorioInstallValidator: failed to read " + plistPath);
                return false;
            }
        }

        //plist dicts are flat key/value pairs: a <key> element followed by its value element.
        private static bool TryReadPlistString(XElement dict, string key, out string value) {
            value = "";
            XElement[] entries = [.. dict.Elements()];
            for (int i = 0; i < entries.Length - 1; i++) {
                if (entries[i].Name.LocalName == "key" && entries[i].Value == key && entries[i + 1].Name.LocalName == "string") {
                    value = entries[i + 1].Value;
                    return true;
                }
            }
            return false;
        }
    }
}
