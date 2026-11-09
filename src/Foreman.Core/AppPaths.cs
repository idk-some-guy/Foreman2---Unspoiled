using System;
using System.IO;

namespace Foreman {
    public static class AppPaths {
        private static bool? isMacOsOverride;

        public static string ExecutableDirectory => AppContext.BaseDirectory;

        private static bool IsMacOs => isMacOsOverride ?? OperatingSystem.IsMacOS();

        //Test-only seam: forces the platform branch below without depending on the host OS (phase 8 Task 2 -
        //this box only ever runs the tests on macOS, so the Linux branch needs its own way in).
        internal static void SetIsMacOsOverride(bool? isMacOs) => isMacOsOverride = isMacOs;

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        //File-location policy (docs/upstream-divergences.md): saved graphs live under the user's Documents
        //folder rather than next to the executable - a signed .app bundle's own directory isn't writable.
        //Linux honors XDG_DOCUMENTS_DIR (xdg-user-dirs) when set, falling back to ~/Documents otherwise.
        private static string DocumentsRoot => IsMacOs
            ? Path.Combine(Home, "Documents")
            : Environment.GetEnvironmentVariable("XDG_DOCUMENTS_DIR") is { Length: > 0 } xdgDocuments
                ? xdgDocuments
                : Path.Combine(Home, "Documents");

        public static string SavedGraphsDirectory => Path.Combine(DocumentsRoot, "Foreman", "Saved Graphs");

        //Same policy, applied to PNG exports (docs/upstream-divergences.md).
        public static string ExportedGraphsDirectory => Path.Combine(DocumentsRoot, "Foreman", "Exported Graphs");

        //Same policy, applied to imported presets (docs/upstream-divergences.md): a signed .app bundle's own
        //Presets folder is read-only, so an import writes here instead - matching SettingsService's own
        //~/Library/Application Support/Foreman (macOS) / $XDG_DATA_HOME/Foreman (Linux) convention for
        //settings.json. Linux falls back to ~/.local/share when XDG_DATA_HOME is unset, per the XDG Base
        //Directory spec.
        private static string UserDataRoot => IsMacOs
            ? Path.Combine(Home, "Library", "Application Support")
            : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdgDataHome
                ? xdgDataHome
                : Path.Combine(Home, ".local", "share");

        public static string UserDataDirectory => Path.Combine(UserDataRoot, "Foreman");

        public static string UserPresetsDirectory => Path.Combine(UserDataDirectory, "Presets");

        //Scratch space for PresetImporter's throwaway Factorio save (docs/upstream-divergences.md): pinned
        //as the child process's own working directory so "--create temp-save.zip" resolves here instead of
        //wherever the OS happens to hand Foreman's own process. Created on demand by the caller.
        public static string ScratchDirectory => Path.Combine(UserDataDirectory, "Scratch");
    }
}
