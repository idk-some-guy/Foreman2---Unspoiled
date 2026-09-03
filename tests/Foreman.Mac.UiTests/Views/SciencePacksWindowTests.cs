using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers io-reference.md §6's SciencePacksLoadForm contract (phase 6 Task 6): the 48px pack grid
    //(MaxColumns 14 row/column balancing), the prerequisite cascade in both directions including
    //upstream's own documented OR-as-AND imprecision (ported as-is, docs/upstream-divergences.md), and
    //Confirm's subset rule against the shared EnabledObjectsDerivation helper (reused from
    //SaveFileLoadWindow's ProcessSaveData, not re-implemented).
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

        private static bool StateOf(SciencePacksWindow window, DataCache cache, string name) =>
            window.SciencePackButtonStates[window.PackButtonFor(PackNamed(cache, name))!];

        //Real click through the actual input pipeline (window.Show() + MouseDown/MouseUp at the button's
        //own screen point), not a direct handler call - proves the constructor's Click wiring, not just
        //the cascade logic behind it.
        private static void ClickPack(SciencePacksWindow window, IItem pack) {
            Button button = window.PackButtonFor(pack)!;
            Point screen = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        private static void ClickConfirm(SciencePacksWindow window) {
            Button button = window.ConfirmationButtonControl;
            Point screen = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        //---- grid row/column balancing (reference §6, SciencePacksLoadForm.cs:36-45) ---------------------

        [Theory]
        [InlineData(7, 1, 7)]
        [InlineData(14, 1, 14)]
        [InlineData(15, 2, 8)]
        [InlineData(20, 2, 10)]
        public void ComputeGridDimensions_BalancesIntoANearSquareGridCappedAtMaxColumns(int count, int expectedRows, int expectedColumns) {
            (int rows, int columns) = SciencePacksWindow.ComputeGridDimensions(count);

            Assert.Equal(expectedRows, rows);
            Assert.Equal(expectedColumns, columns);
        }

        //---- population (reference §6: DisabledPackBGColor = DarkRed, one button per DCache.SciencePacks) -

        [AvaloniaFact]
        public async Task Constructor_PopulatesOneDarkRedButtonPerSciencePack_AllStartingDisabled() {
            DataCache cache = await GetVanillaCacheAsync();

            var window = new SciencePacksWindow(cache, []);

            Assert.Equal(cache.SciencePacks.Count, window.SciencePackButtonStates.Count);
            foreach (KeyValuePair<Button, bool> entry in window.SciencePackButtonStates) {
                Assert.False(entry.Value);
                Assert.Equal(Brushes.DarkRed, entry.Key.Background);
                Assert.Equal(48, entry.Key.Width);
                Assert.Equal(48, entry.Key.Height);
            }
        }

        //---- cascade, both directions (reference §6, SciencePacksLoadForm.cs:79-105) ----------------------
        //Fixture chain (vanilla preset, SciencePackPrerequisites): automation -> (none); logistic ->
        //{automation}; military/chemical -> {automation,logistic}; production/utility ->
        //{automation,logistic,chemical}; space -> {automation,logistic,chemical,production,utility}.

        [AvaloniaFact]
        public async Task RealClick_EnablingAPack_CascadesToEnableOnlyItsOwnPrerequisites() {
            DataCache cache = await GetVanillaCacheAsync();
            var window = new SciencePacksWindow(cache, []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ClickPack(window, PackNamed(cache, "production-science-pack"));

            Assert.True(StateOf(window, cache, "production-science-pack"));
            Assert.True(StateOf(window, cache, "automation-science-pack"));
            Assert.True(StateOf(window, cache, "logistic-science-pack"));
            Assert.True(StateOf(window, cache, "chemical-science-pack"));
            Assert.False(StateOf(window, cache, "military-science-pack"));
            Assert.False(StateOf(window, cache, "utility-science-pack"));
            Assert.False(StateOf(window, cache, "space-science-pack"));
        }

        [AvaloniaFact]
        public async Task RealClick_DisablingAPack_CascadesToDisableEveryDependent() {
            DataCache cache = await GetVanillaCacheAsync();
            var window = new SciencePacksWindow(cache, []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ClickPack(window, PackNamed(cache, "production-science-pack")); //enables automation, logistic, chemical too

            ClickPack(window, PackNamed(cache, "logistic-science-pack")); //was enabled by the cascade above - disable it

            Assert.False(StateOf(window, cache, "logistic-science-pack"));
            Assert.False(StateOf(window, cache, "military-science-pack"));
            Assert.False(StateOf(window, cache, "chemical-science-pack"));
            Assert.False(StateOf(window, cache, "production-science-pack"));
            Assert.False(StateOf(window, cache, "utility-science-pack"));
            Assert.False(StateOf(window, cache, "space-science-pack"));
            Assert.True(StateOf(window, cache, "automation-science-pack")); //not a dependent of logistic - stays enabled
        }

        //---- Confirm: subset rule (reference §6, SciencePacksLoadForm.cs:121-161) -------------------------

        [AvaloniaFact]
        public async Task Confirm_UnionsRecipesOnlyForAvailableTechsWhoseSciPackListIsFullySubsetOfAccepted() {
            DataCache cache = await GetVanillaCacheAsync();
            var enabledObjects = new HashSet<IDataObjectBase>();
            var window = new SciencePacksWindow(cache, enabledObjects);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (string packName in new[] {
                "automation-science-pack", "logistic-science-pack", "chemical-science-pack",
                "production-science-pack", "utility-science-pack",
            })
                ClickPack(window, PackNamed(cache, packName)); //accepted = every pack except military and space

            ClickConfirm(window);

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
            var window = new SciencePacksWindow(cache, enabledObjects);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ClickPack(window, PackNamed(cache, "automation-science-pack"));

            Button cancelButton = window.CancellationButtonControl;
            Point screen = cancelButton.TranslatePoint(new Point(cancelButton.Bounds.Width / 2, cancelButton.Bounds.Height / 2), window)!.Value;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.Accepted);
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
            string source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Foreman.Mac", "Views", "SciencePacksWindow.axaml.cs"));

            Assert.Contains("EnabledObjectsDerivation.ResetToPlayerAssembler(", source);
            Assert.Contains("EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(", source);
            //The assembler/beacon/module transitive-enable loop upstream duplicates verbatim between
            //SavefileLoadForm.cs and SciencePacksLoadForm.cs must not be re-implemented here - only
            //referenced through the shared helper above.
            Assert.DoesNotContain("AssociatedItems.Select(item => item.ProductionRecipes)", source);
        }
    }
}
