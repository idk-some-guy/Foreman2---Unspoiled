using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers the science-packs sheet (io-reference.md §6): the Kit.ChooserCell pack grid, the prerequisite
    //cascade in both directions including upstream's documented OR-as-AND imprecision (ported as-is,
    //docs/upstream-divergences.md), Confirm's subset rule through the shared EnabledObjectsDerivation, and the
    //pane's own scrolling.
    public class SciencePacksWindowTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetVanillaCacheAsync() {
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

        private static IItem PackNamed(DataCache cache, string name) => cache.SciencePacks.First(p => p.Name == name);

        private static bool StateOf(SciencePacksSheet sheet, DataCache cache, string name) =>
            sheet.SciencePackButtonStates[sheet.PackButtonFor(PackNamed(cache, name))!];

        //Presents the sheet on a real, shown owner window through SheetHost - needed for the real-click
        //tests below, which prove the constructor's own Click wiring rather than just the cascade logic
        //behind it.
        private static (SciencePacksSheet Sheet, Window Window) NewShownSheet(DataCache cache, HashSet<IDataObjectBase> enabledObjects) {
            (SciencePacksSheet sheet, Window window) = ShowWhileSliding(cache, enabledObjects);
            AnimationClock.UntilSheetOpen(sheet.PackButtonFor(PackNamed(cache, "automation-science-pack"))!);
            return (sheet, window);
        }

        private static (SciencePacksSheet Sheet, Window Window) ShowWhileSliding(DataCache cache, HashSet<IDataObjectBase> enabledObjects) {
            var sheet = new SciencePacksSheet(cache, enabledObjects);
            var window = new Window { Width = 900, Height = 700 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            return (sheet, window);
        }

        //Real click through the actual input pipeline (MouseDown/MouseUp at the button's own screen point
        //on the owner window), not a direct handler call.
        private static void ClickPack(SciencePacksSheet sheet, Window window, IItem pack) {
            ChooserCell button = sheet.PackButtonFor(pack)!;
            Point screen = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        private static void ClickConfirm(SciencePacksSheet sheet, Window window) {
            Button button = sheet.ConfirmationButtonControl;
            Point screen = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public async Task ASheetJustShown_IsStillSlidingIn_SoAPointTakenNowGoesStaleOnceItSettles() {
            DataCache cache = await GetVanillaCacheAsync();
            (SciencePacksSheet sheet, Window window) = ShowWhileSliding(cache, []);
            ChooserCell cell = sheet.PackButtonFor(PackNamed(cache, "production-science-pack"))!;
            Point Centre() => cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
            Point early = Centre();

            Assert.False(AnimationClock.SheetPanelIsOpen(cell));

            AnimationClock.UntilSheetOpen(cell);

            Assert.True(AnimationClock.SheetPanelIsOpen(cell));
            Assert.NotEqual(early, Centre());
        }

        //---- population (reference §6: DisabledPackBGColor, now Foreman.Color.Error, one button per
        //DCache.SciencePacks) ----------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task Constructor_PopulatesOneUnselectedCellPerSciencePack_NamedForThePack() {
            DataCache cache = await GetVanillaCacheAsync();

            var sheet = new SciencePacksSheet(cache, []);

            Assert.Equal(cache.SciencePacks.Count, sheet.SciencePackButtonStates.Count);
            foreach (KeyValuePair<ChooserCell, bool> entry in sheet.SciencePackButtonStates) {
                Assert.False(entry.Value);
                Assert.False(entry.Key.IsSelected);
                Assert.Equal(((IItem)entry.Key.Tag!).FriendlyName, entry.Key.FullName);
            }
        }

        //---- cascade, both directions (reference §6, SciencePacksLoadForm.cs:79-105) ----------------------
        //Fixture chain (vanilla preset, SciencePackPrerequisites): automation -> (none); logistic ->
        //{automation}; military/chemical -> {automation,logistic}; production/utility ->
        //{automation,logistic,chemical}; space -> {automation,logistic,chemical,production,utility}.

        [AvaloniaFact]
        public async Task RealClick_EnablingAPack_CascadesToEnableOnlyItsOwnPrerequisites() {
            DataCache cache = await GetVanillaCacheAsync();
            (SciencePacksSheet sheet, Window window) = NewShownSheet(cache, []);

            ClickPack(sheet, window, PackNamed(cache, "production-science-pack"));

            Assert.True(StateOf(sheet, cache, "production-science-pack"));
            Assert.True(StateOf(sheet, cache, "automation-science-pack"));
            Assert.True(StateOf(sheet, cache, "logistic-science-pack"));
            Assert.True(StateOf(sheet, cache, "chemical-science-pack"));
            Assert.False(StateOf(sheet, cache, "military-science-pack"));
            Assert.False(StateOf(sheet, cache, "utility-science-pack"));
            Assert.False(StateOf(sheet, cache, "space-science-pack"));
        }

        [AvaloniaFact]
        public async Task RealClick_DisablingAPack_CascadesToDisableEveryDependent() {
            DataCache cache = await GetVanillaCacheAsync();
            (SciencePacksSheet sheet, Window window) = NewShownSheet(cache, []);
            ClickPack(sheet, window, PackNamed(cache, "production-science-pack")); //enables automation, logistic, chemical too

            ClickPack(sheet, window, PackNamed(cache, "logistic-science-pack")); //was enabled by the cascade above - disable it

            Assert.False(StateOf(sheet, cache, "logistic-science-pack"));
            Assert.False(StateOf(sheet, cache, "military-science-pack"));
            Assert.False(StateOf(sheet, cache, "chemical-science-pack"));
            Assert.False(StateOf(sheet, cache, "production-science-pack"));
            Assert.False(StateOf(sheet, cache, "utility-science-pack"));
            Assert.False(StateOf(sheet, cache, "space-science-pack"));
            Assert.True(StateOf(sheet, cache, "automation-science-pack")); //not a dependent of logistic - stays enabled
        }

        //---- Confirm: subset rule (reference §6, SciencePacksLoadForm.cs:121-161) -------------------------

        [AvaloniaFact]
        public async Task Confirm_UnionsRecipesOnlyForAvailableTechsWhoseSciPackListIsFullySubsetOfAccepted() {
            DataCache cache = await GetVanillaCacheAsync();
            var enabledObjects = new HashSet<IDataObjectBase>();
            (SciencePacksSheet sheet, Window window) = NewShownSheet(cache, enabledObjects);
            foreach (string packName in new[] {
                "automation-science-pack", "logistic-science-pack", "chemical-science-pack",
                "production-science-pack", "utility-science-pack",
            })
                ClickPack(sheet, window, PackNamed(cache, packName)); //accepted = every pack except military and space

            ClickConfirm(sheet, window);

            var accepted = cache.SciencePacks.Where(p => p.Name is not ("military-science-pack" or "space-science-pack")).ToHashSet();
            var expected = new HashSet<IDataObjectBase>();
            EnabledObjectsDerivation.ResetToPlayerAssembler(cache, expected);
            foreach (ITechnology tech in cache.Technologies.Values)
                if (tech.Available && !tech.SciPackList.Except(accepted).Any())
                    expected.UnionWith(tech.UnlockedRecipes);
            EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(cache, expected);
            Assert.True(enabledObjects.SetEquals(expected));

            //artillery needs exactly the military pack beyond what's accepted here (its SciPackList is
            //automation/logistic/military/chemical/utility - no production) - a concrete instance of
            //"excluded by exactly one missing pack" that also proves the exclusion is material: none of
            //artillery's 3 unlocked recipes made it into enabledObjects.
            ITechnology artillery = cache.Technologies["artillery"];
            Assert.True(artillery.Available);
            Assert.Single(artillery.SciPackList.Except(accepted));
            Assert.NotEmpty(artillery.UnlockedRecipes);
            Assert.All(artillery.UnlockedRecipes, recipe => Assert.DoesNotContain(recipe, enabledObjects));

            //robotics needs only automation/logistic/chemical, all accepted here - fully included.
            ITechnology robotics = cache.Technologies["robotics"];
            Assert.NotEmpty(robotics.UnlockedRecipes);
            Assert.All(robotics.UnlockedRecipes, recipe => Assert.Contains(recipe, enabledObjects));
        }

        [AvaloniaFact]
        public async Task Cancel_LeavesEnabledObjectsUntouched() {
            DataCache cache = await GetVanillaCacheAsync();
            IRecipe preExisting = cache.Recipes.Values.First();
            var enabledObjects = new HashSet<IDataObjectBase> { preExisting };
            (SciencePacksSheet sheet, Window window) = NewShownSheet(cache, enabledObjects);
            ClickPack(sheet, window, PackNamed(cache, "automation-science-pack"));

            Button cancelButton = sheet.CancellationButtonControl;
            Point screen = cancelButton.TranslatePoint(new Point(cancelButton.Bounds.Width / 2, cancelButton.Bounds.Height / 2), window)!.Value;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(sheet.Accepted);
            Assert.Single(enabledObjects);
            Assert.Contains(preExisting, enabledObjects);
        }

        //---- helper reuse (io-reference.md risk 5: shared EnabledObjectsDerivation, not a second copy) ----

        private static string RepoRoot() {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ForemanMac.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (ForemanMac.slnx) above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void ConfirmHandler_CallsTheSharedDerivationHelper_WithNoSecondTransitiveDeriveImplementation() {
            string source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Foreman.Mac", "Views", "Sheets", "SciencePacksSheet.axaml.cs"));

            Assert.Contains("EnabledObjectsDerivation.ResetToPlayerAssembler(", source);
            Assert.Contains("EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(", source);
            //The assembler/beacon/module transitive-enable loop upstream duplicates verbatim between
            //SavefileLoadForm.cs and SciencePacksLoadForm.cs must not be re-implemented here - only
            //referenced through the shared helper above.
            Assert.DoesNotContain("AssociatedItems.Select(item => item.ProductionRecipes)", source);
        }

        //---- the kit sheet: named width, chooser cells, trailing button row ------------------------------

        [AvaloniaFact]
        public async Task SciencePacks_Width_IsTheNamedWidthToken() {
            DataCache cache = await GetVanillaCacheAsync();
            var sheet = new SciencePacksSheet(cache, []);
            (_, SheetHost host, _) = SheetFixture.Present(sheet);

            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthStandard"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Select researched science packs", SheetFixture.FrameOf(host, sheet).Title);
            Assert.Equal(["Cancel", "Confirm"], sheet.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.Same(sheet.ConfirmationButtonControl, sheet.Buttons.ResolveDefaultButton());
        }

        [AvaloniaFact]
        public async Task SciencePacks_Grid_UsesChooserCells_At88pt() {
            DataCache cache = await GetVanillaCacheAsync();
            var sheet = new SciencePacksSheet(cache, []);
            (Window window, SheetHost host, _) = SheetFixture.Present(sheet);

            double pitch = SheetFixture.Token("Foreman.Size.ChooserCell");
            Assert.Equal(88, pitch);
            ChooserCell[] cells = [.. sheet.GetVisualDescendants().OfType<ChooserCell>()];
            Assert.Equal(cache.SciencePacks.Count, cells.Length);
            Assert.Empty(sheet.GetVisualDescendants().OfType<Button>());
            double right = SheetFixture.PanelOf(host, sheet).Bounds.Width;
            foreach (ChooserCell cell in cells) {
                Assert.Equal(pitch, cell.Bounds.Width, 0.5);
                Assert.Equal(pitch, cell.Bounds.Height, 0.5);
                Assert.True(cell.TranslatePoint(new Point(cell.Bounds.Width, 0), SheetFixture.PanelOf(host, sheet))!.Value.X <= right);
            }
            Assert.True(cells.Select(c => c.TranslatePoint(new Point(0, 0), sheet)!.Value.Y).Distinct().Count() > 1);

            ChooserCell automation = sheet.PackButtonFor(PackNamed(cache, "automation-science-pack"))!;
            Assert.True(automation.Focusable);
            ClickPack(sheet, window, PackNamed(cache, "automation-science-pack"));
            Assert.True(automation.IsSelected);
            Assert.Contains(":selected", automation.Classes);
            CanvasKeys.KeyDownOn(automation, Key.Space);
            Assert.False(automation.IsSelected);
            Assert.False(StateOf(sheet, cache, "automation-science-pack"));
        }

        [AvaloniaFact]
        public async Task VoiceOverPress_OnACell_TogglesItAndCascadesLikeAClick() {
            DataCache cache = await GetVanillaCacheAsync();
            var sheet = new SciencePacksSheet(cache, []);
            SheetFixture.Present(sheet);
            ChooserCell chemical = sheet.PackButtonFor(PackNamed(cache, "chemical-science-pack"))!;

            ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(chemical)).Invoke();
            Dispatcher.UIThread.RunJobs();

            Assert.True(chemical.IsSelected);
            Assert.True(StateOf(sheet, cache, "chemical-science-pack"));
            Assert.True(StateOf(sheet, cache, "automation-science-pack"));
            Assert.True(StateOf(sheet, cache, "logistic-science-pack"));
            Assert.False(StateOf(sheet, cache, "production-science-pack"));
        }

        [AvaloniaFact]
        public async Task ManyPacksInAShortWindow_ScrollInsideTheSheet_WhichStaysInTheWindow() {
            DataCache cache = await GetVanillaCacheAsync();
            var sheet = new SciencePacksSheet(cache, []);
            (Window window, SheetHost host, _) = SheetFixture.Present(sheet, 700, 300);
            AnimationClock.UntilReflowsSettled(window);

            Border panel = SheetFixture.PanelOf(host, sheet);
            Point bottom = panel.TranslatePoint(new Point(0, panel.Bounds.Height), window)!.Value;
            Assert.True(bottom.Y <= window.Bounds.Height + 0.5);
            ScrollViewer scroller = sheet.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.Contains(sheet.PackButtonFor(PackNamed(cache, "space-science-pack"))!, scroller.GetVisualDescendants().OfType<ChooserCell>());
        }
    }
}
