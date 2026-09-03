using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Views;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class DataLoadWindowTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [AvaloniaFact]
        public async Task Load_RealPreset_PopulatesResultAndReportsProgress() {
            var window = new DataLoadWindow(new Preset(VanillaPresetName, true, true));

            await window.LoadTask;

            Assert.NotNull(window.Result);
            Assert.Equal(100, window.Progress);
            Assert.False(string.IsNullOrEmpty(window.StatusText));
        }

        [AvaloniaFact]
        public async Task Load_MissingPreset_ResultIsNullWithNoEscapedException() {
            var window = new DataLoadWindow(new Preset("Nonexistent Preset", true, true));

            await window.LoadTask;

            Assert.Null(window.Result);
        }

        [AvaloniaFact]
        public async Task Load_FilterRecipesFlag_ReachesDataCache() {
            var filtered = new DataLoadWindow(new Preset(VanillaPresetName, true, true), filterRecipes: true);
            await filtered.LoadTask;

            var unfiltered = new DataLoadWindow(new Preset(VanillaPresetName, true, true), filterRecipes: false);
            await unfiltered.LoadTask;

            int filteredCount = filtered.Result!.AvailableRecipes.Count();
            int unfilteredCount = unfiltered.Result!.AvailableRecipes.Count();

            Assert.True(unfilteredCount > filteredCount);
        }

        [AvaloniaFact]
        public async Task Load_ManyConcurrentRealPresetLoads_AllReachProgress100() {
            var windows = new List<DataLoadWindow>();
            for (int i = 0; i < 8; i++)
                windows.Add(new DataLoadWindow(new Preset(VanillaPresetName, true, true)));

            await Task.WhenAll(windows.Select(w => w.LoadTask));

            Assert.All(windows, w => Assert.Equal(100, w.Progress));
        }

        [AvaloniaFact]
        public void Close_MidLoad_ShowsCloseWarningInsteadOfClosingSilently() {
            var window = new DataLoadWindow(new Preset(VanillaPresetName, true, true));
            window.Show();

            window.Close();

            Assert.True(window.CloseWarningDialogShown);
            Assert.True(window.IsVisible);
        }

        [AvaloniaFact]
        public void Close_MidLoad_WarningDescribesLoadContinuingInBackground() {
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempDir());
            ErrorLogging.ClearLog();
            var window = new DataLoadWindow(new Preset(VanillaPresetName, true, true));
            window.Show();

            window.Close();

            string log = File.ReadAllText(ErrorLogging.LogFilePath);
            Assert.Contains("continues", log);
            Assert.DoesNotContain("incomplete", log);
        }
    }
}
