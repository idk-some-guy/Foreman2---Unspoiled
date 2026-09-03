using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class SettingsServiceTests {
        private static readonly JsonSerializerOptions ComparisonOptions = new() {
            Converters = { new JsonStringEnumConverter() },
        };

        private static string NewTempHome() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(home);
            return home;
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static AppSettings BuildNonDefaultSettings() => new() {
            CurrentPresetName = "Factorio 2.0 Space Age",
            DefaultModuleOption = ModuleSelector.Style.Productivity,
            MinorGridlines = 1,
            MajorGridlines = 2,
            AltGridlines = true,
            ShowHidden = true,
            IgnoreAssemblerStatus = true,
            DynamicLineWidth = true,
            RecipeNameOnlyFilter = true,
            LevelOfDetail = 2,
            DefaultAssemblerOption = AssemblerSelector.Style.Best,
            DefaultRateUnit = ProductionGraph.RateUnit.Per1Min,
            LastSaveFileLocation = "/Users/test/Saved Graphs/factory.fjson",
            ShowRecipeToolTip = false,
            ShowUnavailable = true,
            NodeCountForSimpleView = 500,
            UseRecipeBWfilters = false,
            ShowWarningArrows = false,
            ShowErrorArrows = false,
            AbbreviateSciPacks = false,
            RoundAssemblerCount = true,
            EnableExtraProductivityForNonMiners = true,
            ShowDisconnectedArrows = true,
            DefaultNodeDirection = NodeDirection.Down,
            FlagOUSuppliedNodes = true,
            ShowOUSuppliedArrows = true,
            IconsOnlyView = true,
            IconsSize = 32,
            SimplePassthroughNodes = true,
            ArrowsOnLinks = true,
            SmartNodeDirection = false,
            FlagDarkMode = ThemeMode.Dark,
            UpgradeRequired = false,
            AnnotTextFontFamily = "Helvetica",
            AnnotTextFontSize = "18",
            AnnotTextFontStyle = 0,
            AnnotTextColorARGB = -1,
            AnnotTextBackColorARGB = 16777215,
            AnnotTextAlign = 2,
            AnnotShapeType = 1,
            AnnotShapeFillColorARGB = 255,
            AnnotShapeBorderColorARGB = -256,
            AnnotShapeBorderWidth = 4,
            QualitySteps = 5,
            LowPriorityPower = 2.5m,
            PullConsumerNodes = true,
            PullConsumerNodesPower = 3.5m,
        };

        [Fact]
        public void RoundTrip_SaveThenLoad_RestoresEveryProperty() {
            var service = new SettingsService(NewTempHome());
            var expected = BuildNonDefaultSettings();

            service.Save(expected);
            var actual = service.Load();

            Assert.Equal(JsonSerializer.Serialize(expected, ComparisonOptions), JsonSerializer.Serialize(actual, ComparisonOptions));
        }

        [Fact]
        public void Load_MissingFile_ReturnsUpstreamDefaults() {
            var service = new SettingsService(NewTempHome());

            var settings = service.Load();

            Assert.Equal("", settings.CurrentPresetName);
            Assert.Equal(0, settings.LevelOfDetail);
            Assert.Equal(ThemeMode.System, settings.FlagDarkMode);
            Assert.Equal("Segoe UI", settings.AnnotTextFontFamily);
            Assert.Equal(1, settings.QualitySteps);
            Assert.Equal(4m, settings.LowPriorityPower);
            Assert.False(settings.PullConsumerNodes);
            Assert.Equal(1m, settings.PullConsumerNodesPower);
        }

        [Theory]
        [InlineData("false", ThemeMode.System)]
        [InlineData("true", ThemeMode.Dark)]
        public void Load_LegacyBoolFlagDarkMode_MigratesToSystemOrDark(string legacyValue, ThemeMode expected) {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ \"FlagDarkMode\": " + legacyValue + " }");
            var service = new SettingsService(home);

            var settings = service.Load();

            Assert.Equal(expected, settings.FlagDarkMode);
        }

        [Fact]
        public void Load_LegacyLowPriorityPower_HealsToFour() {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ \"LowPriorityPower\": 100000 }");
            var service = new SettingsService(home);

            var settings = service.Load();

            Assert.Equal(4m, settings.LowPriorityPower);
        }

        [Fact]
        public void Load_InRangeLowPriorityPower_PassesThroughUntouched() {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ \"LowPriorityPower\": 5 }");
            var service = new SettingsService(home);

            var settings = service.Load();

            Assert.Equal(5m, settings.LowPriorityPower);
        }

        [Fact]
        public void Load_OutOfRangeLowPriorityPower_ClampsToSettingsUiMaximum() {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ \"LowPriorityPower\": 9 }");
            var service = new SettingsService(home);

            var settings = service.Load();

            Assert.Equal(6m, settings.LowPriorityPower);
        }

        [Fact]
        public void Load_OutOfRangePullConsumerNodesPower_ClampsToSettingsUiMaximum() {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ \"PullConsumerNodesPower\": 9 }");
            var service = new SettingsService(home);

            var settings = service.Load();

            Assert.Equal(5m, settings.PullConsumerNodesPower);
        }

        [Fact]
        public void Load_CorruptFile_ReturnsDefaultsWithoutThrowing() {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ not valid json ][");
            var service = new SettingsService(home);
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempDir());
            ErrorLogging.ClearLog();

            var settings = service.Load();

            Assert.Equal(JsonSerializer.Serialize(new AppSettings(), ComparisonOptions), JsonSerializer.Serialize(settings, ComparisonOptions));
            Assert.Contains("Failed to load settings.json", File.ReadAllText(ErrorLogging.LogFilePath));
        }

        [Fact]
        public void Save_DestinationDirectoryUnavailable_LogsInsteadOfThrowing() {
            string home = NewTempHome();
            string libraryDir = Path.Combine(home, "Library");
            Directory.CreateDirectory(libraryDir);
            File.WriteAllText(Path.Combine(libraryDir, "Application Support"), "blocks the Foreman subdirectory");
            var service = new SettingsService(home);
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempDir());
            ErrorLogging.ClearLog();

            service.Save(new AppSettings());

            Assert.Contains("Failed to save settings", File.ReadAllText(ErrorLogging.LogFilePath));
        }

        //Settings now persist to foreman.db rather than settings.json (phase 9 Task 2); these three
        //cases keep verifying Save lands under the right platform root, against the new medium.
        [Fact]
        public void Save_WritesToLibraryApplicationSupportForemanDb() {
            string home = NewTempHome();
            var service = new SettingsService(home);

            service.Save(new AppSettings());

            string expectedPath = Path.Combine(home, "Library", "Application Support", "Foreman", "foreman.db");
            Assert.True(File.Exists(expectedPath));
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2), exercised
        //via the isMacOsOverride seam since this box only ever runs the tests on macOS.
        [Fact]
        public void Save_OnLinux_NoXdgDataHome_WritesToDotLocalShareForemanDb() {
            string home = NewTempHome();
            var service = new SettingsService(home, isMacOsOverride: false);

            service.Save(new AppSettings());

            string expectedPath = Path.Combine(home, ".local", "share", "Foreman", "foreman.db");
            Assert.True(File.Exists(expectedPath));
        }

        [Fact]
        public void Save_OnLinux_XdgDataHomeSet_WritesUnderIt() {
            string home = NewTempHome();
            string xdgDataHome = NewTempDir();
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", xdgDataHome);
            try {
                var service = new SettingsService(home, isMacOsOverride: false);

                service.Save(new AppSettings());

                string expectedPath = Path.Combine(xdgDataHome, "Foreman", "foreman.db");
                Assert.True(File.Exists(expectedPath));
            } finally {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            }
        }

        [Fact]
        public void FreshDir_SaveThenReopen_PersistsToDbAndNeverWritesJson() {
            string home = NewTempHome();
            var nonDefault = BuildNonDefaultSettings();

            var firstRun = new SettingsService(home);
            var defaults = firstRun.Load();
            firstRun.Save(nonDefault);
            var reopened = new SettingsService(home).Load();

            Assert.Equal(JsonSerializer.Serialize(new AppSettings(), ComparisonOptions), JsonSerializer.Serialize(defaults, ComparisonOptions));
            Assert.Equal(JsonSerializer.Serialize(nonDefault, ComparisonOptions), JsonSerializer.Serialize(reopened, ComparisonOptions));
            string settingsJsonPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Assert.False(File.Exists(settingsJsonPath));
        }

        [Fact]
        public void LegacyJsonOnly_ImportsConvertsAndHealsThenSurvivesJsonDeletion() {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ \"FlagDarkMode\": true, \"LowPriorityPower\": 100000 }");
            var service = new SettingsService(home);

            var imported = service.Load();

            Assert.Equal(ThemeMode.Dark, imported.FlagDarkMode);
            Assert.Equal(4m, imported.LowPriorityPower);

            File.Delete(settingsPath);
            var afterRestart = new SettingsService(home).Load();

            Assert.Equal(ThemeMode.Dark, afterRestart.FlagDarkMode);
            Assert.Equal(4m, afterRestart.LowPriorityPower);
        }

        //Final-review C1: a garbage foreman.db (disk corruption, a killed write) must not brick every
        //future launch - the db is a rebuildable cache, so a bad file gets quarantined and Load falls
        //back to the legacy json/defaults path instead of throwing.
        [Fact]
        public void Load_CorruptDbFile_QuarantinesFileAndReturnsDefaults() {
            string home = NewTempHome();
            string foremanDir = Path.Combine(home, "Library", "Application Support", "Foreman");
            Directory.CreateDirectory(foremanDir);
            string dbPath = Path.Combine(foremanDir, "foreman.db");
            File.WriteAllBytes(dbPath, [1, 2, 3, 4, 5]);
            var service = new SettingsService(home);

            var settings = service.Load();

            Assert.Equal(JsonSerializer.Serialize(new AppSettings(), ComparisonOptions), JsonSerializer.Serialize(settings, ComparisonOptions));
            Assert.True(File.Exists(dbPath + ".corrupt"));
        }

        [Fact]
        public void BothJsonAndDbPresent_DbWins() {
            string home = NewTempHome();
            string settingsPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ \"CurrentPresetName\": \"From Json\" }");
            string dbPath = Path.Combine(home, "Library", "Application Support", "Foreman", "foreman.db");
            using (var db = ForemanDbContext.Open(dbPath)) {
                db.Settings.Add(new SettingsRow { Id = 1, CurrentPresetName = "From Db", AnnotTextFontFamily = "Segoe UI", AnnotTextFontSize = "14" });
                db.SaveChanges();
            }

            var loaded = new SettingsService(home).Load();

            Assert.Equal("From Db", loaded.CurrentPresetName);
        }
    }
}
