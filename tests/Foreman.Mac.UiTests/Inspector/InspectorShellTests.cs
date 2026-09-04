using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    public class InspectorShellTests : IDisposable {
        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public async Task SelectingOneNode_ShowsSidebarAt300WithIdentity() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            Assert.False(window.Inspector.IsVisible);

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Inspector.IsVisible);
            Assert.Equal(300, window.Inspector.Bounds.Width, 1);
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;
            Assert.Equal(recipe.BaseRecipe.FriendlyName, window.Inspector.FindControl<TextBlock>("IdentityName")!.Text);
        }

        [AvaloniaFact]
        public async Task ClearingSelection_HidesSidebar_CanvasReclaimsWidth() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            double fullWidth = window.GraphCanvas.Bounds.Width;

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fullWidth - 300, window.GraphCanvas.Bounds.Width, 1);

            window.GraphCanvas.Viewer.SetSelection([]);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.Inspector.IsVisible);
            Assert.Equal(fullWidth, window.GraphCanvas.Bounds.Width, 1);
        }

        [AvaloniaFact]
        public async Task Pinned_EmptySelection_ShowsNoSelectionText() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.ViewModel.Inspector.IsPinned = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Inspector.IsVisible);
            Assert.Equal("No selection", window.Inspector.FindControl<TextBlock>("EmptyState")!.Text);
        }

        [AvaloniaFact]
        public async Task SelectedRecipeNode_SubtitleShowsAssemblerAndQuality() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            var recipe = (Foreman.Graph.IRecipeNodeViewModel)window.GraphCanvas.Viewer.NodeElements.First().ViewModel;

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            string expected = $"{recipe.SelectedAssembler.Assembler.FriendlyName} · {recipe.SelectedAssembler.Quality.FriendlyName}";
            Assert.Equal(expected, window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text);
        }

        [AvaloniaFact]
        public async Task SelectedSupplierNode_ShowsItemNameAndSupplierSubtitle() {
            DataCache cache = await InspectorTestSupport.GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            Dispatcher.UIThread.RunJobs();

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(cache.Items["iron-plate"].FriendlyName, window.Inspector.FindControl<TextBlock>("IdentityName")!.Text);
            Assert.Equal("Supplier node", window.Inspector.FindControl<TextBlock>("IdentitySubtitle")!.Text);
        }

        [AvaloniaFact]
        public void InspectorTokens_ResolveInBothThemes() {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark }) {
                Application.Current!.RequestedThemeVariant = variant;
                foreach (string key in new[] {
                    "Foreman.Size.InspectorWidth", "Foreman.Color.InspectorBackground",
                    "Foreman.Type.InspectorTitle.FontSize", "Foreman.Type.InspectorTitle.FontWeight",
                    "Foreman.Type.InspectorSubtitle.FontSize",
                })
                    Assert.True(Application.Current.TryGetResource(key, variant, out _), key);
            }
        }

        [AvaloniaFact]
        public async Task Scale1_5_InspectorTransformedWidthIs450() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();

            WindowScaler.SetScale(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(450, TransformedWidth(window.Inspector, window), 1);
        }

        private static double TransformedWidth(Control control, Window window) {
            Point topLeft = control.TranslatePoint(new Point(0, 0), window)!.Value;
            Point topRight = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value;
            return topRight.X - topLeft.X;
        }

        [AvaloniaFact]
        public async Task LoadingAGraphDocument_ClearsStaleInspectorSelection() {
            DataCache cache = await InspectorTestSupport.GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.Settings = new AppSettings { CurrentPresetName = cache.PresetName! };
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();

            var pair = new RecipeQualityPair(cache.Recipes["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateRecipeNode(pair, new System.Drawing.Point(0, 0));
            Dispatcher.UIThread.RunJobs();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElements.First()]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Inspector.IsVisible);

            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".fjson");
            try {
                await window.SaveGraphAsync(path);
                string json = await File.ReadAllTextAsync(path);

                await window.LoadGraphJsonAsync(json, path);
                Dispatcher.UIThread.RunJobs();

                Assert.False(window.Inspector.IsVisible);
                Assert.Null(window.ViewModel.Inspector.Node);
            } finally {
                File.Delete(path);
            }
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
