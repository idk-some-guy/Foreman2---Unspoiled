using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Models;
using System;
using System.Data.Common;
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

        private const int SettingsRowId = 1;

        private readonly string settingsFilePath;
        private readonly string dbPath;

        //Linux equivalent mirrors AppPaths.UserDataDirectory (docs/upstream-divergences.md):
        //$XDG_DATA_HOME/Foreman, falling back to ~/.local/share/Foreman when unset.
        public SettingsService(string? baseDirectoryOverride = null, bool? isMacOsOverride = null) {
            string home = baseDirectoryOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool isMacOs = isMacOsOverride ?? OperatingSystem.IsMacOS();
            string userDataRoot = isMacOs
                ? Path.Combine(home, "Library", "Application Support")
                : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdgDataHome
                    ? xdgDataHome
                    : Path.Combine(home, ".local", "share");
            string foremanDir = Path.Combine(userDataRoot, "Foreman");
            settingsFilePath = Path.Combine(foremanDir, "settings.json");
            dbPath = StoragePaths.DbPath(foremanDir);
        }

        //Legacy settings.json files can carry the upstream-verbatim LowPriorityPower default (1e5),
        //which overflowed the solver's objective to Infinity (docs/upstream-divergences.md). Heal that
        //exact value on load and clamp both solver-power fields to the same bounds their NumericUpDown
        //counterparts enforce in the Settings UI (SettingsViewModel.LoadFrom).
        private const decimal LegacyLowPriorityPower = 100000m;

        //Db row present -> that's authoritative. Otherwise fall back to the legacy json (one-time
        //import, persisted to db so the file is never consulted again) and finally to defaults.
        public AppSettings Load() {
            SettingsRow? row = TryFindRow();
            if (row is not null) {
                AppSettings fromDb = ToAppSettings(row);
                HealLoadedDefaults(fromDb);
                return fromDb;
            }

            bool hasLegacyJson = File.Exists(settingsFilePath);
            AppSettings settings = LoadFromJson();
            if (hasLegacyJson)
                SaveToDb(settings);
            return settings;
        }

        private AppSettings LoadFromJson() {
            if (!File.Exists(settingsFilePath))
                return new AppSettings();

            try {
                string json = File.ReadAllText(settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
                HealLoadedDefaults(settings);
                return settings;
            } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                ErrorLogging.LogLine("Failed to load settings.json, using defaults: " + ex);
                return new AppSettings();
            }
        }

        private static void HealLoadedDefaults(AppSettings settings) {
            settings.LowPriorityPower = settings.LowPriorityPower == LegacyLowPriorityPower
                ? 4m
                : Math.Clamp(settings.LowPriorityPower, 1m, 6m);
            settings.PullConsumerNodesPower = Math.Clamp(settings.PullConsumerNodesPower, 0m, 5m);
            settings.UiScale = Math.Clamp(settings.UiScale, 1.0, 2.0);
            settings.InspectorWidth = Math.Clamp(settings.InspectorWidth, 260, 380);
        }

        public void Save(AppSettings settings) {
            SaveCallCount++;
            SaveToDb(settings);
        }

        //Test-only seam: nothing outside Foreman.Mac.UiTests reads this.
        internal int SaveCallCount { get; private set; }

        private SettingsRow? TryFindRow() {
            try {
                using ForemanDbContext db = ForemanDbContext.Open(dbPath);
                return db.Settings.Find(SettingsRowId);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException) {
                ErrorLogging.LogLine("Failed to load settings db, falling back to json/defaults: " + ex);
                return null;
            }
        }

        private void SaveToDb(AppSettings settings) {
            try {
                using ForemanDbContext db = ForemanDbContext.Open(dbPath);
                SettingsRow row = ToRow(settings);
                SettingsRow? existing = db.Settings.Find(SettingsRowId);
                if (existing is null)
                    db.Settings.Add(row);
                else
                    db.Entry(existing).CurrentValues.SetValues(row);
                db.SaveChanges();
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException) {
                ErrorLogging.LogLine("Failed to save settings: " + ex);
            }
        }

        private static SettingsRow ToRow(AppSettings settings) => new() {
            Id = SettingsRowId,
            CurrentPresetName = settings.CurrentPresetName,
            DefaultModuleOption = (int)settings.DefaultModuleOption,
            MinorGridlines = settings.MinorGridlines,
            MajorGridlines = settings.MajorGridlines,
            AltGridlines = settings.AltGridlines,
            ShowHidden = settings.ShowHidden,
            IgnoreAssemblerStatus = settings.IgnoreAssemblerStatus,
            DynamicLineWidth = settings.DynamicLineWidth,
            LevelOfDetail = settings.LevelOfDetail,
            DefaultAssemblerOption = (int)settings.DefaultAssemblerOption,
            DefaultRateUnit = (int)settings.DefaultRateUnit,
            LastSaveFileLocation = settings.LastSaveFileLocation,
            ShowUnavailable = settings.ShowUnavailable,
            NodeCountForSimpleView = settings.NodeCountForSimpleView,
            UseRecipeBWfilters = settings.UseRecipeBWfilters,
            ShowWarningArrows = settings.ShowWarningArrows,
            ShowErrorArrows = settings.ShowErrorArrows,
            AbbreviateSciPacks = settings.AbbreviateSciPacks,
            RoundAssemblerCount = settings.RoundAssemblerCount,
            EnableExtraProductivityForNonMiners = settings.EnableExtraProductivityForNonMiners,
            ShowDisconnectedArrows = settings.ShowDisconnectedArrows,
            DefaultNodeDirection = (int)settings.DefaultNodeDirection,
            FlagOUSuppliedNodes = settings.FlagOUSuppliedNodes,
            ShowOUSuppliedArrows = settings.ShowOUSuppliedArrows,
            IconsOnlyView = settings.IconsOnlyView,
            IconsSize = settings.IconsSize,
            SimplePassthroughNodes = settings.SimplePassthroughNodes,
            ArrowsOnLinks = settings.ArrowsOnLinks,
            SmartNodeDirection = settings.SmartNodeDirection,
            FlagDarkMode = (int)settings.FlagDarkMode,
            UpgradeRequired = settings.UpgradeRequired,
            AnnotTextFontFamily = settings.AnnotTextFontFamily,
            AnnotTextFontSize = settings.AnnotTextFontSize,
            AnnotTextFontStyle = settings.AnnotTextFontStyle,
            AnnotTextColorARGB = settings.AnnotTextColorARGB,
            AnnotTextBackColorARGB = settings.AnnotTextBackColorARGB,
            AnnotTextAlign = settings.AnnotTextAlign,
            AnnotShapeType = settings.AnnotShapeType,
            AnnotShapeFillColorARGB = settings.AnnotShapeFillColorARGB,
            AnnotShapeBorderColorARGB = settings.AnnotShapeBorderColorARGB,
            AnnotShapeBorderWidth = settings.AnnotShapeBorderWidth,
            QualitySteps = settings.QualitySteps,
            LowPriorityPower = settings.LowPriorityPower,
            PullConsumerNodes = settings.PullConsumerNodes,
            PullConsumerNodesPower = settings.PullConsumerNodesPower,
            UiScale = settings.UiScale,
            InspectorPinned = settings.InspectorPinned,
            InspectorWidth = settings.InspectorWidth,
            BrowserWidth = settings.BrowserWidth,
            BrowserHeight = settings.BrowserHeight,
            BrowserItemsGroup = settings.BrowserItemsGroup,
            BrowserRecipesGroup = settings.BrowserRecipesGroup,
            SettingsEnabledObjectsWidth = settings.SettingsEnabledObjectsWidth,
            SettingsEnabledObjectsHeight = settings.SettingsEnabledObjectsHeight,
            SettingsAdvancedWidth = settings.SettingsAdvancedWidth,
            SettingsAdvancedHeight = settings.SettingsAdvancedHeight,
            DefaultLinkRouting = (int)settings.DefaultLinkRouting,
        };

        private static AppSettings ToAppSettings(SettingsRow row) => new() {
            CurrentPresetName = row.CurrentPresetName,
            DefaultModuleOption = (ModuleSelector.Style)row.DefaultModuleOption,
            MinorGridlines = row.MinorGridlines,
            MajorGridlines = row.MajorGridlines,
            AltGridlines = row.AltGridlines,
            ShowHidden = row.ShowHidden,
            IgnoreAssemblerStatus = row.IgnoreAssemblerStatus,
            DynamicLineWidth = row.DynamicLineWidth,
            LevelOfDetail = row.LevelOfDetail,
            DefaultAssemblerOption = (AssemblerSelector.Style)row.DefaultAssemblerOption,
            DefaultRateUnit = (ProductionGraph.RateUnit)row.DefaultRateUnit,
            LastSaveFileLocation = row.LastSaveFileLocation,
            ShowUnavailable = row.ShowUnavailable,
            NodeCountForSimpleView = row.NodeCountForSimpleView,
            UseRecipeBWfilters = row.UseRecipeBWfilters,
            ShowWarningArrows = row.ShowWarningArrows,
            ShowErrorArrows = row.ShowErrorArrows,
            AbbreviateSciPacks = row.AbbreviateSciPacks,
            RoundAssemblerCount = row.RoundAssemblerCount,
            EnableExtraProductivityForNonMiners = row.EnableExtraProductivityForNonMiners,
            ShowDisconnectedArrows = row.ShowDisconnectedArrows,
            DefaultNodeDirection = (NodeDirection)row.DefaultNodeDirection,
            FlagOUSuppliedNodes = row.FlagOUSuppliedNodes,
            ShowOUSuppliedArrows = row.ShowOUSuppliedArrows,
            IconsOnlyView = row.IconsOnlyView,
            IconsSize = row.IconsSize,
            SimplePassthroughNodes = row.SimplePassthroughNodes,
            ArrowsOnLinks = row.ArrowsOnLinks,
            SmartNodeDirection = row.SmartNodeDirection,
            FlagDarkMode = (ThemeMode)row.FlagDarkMode,
            UpgradeRequired = row.UpgradeRequired,
            AnnotTextFontFamily = row.AnnotTextFontFamily,
            AnnotTextFontSize = row.AnnotTextFontSize,
            AnnotTextFontStyle = row.AnnotTextFontStyle,
            AnnotTextColorARGB = row.AnnotTextColorARGB,
            AnnotTextBackColorARGB = row.AnnotTextBackColorARGB,
            AnnotTextAlign = row.AnnotTextAlign,
            AnnotShapeType = row.AnnotShapeType,
            AnnotShapeFillColorARGB = row.AnnotShapeFillColorARGB,
            AnnotShapeBorderColorARGB = row.AnnotShapeBorderColorARGB,
            AnnotShapeBorderWidth = row.AnnotShapeBorderWidth,
            QualitySteps = row.QualitySteps,
            LowPriorityPower = row.LowPriorityPower,
            PullConsumerNodes = row.PullConsumerNodes,
            PullConsumerNodesPower = row.PullConsumerNodesPower,
            UiScale = row.UiScale,
            InspectorPinned = row.InspectorPinned,
            InspectorWidth = row.InspectorWidth,
            BrowserWidth = row.BrowserWidth,
            BrowserHeight = row.BrowserHeight,
            BrowserItemsGroup = row.BrowserItemsGroup,
            BrowserRecipesGroup = row.BrowserRecipesGroup,
            SettingsEnabledObjectsWidth = row.SettingsEnabledObjectsWidth,
            SettingsEnabledObjectsHeight = row.SettingsEnabledObjectsHeight,
            SettingsAdvancedWidth = row.SettingsAdvancedWidth,
            SettingsAdvancedHeight = row.SettingsAdvancedHeight,
            DefaultLinkRouting = (LinkRouting)row.DefaultLinkRouting,
        };
    }
}
