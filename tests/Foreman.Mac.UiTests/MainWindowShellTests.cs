using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SkiaSharp;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class MainWindowShellTests {
        private static readonly string[] ExpectedButtonNames = {
            "New Graph", "Load", "Save", "Save As", "Import Graph", "Export Image",
            "Add Item", "Add Recipe", "Autoconnect", "Settings", "Help / Git repo",
            "Show Graph Summary", "Align Selected",
        };

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

        private static string FlowchartJson() {
            string repoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
            return File.ReadAllText(Path.Combine(repoRoot, "tests", "ForemanTest", "assets", "Flowchart.fjson"));
        }

        [AvaloniaFact]
        public void Toolbar_ContainsAllThirteenCommandButtons() {
            var window = new MainWindow();
            window.Show();

            var names = window.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).ToList();

            foreach (string expected in ExpectedButtonNames)
                Assert.Contains(expected, names);
        }

        //Task 7 enables Add Item/Add Recipe/Autoconnect/Align Selected (reference §8/§10/§11 step 11); phase
        //5b Task 1 enables Settings, Task 4 enables Show Graph Summary; phase 6 Task 1 enables Save/Save As,
        //Task 2 enables Import Graph, Task 3 enables Export Image - Help stays disabled, later-task scope.
        private static readonly string[] StillStubbedButtonNames = {
            "Help / Git repo",
        };

        [AvaloniaFact]
        public void Toolbar_NewGraphAndLoadAndTask7ButtonsAreEnabled_RemainingStubsAreNot() {
            var window = new MainWindow();
            window.Show();

            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => ExpectedButtonNames.Contains(AutomationProperties.GetName(b)))
                .ToDictionary(b => AutomationProperties.GetName(b)!, b => b.IsEnabled);

            Assert.Equal(ExpectedButtonNames.Length, buttons.Count);
            foreach (string name in ExpectedButtonNames.Where(n => !StillStubbedButtonNames.Contains(n)))
                Assert.True(buttons[name], name);
            foreach (string name in StillStubbedButtonNames)
                Assert.False(buttons[name], name);
        }

        [AvaloniaFact]
        public void GridlinesAndGraphOptionsControls_AreEnabled() {
            var window = new MainWindow();
            window.Show();

            foreach (string name in new[] { "MinorGridlinesDropDown", "MajorGridlinesDropDown", "RateOptionsDropDown" })
                Assert.True(window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == name).IsEnabled);
            foreach (string name in new[] { "GridlinesCheckbox", "IconViewCheckBox", "PauseUpdatesCheckbox" })
                Assert.True(window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == name).IsEnabled);
        }

        //Ports GraphViewer_KeyDown's Space handler (reference §7): wired as a separate KeyDown subscription
        //on GraphCanvas rather than the control's own OnKeyDown override, since it needs to reach the
        //toolbar's gridlines checkbox.
        [AvaloniaFact]
        public void SpaceKey_OnGraphCanvas_TogglesShowGridAndSyncsTheGridlinesCheckbox() {
            var window = new MainWindow();
            window.Show();
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            window.GraphCanvas.Grid.ShowGrid = false;
            checkbox.IsChecked = false;
            window.GraphCanvas.Focus();

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(window.GraphCanvas.Grid.ShowGrid);
            Assert.True(checkbox.IsChecked);

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.False(window.GraphCanvas.Grid.ShowGrid);
            Assert.False(checkbox.IsChecked);
        }

        //Retires docs/upstream-divergences.md's WASD-S/Cmd+S entry: plain S still pans, but Cmd+S must reach
        //Save (the NativeMenu KeyGesture) instead of also panning the view underneath it. A node is required
        //so Graph.Bounds is non-empty - Viewport.UpdateGraphBounds forces ViewOffset back to (0,0) whenever
        //the graph is empty, regardless of any pan delta, so an empty graph can't observe panning at all.
        [AvaloniaFact]
        public async Task SKey_OnGraphCanvas_PansOnlyWithoutMeta() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(2000, 2000));
            window.GraphCanvas.Focus();
            System.Drawing.Point before = window.GraphCanvas.Viewport.ViewOffset;

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Meta);

            Assert.Equal(before, window.GraphCanvas.Viewport.ViewOffset);

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);

            Assert.NotEqual(before, window.GraphCanvas.Viewport.ViewOffset);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): Ctrl+S gates
        //the pan on Linux the same way Cmd+S does on macOS, via the UseIsMacOs seam.
        [AvaloniaFact]
        public async Task SKey_OnGraphCanvas_OnLinux_PansOnlyWithoutCtrl() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(2000, 2000));
            window.GraphCanvas.Focus();
            System.Drawing.Point before = window.GraphCanvas.Viewport.ViewOffset;

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);

            Assert.Equal(before, window.GraphCanvas.Viewport.ViewOffset);

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);

            Assert.NotEqual(before, window.GraphCanvas.Viewport.ViewOffset);
        }

        //Regression: ApplyLoadedSettings must seed GraphCanvas.Viewer.IconsOnly from settings.IconsOnlyView
        //before the Icon View checkbox reads it (mirroring how it already seeds Graph.SelectedRateUnit
        //before the rate-unit dropdown reads it) - otherwise a persisted "true" is silently discarded on
        //every launch, since BindViewOptionControls only ever reads Viewer.IconsOnly back into the checkbox.
        [AvaloniaFact]
        public void ApplyLoadedSettings_IconsOnlyViewTrue_SeedsViewerAndChecksTheIconViewBox() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { IconsOnlyView = true };

            window.ApplyLoadedSettings();

            Assert.True(window.GraphCanvas.Viewer.IconsOnly);
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconViewCheckBox");
            Assert.True(checkbox.IsChecked);
        }

        //Regression: ApplyLoadedSettings only ever wired the gridline/rate-unit/icon-view/pause-updates
        //controls into the canvas; everything else upstream's MainForm_Load also applies at launch
        //(LevelOfDetail, ArrowsOnLinks, NodeCountForSimpleView, IconsSize, DynamicLineWidth,
        //FlagOUSuppliedNodes, RoundAssemblerCount, AbbreviateSciPacks, ShowRecipeToolTip, and the four
        //guide-arrow toggles) stayed at code-level defaults regardless of what was saved.
        [AvaloniaFact]
        public void ApplyLoadedSettings_NonDefaultSettings_SeedsContextViewerAndArrowRenderer() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings {
                LevelOfDetail = (int)LevelOfDetail.High,
                ArrowsOnLinks = true,
                NodeCountForSimpleView = 42,
                IconsSize = 64,
                DynamicLineWidth = true,
                FlagOUSuppliedNodes = true,
                RoundAssemblerCount = true,
                AbbreviateSciPacks = false,
                ShowRecipeToolTip = false,
                ShowErrorArrows = true,
                ShowWarningArrows = true,
                ShowDisconnectedArrows = true,
                ShowOUSuppliedArrows = true,
            };

            window.ApplyLoadedSettings();

            NodeElementContext context = window.GraphCanvas.Viewer.Context;
            Assert.Equal(LevelOfDetail.High, context.LevelOfDetail);
            Assert.True(context.ArrowsOnLinks);
            Assert.Equal(64, context.IconsDrawSize);
            Assert.True(context.DynamicLinkWidth);
            Assert.True(context.FlagOUSuppliedNodes);
            Assert.True(context.RoundAssemblerCount);
            Assert.False(context.AbbreviateSciPacks);
            Assert.False(context.ShowRecipeToolTip);
            Assert.Equal(42, window.GraphCanvas.Viewer.NodeCountForSimpleView);

            PointingArrowRenderer arrows = window.GraphCanvas.Viewer.ArrowRenderer;
            Assert.True(arrows.ShowErrorArrows);
            Assert.True(arrows.ShowWarningArrows);
            Assert.True(arrows.ShowDisconnectedArrows);
            Assert.True(arrows.ShowOUNodeArrows);
        }

        //Regression: ApplyLoadedSettings never seeded EnableExtraProductivityForNonMiners (upstream
        //MainForm.cs:102), so a persisted "true" silently reverted to the code-level false default on every
        //launch; seeds both Graph (the value LoadDocument reads/writes) and Context (what RecipeNodeElement
        //actually checks while drawing), mirroring the pair LoadDocument already keeps in sync.
        [AvaloniaFact]
        public void ApplyLoadedSettings_EnableExtraProductivityForNonMinersTrue_SeedsGraphAndContext() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { EnableExtraProductivityForNonMiners = true };

            window.ApplyLoadedSettings();

            Assert.True(window.GraphCanvas.Viewer.Graph.EnableExtraProductivityForNonMiners);
            Assert.True(window.GraphCanvas.Viewer.Context.EnableExtraProductivityForNonMiners);
        }

        //Regression (task 7, reference §8): SmartNodeDirection/DefaultNodeDirection were never read off
        //AppSettings anywhere - GraphCanvas.SmartNodeDirection stayed permanently false and Graph.
        //DefaultNodeDirection stayed permanently Up regardless of what was saved.
        [AvaloniaFact]
        public void ApplyLoadedSettings_SmartNodeDirectionAndDefaultNodeDirection_SeedsCanvasAndGraph() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { SmartNodeDirection = false, DefaultNodeDirection = NodeDirection.Down };

            window.ApplyLoadedSettings();

            Assert.False(window.GraphCanvas.SmartNodeDirection);
            Assert.Equal(NodeDirection.Down, window.GraphCanvas.Viewer.Graph.DefaultNodeDirection);
        }

        //Regression: ApplyLoadedSettings never applied FlagDarkMode to the canvas itself - App.ApplyTheme
        //only swapped the window chrome's ThemeVariant, leaving GraphViewer.BackgroundColor stuck on its
        //light-mode default (upstream MainForm.SetDarkMode/SetLightMode recolors the graph on every load).
        [AvaloniaFact]
        public void ApplyLoadedSettings_DarkTheme_AppliesDarkThemeToViewer() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Dark };

            window.ApplyLoadedSettings();

            Assert.Equal(new SKColor(23, 23, 23), window.GraphCanvas.Viewer.BackgroundColor);
        }

        [AvaloniaFact]
        public void ApplyLoadedSettings_LightTheme_AppliesLightThemeToViewer() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Dark };
            window.ApplyLoadedSettings();

            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Light };
            window.ApplyLoadedSettings();

            Assert.Equal(SKColors.White, window.GraphCanvas.Viewer.BackgroundColor);
        }

        //Regression (human nit 1, upstream MainForm.cs:36-46): a Settings Confirm with "Enable Dark Mode"
        //unchecked now commits ThemeMode.Light (see SettingsWindow.CommitGraphOptionsTab), which must force
        //both the canvas and the app chrome to light even with the app already themed dark - simulating an
        //OS whose own appearance is dark, since Avalonia's RequestedThemeVariant otherwise wins over it.
        [AvaloniaFact]
        public void ApplyLoadedSettings_LightTheme_ForcesLightRegardlessOfAmbientDarkSystem() {
            var app = (App)Application.Current!;
            app.ApplyTheme(ThemeMode.Dark);

            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Light };

            window.ApplyLoadedSettings();
            app.ApplyTheme(window.Settings.FlagDarkMode);

            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            Assert.Equal(SKColors.White, window.GraphCanvas.Viewer.BackgroundColor);
        }

        //Regression (task 7): DCache/DefaultAssemblerQuality were only ever assigned inside LoadDocument, so
        //a fresh/New graph had neither before the first file load - blocking recipe-node creation and this
        //task's Add Item/Add Recipe choosers (both need Viewer.Context.DCache) from ever working at boot.
        [AvaloniaFact]
        public void ApplyLoadedSettings_DataCacheAssigned_SeedsViewerDCacheAndGraphDefaultAssemblerQuality() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = MinimalCacheWithDefaultQuality();
            window.DataCache = cache;
            window.Settings = new AppSettings();

            window.ApplyLoadedSettings();

            Assert.Same(cache, window.GraphCanvas.Viewer.Context.DCache);
            Assert.Same(cache.DefaultQuality, window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality);
        }

        private static DataCache MinimalCacheWithDefaultQuality() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            return cache;
        }

        //Regression: upstream clamps IconsSize into [8,256] before applying it (MainForm.cs:117-119); an
        //out-of-range persisted value (from an older build, a hand-edited settings.json, etc.) must not
        //reach the canvas unclamped.
        [AvaloniaFact]
        public void ApplyLoadedSettings_IconsSizeOutOfRange_ClampsToEightAndTwoFiftySix() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { IconsSize = 4 };

            window.ApplyLoadedSettings();

            Assert.Equal(8, window.GraphCanvas.Viewer.Context.IconsDrawSize);

            window.Settings = new AppSettings { IconsSize = 4000 };
            window.ApplyLoadedSettings();

            Assert.Equal(256, window.GraphCanvas.Viewer.Context.IconsDrawSize);
        }

        //Regression: LoadDocument sets Graph.SelectedRateUnit from the save but nothing resynced
        //RateOptionsDropDown.SelectedIndex or persisted it back to settings (upstream's
        //ApplyLoadedGraphUiState does both, first thing, reference MainForm.cs:227-231).
        //ResyncRateOptionsDropDown is LoadGraphAsync's post-load step (internal, exercised directly here -
        //LoadGraphAsync itself needs a real file-picker StorageProvider this headless test has none of).
        [AvaloniaFact]
        public async Task LoadDocument_SaveWithNonDefaultRateUnit_ResyncDropDownMatchesSettingsAndAvoidsDoubleSolve() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            DataCache cache = await GetCacheAsync();

            GraphLoadResult result = window.GraphCanvas.Viewer.LoadDocument(cache, FlowchartJson());
            Assert.True(result.Success, result.ErrorMessage);
            ProductionGraph.RateUnit expectedUnit = window.GraphCanvas.Viewer.Graph.SelectedRateUnit;

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;
            window.ResyncRateOptionsDropDown();

            var rateOptions = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RateOptionsDropDown");
            Assert.Equal((int)expectedUnit, rateOptions.SelectedIndex);
            Assert.Equal(expectedUnit, window.Settings!.DefaultRateUnit);
            Assert.Equal(0, solveCount); //setting the dropdown's index alone must not re-trigger UpdateNodeValues
        }

        //Regression: BindViewOptionControls subscribed its six control event handlers with += on every call,
        //never unsubscribing first (unlike its own ResyncRateOptionsDropDown pattern above). It's called from
        //boot, ReloadForPresetAsync, and now every OpenSettingsAsync Confirm, so a second ApplyLoadedSettings
        //call left two PauseUpdatesCheckbox handlers stacked - one user toggle then fired UpdateNodeValues
        //twice instead of once.
        [AvaloniaFact]
        public void ApplyLoadedSettings_CalledTwice_TogglingPauseUpdatesStillResolvesExactlyOnce() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            window.ApplyLoadedSettings();

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            var pauseUpdates = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PauseUpdatesCheckbox");
            pauseUpdates.IsChecked = true;
            pauseUpdates.IsChecked = false; //resuming updates fires UpdateNodeValues once per subscribed handler

            Assert.Equal(1, solveCount);
        }

        //Minor#4 (final fix wave): BindViewOptionControls wrote rateOptions.SelectedIndex directly, with the
        //handler already subscribed after the first bind - a second ApplyLoadedSettings call (e.g. a preset-
        //switching Confirm) that also changed settings.DefaultRateUnit fired OnRateOptionsSelectionChanged
        //and re-solved, on top of the apply-tail's own explicit solve.
        [AvaloniaFact]
        public void ApplyLoadedSettings_CalledTwiceWithChangedRateUnit_ResyncsWithoutExtraSolve() {
            var window = new MainWindow();
            window.Show();
            var settings = new AppSettings { DefaultRateUnit = ProductionGraph.RateUnit.Per1Min };
            window.Settings = settings;
            window.ApplyLoadedSettings();

            settings.DefaultRateUnit = ProductionGraph.RateUnit.Per1Hour;
            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            window.ApplyLoadedSettings();

            var rateOptions = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RateOptionsDropDown");
            Assert.Equal((int)ProductionGraph.RateUnit.Per1Hour, rateOptions.SelectedIndex);
            Assert.Equal(0, solveCount);
        }

        //Regression: New used to detach GraphCanvas from GraphViewerHost entirely (Content = null), leaving
        //a subsequent Load rendering into an off-tree control. Upstream's NewGraph() only ever clears the
        //live viewer's graph (GraphViewer.ClearGraph() -> Graph.ClearGraph()) and never touches the host.
        [AvaloniaFact]
        public async Task NewGraphCommand_Execute_ClearsViewerButKeepsCanvasMounted_ThenLoadStillRenders() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);
            Assert.NotEmpty(window.GraphCanvas.Viewer.NodeElements);
            int redrawCountBeforeNew = window.GraphCanvas.RedrawRequestCount;

            window.Commands.New.Execute(null);

            Assert.Empty(window.GraphCanvas.Viewer.NodeElements);
            Assert.Same(window.GraphCanvas, window.GraphViewerHost.Content);
            Assert.True(window.GraphCanvas.RedrawRequestCount > redrawCountBeforeNew); //New must repaint the now-empty graph, same as Autoconnect/AlignSelection already do

            GraphLoadResult result = window.GraphCanvas.Viewer.LoadDocument(cache, FlowchartJson());
            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEmpty(window.GraphCanvas.Viewer.NodeElements);
        }

        //IMPORTANT 4 (final fix wave): New left an open panel bound to a node the clear just deleted -
        //upstream's ClearFloatingControls has no equivalent on this port's own New path.
        [AvaloniaFact]
        public void NewGraphCommand_Execute_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            window.Commands.New.Execute(null);

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        //IMPORTANT 4 (final fix wave): loading a save over a graph with an open edit panel left it bound to
        //a now-dead node, same gap as New above - GraphCanvasControl.LoadDocument wraps Viewer.LoadDocument
        //so LoadGraphAsync's real call site gets the close for free instead of duplicating it there.
        [AvaloniaFact]
        public async Task GraphCanvasLoadDocument_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            GraphLoadResult result = window.GraphCanvas.LoadDocument(cache, FlowchartJson());

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        //Phase 7 deferred-minors sweep (widened toolbar-staleness scope): a toolbar button click is a plain
        //Avalonia Button press, never a canvas pointer event, so it never reached GraphCanvasControl.
        //OnPointerPressed's own click-outside-closes-panel guard - the panel just sat open and stale behind
        //whatever the button did. Real MouseDown/MouseUp at the button's own screen position (not
        //Commands.X.Execute(null)) so this exercises Avalonia's genuine press/release/Click pipeline, the
        //same real-click precedent LinkDragTests.WindowCenterOf documents.
        private static Avalonia.Point WindowCenterOf(Avalonia.Visual control, Window window) =>
            Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        private static void RealClick(Window window, string automationName) {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); //forces layout so the button has real Bounds
            Button button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == automationName);
            Avalonia.Point screenPoint = WindowCenterOf(button, window);
            window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);
        }

        [AvaloniaFact]
        public void AutoconnectButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Autoconnect");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void AlignSelectionButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Align Selected");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void SettingsButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            //No DataCache/Settings wired up: OpenSettingsAsync's own early-return guard fires right after the
            //close, so this never risks opening a real (unstubbed, unclosable-in-headless) modal.
            RealClick(window, "Settings");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void GraphSummaryButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphSummaryDialogStub = _ => { };
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Show Graph Summary");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void ExportImageButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.ImageExportDialogStub = _ => { };
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Export Image");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void VersionLabel_ShowsAppVersion() {
            var window = new MainWindow();
            window.Show();

            var label = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "VersionLabel");

            Assert.Equal(AppVersion.VersionedDisplay, label.Text);
        }

        [AvaloniaFact]
        public void VersionLabel_MatchesSpecFormatVerbatim() {
            var window = new MainWindow();
            window.Show();

            var label = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "VersionLabel");

            var informational = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            var shortSemVer = informational?.Split('+')[0];
            Assert.Matches(@"^\d+\.\d+\.\d+$", shortSemVer);

            var upstream = typeof(AppVersion).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpstreamVersion")?.Value;
            Assert.Matches(@"^\d+\.\d+\.\d+$", upstream);

            Assert.Equal($"v {shortSemVer} based on {upstream}", label.Text);
        }

        [AvaloniaFact]
        public void NativeMenu_HasTopLevelFileEditViewHelp() {
            var window = new MainWindow();

            var headers = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();

            Assert.Contains("File", headers);
            Assert.Contains("Edit", headers);
            Assert.Contains("View", headers);
            Assert.Contains("Help", headers);
        }

        [AvaloniaFact]
        public void NativeMenu_FileContainsSaveWithCmdSGesture() {
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            var save = fileMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Save");

            Assert.Equal(Key.S, save.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, save.Gesture.KeyModifiers);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): every
        //NativeMenu gesture reads PlatformModifiers.Primary, which BuildNativeMenu bakes in once at
        //construction - forcing Linux before building the window proves the whole menu picks up Ctrl.
        [AvaloniaFact]
        public void NativeMenu_FileContainsSaveWithCtrlSGesture_OnLinux() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            var save = fileMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Save");

            Assert.Equal(Key.S, save.Gesture!.Key);
            Assert.Equal(KeyModifiers.Control, save.Gesture.KeyModifiers);
        }

        [AvaloniaFact]
        public void NativeMenu_FileNewSaveImportAndExportAreEnabled() {
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            var items = fileMenu.Items.OfType<NativeMenuItem>().ToDictionary(i => i.Header!, i => i.IsEnabled);

            Assert.True(items["New"]);
            Assert.True(items["Save"]);
            Assert.True(items["Save As"]);
            Assert.True(items["Import"]);
            Assert.True(items["Export Image"]);
        }

        [AvaloniaFact]
        public void Constructor_SetsWindowStateMaximized() {
            var window = new MainWindow();

            Assert.Equal(WindowState.Maximized, window.WindowState);
        }

        [AvaloniaFact]
        public void ApplyTheme_Dark_SetsActualThemeVariantDark() {
            var app = (App)Application.Current!;

            app.ApplyTheme(ThemeMode.Dark);
            var window = new MainWindow();

            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        }

        [AvaloniaFact]
        public void GroupBoxBorders_ThemedAcrossLightAndDark_ResolveDifferentColors() {
            var app = (App)Application.Current!;

            app.ApplyTheme(ThemeMode.Light);
            var lightWindow = new MainWindow();
            lightWindow.Show();
            var lightColor = GroupBoxBorderColor(lightWindow, "GridLinesGroupBox");

            app.ApplyTheme(ThemeMode.Dark);
            var darkWindow = new MainWindow();
            darkWindow.Show();
            var darkColor = GroupBoxBorderColor(darkWindow, "GridLinesGroupBox");

            Assert.NotEqual(lightColor, darkColor);
        }

        private static Color GroupBoxBorderColor(MainWindow window, string borderName) {
            var border = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == borderName);
            return Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color;
        }
    }
}
