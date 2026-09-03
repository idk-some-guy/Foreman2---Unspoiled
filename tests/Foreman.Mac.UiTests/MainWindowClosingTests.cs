using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Covers the final-review C2 finding: MainWindow.Closing only ever saved settings (ShellBootstrapper's
    //own subscription) - an unsaved graph was discarded silently on window close, unlike New/Load, which
    //both already gate through TestGraphSavedStatusAsync. Wires the same cancel-then-reclose pattern
    //DataLoadWindow.OnClosing already uses (io-reference.md's own precedent): the first Close cancels and
    //awaits the gate; a confirmed second Close goes through.
    public class MainWindowClosingTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static string TempSavePath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".fjson");

        private static async Task<(MainWindow window, string path)> NewWindowWithModifiedTitledGraphAsync() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            string path = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);
            await window.SaveGraphAsAsync();

            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);
            return (window, path);
        }

        [AvaloniaFact]
        public async Task Close_ModifiedTitledGraph_PromptsForSaveBeforeContinuing() {
            (MainWindow window, string path) = await NewWindowWithModifiedTitledGraphAsync();
            try {
                bool promptShown = false;
                window.SaveBeforeContinuingChoiceStub = () => { promptShown = true; return Task.FromResult(ConfirmChoice.Cancel); };

                window.Close();

                Assert.True(promptShown);
            } finally {
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task Close_ModifiedTitledGraph_CancelChoice_KeepsWindowOpen() {
            (MainWindow window, string path) = await NewWindowWithModifiedTitledGraphAsync();
            try {
                window.SaveBeforeContinuingChoiceStub = () => Task.FromResult(ConfirmChoice.Cancel);

                window.Close();

                Assert.True(window.IsVisible);
            } finally {
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task Close_ModifiedTitledGraph_NoChoice_ClosesWithoutSaving() {
            (MainWindow window, string path) = await NewWindowWithModifiedTitledGraphAsync();
            try {
                string baselineBeforeClose = window.SaveFileBaselineJson!;
                window.SaveBeforeContinuingChoiceStub = () => Task.FromResult(ConfirmChoice.No);

                window.Close();

                Assert.False(window.IsVisible);
                Assert.Equal(baselineBeforeClose, File.ReadAllText(path)); //"No" must not have re-saved over the path
            } finally {
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task Close_ModifiedTitledGraph_YesChoice_SavesThenCloses() {
            (MainWindow window, string path) = await NewWindowWithModifiedTitledGraphAsync();
            try {
                string baselineBeforeClose = window.SaveFileBaselineJson!;
                window.SaveBeforeContinuingChoiceStub = () => Task.FromResult(ConfirmChoice.Yes);

                window.Close();

                Assert.False(window.IsVisible);
                Assert.NotEqual(baselineBeforeClose, File.ReadAllText(path)); //"Yes" re-saved the node added above
            } finally {
                File.Delete(path);
            }
        }
    }
}
