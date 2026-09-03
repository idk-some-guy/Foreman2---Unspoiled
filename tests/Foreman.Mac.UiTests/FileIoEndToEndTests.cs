using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Phase 6's own gate (docs/superpowers/plans/2026-09-02-phase6-file-preset-io.md, Task 9): Tasks 1-8 each
    //land their own window/pipeline clean in isolation - this covers the seams BETWEEN them instead: a save
    //feeding a dirty-prompted load, an import landing on top of a load, an export rendering a loaded graph,
    //a preset import becoming visible to the resolver, and a cross-preset load picking the right path.
    [UnsupportedOSPlatform("windows")]
    public class FileIoEndToEndTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string SpaceAgePresetName = "Factorio 2.0 Space Age";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedVanillaCache;
        private static DataCache? sharedSpaceAgeCache;

        private static async Task<DataCache> GetSharedVanillaCacheAsync() {
            if (sharedVanillaCache is not null)
                return sharedVanillaCache;
            await CacheGate.WaitAsync();
            try {
                sharedVanillaCache ??= await LoadCacheAsync(VanillaPresetName);
            } finally {
                CacheGate.Release();
            }
            return sharedVanillaCache;
        }

        private static async Task<DataCache> GetSharedSpaceAgeCacheAsync() {
            if (sharedSpaceAgeCache is not null)
                return sharedSpaceAgeCache;
            await CacheGate.WaitAsync();
            try {
                sharedSpaceAgeCache ??= await LoadCacheAsync(SpaceAgePresetName);
            } finally {
                CacheGate.Release();
            }
            return sharedSpaceAgeCache;
        }

        private static async Task<DataCache> LoadCacheAsync(string presetName) {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(presetName, true, true), new Progress<KeyValuePair<int, string>>());
            return cache;
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string TempSavePath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".fjson");
        private static string TempPngPath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

        //The real bundled Presets directory (Vanilla/SpaceAge) always stays readable here; only the user-
        //writable half is ever pointed at a temp dir, so a preset switch can genuinely happen without any
        //test ever touching the real ~/Library Presets folder.
        private static MainWindow NewReadyWindow(DataCache cache, AppSettings settings, string tempHome) {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.Viewport.SetSize(800, 800);
            window.DataCache = cache;
            window.Settings = settings;
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: NewTempDir());
            window.SettingsService = new SettingsService(tempHome);
            window.ApplyLoadedSettings();
            return window;
        }

        //================================================================================================
        // Seam 1: save -> modify -> dirty-prompt on load -> decline -> the earlier file wins.
        //================================================================================================

        [AvaloniaFact]
        public async Task SaveModifyLoad_DirtyPromptDeclineSave_RestoresEarlierNodesAnnotationsViewportAndEnabledFlags() {
            var cache = new DataCache(filterRecipes: true); //dedicated cache: this test flips a recipe's Enabled flag
            await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            IRecipe ironPlateRecipe = cache.Recipes["iron-plate"];
            Assert.True(ironPlateRecipe.Enabled);
            ironPlateRecipe.Enabled = false; //captured into the save below

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["copper-plate"], cache.DefaultQuality!), new Point(30, 40));
            window.GraphCanvas.Viewer.AddAnnotationElement(new TextAnnotationElement(new Point(60, 20)) { Text = "Round trip" });
            window.GraphCanvas.Viewport.ViewOffset = new Point(15, 25);
            window.GraphCanvas.Viewport.ViewScale = 1.75f;

            string path = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);
            try {
                await window.SaveGraphAsAsync();
                Assert.Equal("Foreman 2 (" + VanillaPresetName + ") - " + path, window.Title);
                string savedJson = await File.ReadAllTextAsync(path);

                //Modify the graph after the save, so the load below finds it dirty.
                window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), new Point(300, 300));
                ironPlateRecipe.Enabled = true;

                bool promptShown = false;
                window.SaveBeforeContinuingChoiceStub = () => { promptShown = true; return Task.FromResult(ConfirmChoice.No); };
                Assert.True(await window.TestGraphSavedStatusAsync());
                Assert.True(promptShown);

                await window.LoadGraphJsonAsync(savedJson, path);

                Assert.Equal("Foreman 2 (" + VanillaPresetName + ") - " + path, window.Title);
                Assert.Equal(path, window.SaveFilePath);

                INodeViewModel node = Assert.Single(window.GraphCanvas.Viewer.Session.View.Nodes);
                var supplier = Assert.IsAssignableFrom<ISupplierNodeViewModel>(node);
                Assert.Equal(cache.Items["copper-plate"], supplier.SuppliedItem.Item);
                Assert.Equal(new Point(30, 40), node.Location);

                var annotation = Assert.IsType<TextAnnotationElement>(Assert.Single(window.GraphCanvas.Viewer.Annotations));
                Assert.Equal("Round trip", annotation.Text);
                Assert.Equal(new Point(60, 20), annotation.Location);

                Assert.Equal(new Point(15, 25), window.GraphCanvas.Viewport.ViewOffset);
                Assert.Equal(1.75f, window.GraphCanvas.Viewport.ViewScale);

                Assert.False(ironPlateRecipe.Enabled); //restored to the save's own captured state, not the post-save flip
            } finally {
                File.Delete(path);
            }
        }

        //================================================================================================
        // Seam 2: load doc B, import doc A on top -> merged graph, selection lands on the imported nodes.
        //================================================================================================

        [AvaloniaFact]
        public async Task LoadThenImport_MergesTheImportedDocumentAndSelectsOnlyItsNodes() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), new Point(1000, 1000));
            string pathA = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(pathA);

            string pathB = TempSavePath();
            try {
                await window.SaveGraphAsAsync();
                string jsonA = await File.ReadAllTextAsync(pathA);

                await window.NewGraphAsync();
                window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["copper-plate"], cache.DefaultQuality!), new Point(500, 500));
                window.SaveFilePathStub = () => Task.FromResult<string?>(pathB);
                await window.SaveGraphAsAsync();
                string jsonB = await File.ReadAllTextAsync(pathB);

                await window.LoadGraphJsonAsync(jsonB, pathB); //doc B genuinely loaded first
                Assert.Single(window.GraphCanvas.Viewer.NodeElements);

                await window.ImportGraphJsonAsync(jsonA, pathA); //doc A merged on top

                Assert.Equal(2, window.GraphCanvas.Viewer.NodeElements.Count);
                List<ISupplierNodeViewModel> suppliers = [.. window.GraphCanvas.Viewer.Session.View.Nodes.OfType<ISupplierNodeViewModel>()];
                Assert.Contains(suppliers, n => n.SuppliedItem.Item == cache.Items["iron-plate"]);
                Assert.Contains(suppliers, n => n.SuppliedItem.Item == cache.Items["copper-plate"]);

                BaseNodeElement selected = Assert.Single(window.GraphCanvas.Viewer.SelectedNodes);
                var selectedSupplier = Assert.IsAssignableFrom<ISupplierNodeViewModel>(selected.ViewModel);
                Assert.Equal(cache.Items["iron-plate"], selectedSupplier.SuppliedItem.Item);
                Assert.True(selected.Highlighted);

                //Verify imported node was repositioned: iron-plate was saved at (1000, 1000), but after import it should
                //be repositioned so its centroid lands at the viewport-center origin in graph space. Loading copper-plate at
                //(500, 500) first adjusts ViewOffset via UpdateGraphBounds, making the origin offset from graph zero. The
                //repositioning centers the imported node(s) at that origin. Expected: node moves from (1000, 1000) to (300, 300).
                Assert.Equal(300, selectedSupplier.Location.X);
                Assert.Equal(300, selectedSupplier.Location.Y);
            } finally {
                File.Delete(pathA);
                File.Delete(pathB);
            }
        }

        //================================================================================================
        // Seam 3: export a genuinely loaded graph to PNG - nonzero file, pixel-sample the node.
        //================================================================================================

        //Graph.Bounds carries no padding once a node exists (GraphExportBounds.Compute), so the exported
        //bitmap's own extent IS the node's drawn rect: dead center always lands inside its filled
        //background, and the true image corner always lands outside FillRoundRect's own corner radius.
        [AvaloniaFact]
        public async Task ExportLoadedGraph_ToPng_WritesANonEmptyFileWithTheNodeVisible() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), new Point(0, 0));
            string savePath = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(savePath);
            string pngPath = TempPngPath();
            try {
                await window.SaveGraphAsAsync();
                string json = await File.ReadAllTextAsync(savePath);

                await window.NewGraphAsync();
                await window.LoadGraphJsonAsync(json, savePath);
                Assert.Single(window.GraphCanvas.Viewer.NodeElements);

                ImageExportWindow? exportWindow = null;
                window.ImageExportDialogStub = viewer => exportWindow = new ImageExportWindow(viewer);
                await window.OpenImageExportAsync();
                exportWindow!.TransparencyCheckBoxControl.IsChecked = true;
                exportWindow.FileTextBoxControl.Text = pngPath;

                await exportWindow.ExportAsync();

                Assert.True(File.Exists(pngPath));
                Assert.True(new FileInfo(pngPath).Length > 0);

                using SKBitmap bitmap = SKBitmap.Decode(pngPath)!;
                Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).Alpha);
                Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            } finally {
                File.Delete(savePath);
                if (File.Exists(pngPath))
                    File.Delete(pngPath);
            }
        }

        //================================================================================================
        // Seam 4: preset import -> resolver visibility; switch-remap covered against the bundled fixtures.
        //================================================================================================

        //Inlines the same stub-executable technique ForemanTest's StubFactorioHarness uses for the export
        //pipeline's happy path - Foreman.Mac.UiTests can't reference that project, so this is a standalone
        //copy of just the one script shape this test needs (--create/--benchmark answered identically).
        private static string NewStubFactorioInstall() {
            string macOsDir = Path.Combine(NewTempDir(), "factorio.app", "Contents", "MacOS");
            Directory.CreateDirectory(macOsDir);
            string installPath = Path.GetDirectoryName(macOsDir)!;
            Directory.CreateDirectory(Path.Combine(installPath, "data"));

            const string p2 = "{\"mods\":[],\"items\":[{\"name\":\"iron-plate\",\"lid\":\"$0\"}]}";
            string body = "#!/bin/sh\n" +
                "touch ./temp-save.zip\n" +
                "cat <<'FOREMAN_EOF'\n" +
                "<<<START-EXPORT-LN>>>\n$0<#~#>Unknown key: \"Iron Plate\"\n<<<END-EXPORT-LN>>>\n" +
                "<<<START-EXPORT-P1>>>\n{}\n<<<END-EXPORT-P1>>>\n" +
                "<<<START-EXPORT-P2>>>\n" + p2 + "\n<<<END-EXPORT-P2>>>\n" +
                "FOREMAN_EOF\n" +
                "exit 0\n";
            string exePath = Path.Combine(macOsDir, "factorio");
            File.WriteAllText(exePath, body);
            File.SetUnixFileMode(exePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return installPath;
        }

        [AvaloniaFact]
        public async Task PresetImport_WritesToTheUserDirectory_ResolverSeesTheNewPreset() {
            string installPath = NewStubFactorioInstall();
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "My Imported Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), (_, _) => Task.FromResult(true), CancellationToken.None);

            Assert.Equal(PresetImportOutcome.Ok, result.Outcome);

            var resolver = new PresetResolver(presetsDirectoryOverride: NewTempDir(), userPresetsDirectoryOverride: userPresetsDir);
            List<Preset> presets = resolver.BuildPresetList(currentPresetName: "Some Other Preset");

            Assert.Contains(presets, p => p.Name == "My Imported Preset");
        }

        //A freshly-imported synthetic preset can't itself be loaded into a DataCache without writing into
        //the real ~/Library Presets directory (DataCache.LoadAllData -> PresetProcessor.PrepPreset has no
        //directory-override seam), so the switch-remap half of this seam runs against the bundled
        //Vanilla/SpaceAge fixtures instead - the exhaustive version of this cascade (annotations, viewport,
        //enabled-object resync) already lives in DialogWindowsEndToEndTests; this only proves it from this
        //file's own save/load-adjacent setup.
        [AvaloniaFact]
        public async Task PresetSwitchViaSettings_BundledFixturePresets_GraphSurvivesRemap() {
            DataCache vanilla = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(vanilla, settings, tempHome);

            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(vanilla.Items["iron-plate"], vanilla.DefaultQuality!), new Point(0, 0));

            window.SettingsDialogStub = options => {
                Preset spaceAge = options.Presets!.First(p => p.Name == SpaceAgePresetName);
                options.SelectedPreset = spaceAge;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(SpaceAgePresetName, settings.CurrentPresetName);
            Assert.NotSame(vanilla, window.DataCache);
            Assert.Single(window.GraphCanvas.Viewer.Session.View.Nodes); //iron-plate exists in both presets
        }

        //================================================================================================
        // Seam 5: cross-preset load - saved under Vanilla, loaded with Space Age active.
        //================================================================================================

        //An exact SavedPresetName match against an installed preset with zero errors takes the silent-
        //switch branch, not the ranked picker - MainWindowPresetResolutionTests already covers the picker
        //branch (no-match/error cases) at the ResolveChosenPresetAsync level; this proves the silent-switch
        //branch actually swaps the live DataCache and reloads the graph, one level up, through
        //LoadGraphJsonAsync itself.
        [AvaloniaFact]
        public async Task CrossPresetLoad_SavedUnderVanillaWithSpaceAgeActive_SilentlySwitchesAndReloadsTheGraph() {
            DataCache vanilla = await GetSharedVanillaCacheAsync();
            DataCache spaceAge = await GetSharedSpaceAgeCacheAsync();
            string tempHome = NewTempHome();

            var savingWindow = NewReadyWindow(vanilla, new AppSettings { CurrentPresetName = VanillaPresetName }, tempHome);
            savingWindow.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(vanilla.Items["iron-plate"], vanilla.DefaultQuality!), new Point(0, 0));
            string path = TempSavePath();
            savingWindow.SaveFilePathStub = () => Task.FromResult<string?>(path);

            try {
                await savingWindow.SaveGraphAsAsync();
                string json = await File.ReadAllTextAsync(path);

                var activeSettings = new AppSettings { CurrentPresetName = SpaceAgePresetName };
                MainWindow activeWindow = NewReadyWindow(spaceAge, activeSettings, tempHome);
                string? switchMessage = null;
                activeWindow.PresetSwitchInfoStub = message => { switchMessage = message; return Task.CompletedTask; };
                activeWindow.PresetSelectionDialogStub = _ => throw new InvalidOperationException("an exact zero-error match must silent-switch, not show the picker");

                await activeWindow.LoadGraphJsonAsync(json, path);

                Assert.NotNull(switchMessage);
                Assert.Equal(VanillaPresetName, activeSettings.CurrentPresetName);
                Assert.NotSame(spaceAge, activeWindow.DataCache);
                Assert.Equal(VanillaPresetName, activeWindow.DataCache!.PresetName);
                Assert.Single(activeWindow.GraphCanvas.Viewer.Session.View.Nodes);
            } finally {
                File.Delete(path);
            }
        }

        //================================================================================================
        // Seam 6: every placeholder divergence line for the three Settings buttons is retired.
        //================================================================================================

        private static string RepoRoot() {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null) {
                if (File.Exists(Path.Combine(dir.FullName, "docs", "upstream-divergences.md")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        [Fact]
        public void UpstreamDivergences_ThreeSettingsButtons_CarryNoUnretiredPlaceholderClaim() {
            string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "upstream-divergences.md"));
            string[] buttonMarkers = ["ImportPresetButton", "SetEnabledFromSciencePacksButton", "SciencePacksWindow", "SaveFileLoadWindow", "\"Load from save\""];

            List<string> flagged = [.. lines.Where(line =>
                (line.Contains("placeholder", StringComparison.Ordinal) || line.Contains("coming in a later phase", StringComparison.Ordinal)) &&
                buttonMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))];

            Assert.NotEmpty(flagged); //proves the grep actually found the ImportPresetButton history line, not a typo'd marker matching nothing

            foreach (string line in flagged) {
                //A leftover mention is only acceptable framed as retired history, not a live gap.
                Assert.Contains("originally", line, StringComparison.Ordinal);
                Assert.True(
                    line.Contains("now opens the real", StringComparison.Ordinal) || line.Contains("is real as of", StringComparison.Ordinal),
                    "Expected a retirement marker alongside the historical placeholder mention: " + line);
            }
        }

        [Fact]
        public void SettingsWindowSource_HasNoPlaceholderComingInALaterPhaseText() {
            string source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Foreman.Mac", "Views", "SettingsWindow.axaml.cs"));

            Assert.DoesNotContain("coming in a later phase", source, StringComparison.Ordinal);
        }

        private static string NewTempHome() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(home);
            return home;
        }
    }
}
