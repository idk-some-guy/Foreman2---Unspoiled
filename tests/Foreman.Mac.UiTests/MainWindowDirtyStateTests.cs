using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.UiTests {
    //Task 7 (contract #toolbar): MainWindowViewModel.IsDirty is a cheap UI-hint flag, not the authoritative
    //dirty gate - TestGraphSavedStatusAsync's content-hash compare (MainWindowSaveLoadTests.cs) stays the
    //one that gates New/Load/Close, untouched by this file. IsDirty only drives the Save button's tint.
    public class MainWindowDirtyStateTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        private static string TempSavePath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".fjson");

        //---- the flag itself ------------------------------------------------------------------------------

        [AvaloniaFact]
        public void IsDirty_FreshWindow_IsFalse() {
            var window = new MainWindow();
            window.Show();

            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public async Task GraphMutation_NodeAdded_SetsIsDirty() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);

            Assert.True(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public async Task SaveGraphAsAsync_Success_ClearsIsDirty() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            window.ViewModel.IsDirty = true;
            string path = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);

            try {
                await window.SaveGraphAsAsync();

                Assert.False(window.ViewModel.IsDirty);
            } finally {
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task NewGraphAsync_ClearsIsDirty() {
            var window = new MainWindow();
            window.Show();
            window.ViewModel.IsDirty = true;

            await window.NewGraphAsync();

            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void ApplyLoadedGraphUiState_ClearsIsDirty() {
            var window = new MainWindow();
            window.Show();
            window.ViewModel.IsDirty = true;

            window.ApplyLoadedGraphUiState("/tmp/does-not-need-to-exist.fjson");

            Assert.False(window.ViewModel.IsDirty);
        }

        //---- Save button's dirty tint (contract #toolbar's Save .on state) --------------------------------

        private static Button SaveButton(MainWindow window) =>
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveButton");

        [AvaloniaTheory]
        [InlineData(ThemeMode.Dark, "#290A84FF")]
        [InlineData(ThemeMode.Light, "#1F007AFF")]
        public void SaveButton_Dirty_BackgroundUsesAccentSoftToken(ThemeMode mode, string expectedHex) {
            ((App)Application.Current!).ApplyTheme(mode);
            var window = new MainWindow();
            window.Show();

            window.ViewModel.IsDirty = true;
            Dispatcher.UIThread.RunJobs();

            var background = Assert.IsType<ImmutableSolidColorBrush>(SaveButton(window).Background);
            Assert.Equal(AvColor.Parse(expectedHex), background.Color);
        }

        [AvaloniaFact]
        public void SaveButton_Clean_DoesNotUseAccentSoftBackground() {
            ((App)Application.Current!).ApplyTheme(ThemeMode.Dark);
            var window = new MainWindow();
            window.Show();

            Dispatcher.UIThread.RunJobs();

            var accentSoft = AvColor.Parse("#290A84FF");
            bool isTinted = SaveButton(window).Background is ImmutableSolidColorBrush { } brush && brush.Color == accentSoft;
            Assert.False(isTinted);
        }

        //M-5 (final fix wave): the plain :pointerover style sets ContentPresenter.Background directly,
        //which used to outrank .on's Background setter on the Button itself (the template's ContentPresenter
        //only inherits that one via TemplateBinding) - hovering a dirty Save button swapped the accent tint
        //for the generic hover gray. Reads the ContentPresenter's own resolved Background, not the Button's,
        //since that's the property the bug actually lived on.
        [AvaloniaFact]
        public void SaveButton_DirtyAndPointerOver_ContentPresenterKeepsAccentBackground() {
            ((App)Application.Current!).ApplyTheme(ThemeMode.Dark);
            var window = new MainWindow();
            window.Show();
            window.ViewModel.IsDirty = true;
            Dispatcher.UIThread.RunJobs();

            Button save = SaveButton(window);
            Point center = save.TranslatePoint(new Point(save.Bounds.Width / 2, save.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            Dispatcher.UIThread.RunJobs();

            Assert.True(save.IsPointerOver);
            var presenter = save.GetVisualDescendants().OfType<ContentPresenter>().Single();
            var background = Assert.IsType<ImmutableSolidColorBrush>(presenter.Background);
            Assert.Equal(AvColor.Parse("#290A84FF"), background.Color);
        }
    }
}
