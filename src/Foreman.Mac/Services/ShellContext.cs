using Foreman.DataCaching;

namespace Foreman.Mac.Services {
    internal sealed class ShellContext(GraphLibraryService library, AppSettings settings, DataCache cache, SettingsService? settingsService, PresetResolver? presetResolver) {
        public GraphLibraryService Library { get; } = library;
        public AppSettings Settings { get; } = settings;
        public DataCache Cache { get; set; } = cache;
        public SettingsService? SettingsService { get; } = settingsService;
        public PresetResolver? PresetResolver { get; } = presetResolver;
        public MainWindow? Window { get; set; }
    }
}
