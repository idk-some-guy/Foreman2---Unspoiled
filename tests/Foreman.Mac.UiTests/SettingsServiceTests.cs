using Foreman;
using Foreman.DataCaching;
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
            LockedRecipeEditorPosition = true,
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

            Assert.Contains("Failed to save settings.json", File.ReadAllText(ErrorLogging.LogFilePath));
        }

        [Fact]
        public void Save_WritesToLibraryApplicationSupportForemanSettingsJson() {
            string home = NewTempHome();
            var service = new SettingsService(home);

            service.Save(new AppSettings());

            string expectedPath = Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");
            Assert.True(File.Exists(expectedPath));
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2), exercised
        //via the isMacOsOverride seam since this box only ever runs the tests on macOS.
        [Fact]
        public void Save_OnLinux_NoXdgDataHome_WritesToDotLocalShareForemanSettingsJson() {
            string home = NewTempHome();
            var service = new SettingsService(home, isMacOsOverride: false);

            service.Save(new AppSettings());

            string expectedPath = Path.Combine(home, ".local", "share", "Foreman", "settings.json");
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

                string expectedPath = Path.Combine(xdgDataHome, "Foreman", "settings.json");
                Assert.True(File.Exists(expectedPath));
            } finally {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            }
        }
    }
}
