using Foreman.DataCaching;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Foreman {
    public static class FactorioInstallValidator {
        public static bool TryValidateExecutable(string factorioExePath, [NotNullWhen(false)] out string? userMessage) {
            userMessage = null;
            if (!File.Exists(factorioExePath)) {
                userMessage = "Could not find factorio.exe. Please select a valid Factorio install location.";
                return false;
            }

            if (!TryGetVersion(factorioExePath, out int major, out int minor, out int build, out string versionText)) {
                userMessage = "Could not determine the Factorio version. Please select a valid Factorio install location.";
                ErrorLogging.LogLine("FactorioInstallValidator: could not determine version for " + factorioExePath);
                return false;
            }

            if (major < 2) {
                userMessage = "Factorio Version below 2.0 can not be used with this version of Foreman. Please use Factorio 2.0 or newer. Alternatively download dev.13 or under of foreman 2.0 for pre factorio 2.0.";
                ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Factorio version 0.x or 1.x instead of 2.x - use Foreman dev.13 or below for these factorio installs. ({0})", versionText));
                return false;
            }

            if (major > 2) {
                userMessage = "Factorio Version 3.x+ can not be used with this version of Foreman. Sit tight and wait for update...\nYou can also try to msg me on discord (u\\DanielKotes) if for some reason I am not already aware of this.";
                ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Factorio version 3.x+ isnt supported. ({0})", versionText));
                return false;
            }

            if (minor < 0 || (minor == 0 && build < 7)) {
                userMessage = "Factorio version (" + versionText + ") can not be used with Foreman. Please use Factorio 2.0.7 or newer.";
                ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture, "Factorio version was too old. {0} instead of 2.0.7+", versionText));
                return false;
            }

            return true;
        }

        private static bool TryGetVersion(string factorioExePath, out int major, out int minor, out int build, out string versionText) {
            if (TryReadStdoutVersion(factorioExePath, out versionText) && TryParseVersionParts(versionText, out major, out minor, out build))
                return true;

            if (TryReadBundleVersion(factorioExePath, out versionText) && TryParseVersionParts(versionText, out major, out minor, out build))
                return true;

            //non-bundle layout (e.g. a Windows factorio.exe with real PE version resources).
            var factorioVersionInfo = FileVersionInfo.GetVersionInfo(factorioExePath);
            versionText = factorioVersionInfo.ProductVersion ?? "";
            major = factorioVersionInfo.ProductMajorPart;
            minor = factorioVersionInfo.ProductMinorPart;
            build = factorioVersionInfo.ProductBuildPart;
            return !string.IsNullOrEmpty(versionText);
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

        private static bool TryParseVersionParts(string versionText, out int major, out int minor, out int build) {
            major = minor = build = 0;
            string[] parts = versionText.Split('.');
            if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major))
                return false;
            if (parts.Length > 1)
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
            if (parts.Length > 2)
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out build);
            return true;
        }
    }
}
