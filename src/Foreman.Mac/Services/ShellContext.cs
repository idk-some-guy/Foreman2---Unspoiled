using Foreman.DataCaching;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal sealed class ShellContext(GraphLibraryService library, AppSettings settings, DataCache cache, SettingsService? settingsService, PresetResolver? presetResolver) {
        public GraphLibraryService Library { get; } = library;
        public AppSettings Settings { get; } = settings;
        public DataCache Cache { get; set; } = cache;
        public SettingsService? SettingsService { get; } = settingsService;
        public PresetResolver? PresetResolver { get; } = presetResolver;
        public MainWindow? Window { get; set; }
        public bool OpenPending { get; set; }
        public bool CacheLoaded { get; set; } = true;
        public Task<bool>? LoadTask { get; set; }
        public string? LoadingPresetName { get; set; }
        public int LoadGeneration { get; set; }
        public bool Loading => LoadTask is { IsCompleted: false };
    }
}
