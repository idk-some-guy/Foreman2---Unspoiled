using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Foreman.DataCaching {
    /// <summary>Append-only error log (errorlog.txt). Caught exceptions use <see cref="LogException"/>; UI shows generic text.</summary>
    public static class ErrorLogging {
        private static readonly AsyncLocal<string?> logDirectoryOverride = new();
        private static string? defaultLogDirectoryOverride;

        public static string LogFilePath => Path.Combine(logDirectoryOverride.Value ?? defaultLogDirectoryOverride ?? AppPaths.UserDataDirectory, "errorlog.txt");

        /// <summary>Process-wide fallback used whenever no <see cref="UseIsolatedLogDirectory"/> flow is active.
        /// Test-only seam: each test assembly's init hook points this at a per-run temp directory so no test
        /// can reach the real user profile path even without wrapping every call site.</summary>
        internal static void SetDefaultLogDirectory(string? directory) => defaultLogDirectoryOverride = directory;

        internal static string? DefaultLogDirectory => defaultLogDirectoryOverride;

        /// <summary>Redirects this async flow's log calls to an isolated directory until disposed. Test-only seam.</summary>
        public static IDisposable UseIsolatedLogDirectory(string directory) {
            string? previous = logDirectoryOverride.Value;
            logDirectoryOverride.Value = directory;
            return new LogDirectoryRestorer(previous);
        }

        private sealed class LogDirectoryRestorer : IDisposable {
            private readonly string? previous;
            public LogDirectoryRestorer(string? previous) => this.previous = previous;
            public void Dispose() => logDirectoryOverride.Value = previous;
        }

        public static void ClearLog() {
            if (File.Exists(LogFilePath))
                Utf8File.WriteAllText(LogFilePath, "");
        }

        public static void LogLine(string message) {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "]: " + message + "\n";
            try {
                //UserDataDirectory isn't guaranteed to exist yet on a fresh install - unlike the bundle's own
                //executable directory this used to write next to, matching SettingsService.Save's own guard.
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                Utf8File.AppendAllText(LogFilePath, line);
            } catch (Exception writeFailure) {
                Trace.WriteLine("Failed to write errorlog.txt: " + writeFailure);
                Trace.WriteLine(line.TrimEnd());
            }
        }

        public static void LogException(Exception ex, string? context = null) {
            if (string.IsNullOrEmpty(context))
                LogLine(ex.ToString());
            else
                LogLine(context + ": " + ex);
        }
    }
}
