using Foreman.DataCaching;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foreman.Mac.Services {
    //Reads a legacy bool FlagDarkMode (false/true) as System/Dark; writes the tri-state enum as its name.
    internal sealed class ThemeModeJsonConverter : JsonConverter<ThemeMode> {
        public override ThemeMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.True)
                return ThemeMode.Dark;
            if (reader.TokenType == JsonTokenType.False)
                return ThemeMode.System;
            return Enum.TryParse(reader.GetString(), ignoreCase: true, out ThemeMode mode) ? mode : ThemeMode.System;
        }

        public override void Write(Utf8JsonWriter writer, ThemeMode value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    public sealed class SettingsService {
        private static readonly JsonSerializerOptions SerializerOptions = new() {
            WriteIndented = true,
            Converters = { new ThemeModeJsonConverter(), new JsonStringEnumConverter() },
        };

        private readonly string settingsFilePath;

        //Linux equivalent mirrors AppPaths.UserDataDirectory (docs/upstream-divergences.md, phase 8 Task 2):
        //$XDG_DATA_HOME/Foreman, falling back to ~/.local/share/Foreman when unset.
        public SettingsService(string? baseDirectoryOverride = null, bool? isMacOsOverride = null) {
            string home = baseDirectoryOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool isMacOs = isMacOsOverride ?? OperatingSystem.IsMacOS();
            string userDataRoot = isMacOs
                ? Path.Combine(home, "Library", "Application Support")
                : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdgDataHome
                    ? xdgDataHome
                    : Path.Combine(home, ".local", "share");
            settingsFilePath = Path.Combine(userDataRoot, "Foreman", "settings.json");
        }

        public AppSettings Load() {
            if (!File.Exists(settingsFilePath))
                return new AppSettings();

            try {
                string json = File.ReadAllText(settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                ErrorLogging.LogLine("Failed to load settings.json, using defaults: " + ex);
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
                File.WriteAllText(settingsFilePath, JsonSerializer.Serialize(settings, SerializerOptions));
            } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                ErrorLogging.LogLine("Failed to save settings.json: " + ex);
            }
        }
    }
}
