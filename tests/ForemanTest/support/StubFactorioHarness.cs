using System;
using System.IO;
using System.Runtime.Versioning;

namespace ForemanTest.support {
    // Builds shell-script stand-ins for factorio's CLI so tests don't launch the real binary.
    [UnsupportedOSPlatform("windows")]
    internal static class StubFactorioHarness {
        public static string CreateExecutableDirectory() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "factorio.app", "Contents", "MacOS");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string WriteScript(string macOsDir, string scriptBody) {
            string path = Path.Combine(macOsDir, "factorio");
            File.WriteAllText(path, "#!/bin/sh\n" + scriptBody);
            MakeExecutable(path);
            return path;
        }

        /// <summary>Prints the given stdout verbatim and exits with the given code.</summary>
        public static string WriteEchoScript(string macOsDir, string stdout, int exitCode = 0) =>
            WriteScript(macOsDir, "printf '%s' " + ShellQuote(stdout) + "\nexit " + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");

        /// <summary>Emits one of the upstream crash-detector strings, then exits non-zero like a crashed Factorio would.</summary>
        public static string WriteCrashScript(string macOsDir, string crashMarker = "Received SIGSEGV") =>
            WriteEchoScript(macOsDir, crashMarker + "\n", exitCode: 1);

        /// <summary>Emits the instance-lock string another running Factorio prints, then exits non-zero.</summary>
        public static string WriteAnotherInstanceScript(string macOsDir) =>
            WriteEchoScript(macOsDir, "Is another instance already running?\n", exitCode: 1);

        /// <summary>Responds to --version with a single "Version: x.y.z (...)" line; ignores every other argument.</summary>
        public static string WriteVersionScript(string macOsDir, string version) =>
            WriteScript(macOsDir, "printf 'Version: " + version + " (build 1, mac64, headless)\\n'\nexit 0\n");

        /// <summary>Emits the export pipeline's four marker-delimited sections and, unless suppressed, creates temp-save.zip in the caller's working directory - matching the real CLI contract, where "--create temp-save.zip" resolves relative to the process's own working directory, not the script's own location.</summary>
        public static string WriteExportScript(string macOsDir, string lnSection, string p1Section, string p2Section, bool createTempSave = true) {
            string body =
                (createTempSave ? "touch ./temp-save.zip\n" : "") +
                "cat <<'FOREMAN_EOF'\n" +
                "<<<START-EXPORT-LN>>>\n" + lnSection + "\n<<<END-EXPORT-LN>>>\n" +
                "<<<START-EXPORT-P1>>>\n" + p1Section + "\n<<<END-EXPORT-P1>>>\n" +
                "<<<START-EXPORT-P2>>>\n" + p2Section + "\n<<<END-EXPORT-P2>>>\n" +
                "FOREMAN_EOF\n" +
                "exit 0\n";
            return WriteScript(macOsDir, body);
        }

        /// <summary>Emits the save-read pipeline's single P0 marker section.</summary>
        public static string WriteSaveReadScript(string macOsDir, string p0Section) {
            string body =
                "cat <<'FOREMAN_EOF'\n" +
                "<<<START-EXPORT-P0>>>\n" + p0Section + "\n<<<END-EXPORT-P0>>>\n" +
                "FOREMAN_EOF\n" +
                "exit 0\n";
            return WriteScript(macOsDir, body);
        }

        /// <summary>Sleeps for the given duration before printing anything, so a caller can cancel mid-run.</summary>
        public static string WriteSleepThenEchoScript(string macOsDir, double sleepSeconds, string stdout) =>
            WriteScript(macOsDir, "sleep " + sleepSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\nprintf '%s' " + ShellQuote(stdout) + "\nexit 0\n");

        private static void MakeExecutable(string path) {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }

        private static string ShellQuote(string value) =>
            "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
