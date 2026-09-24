using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TabStrip = Foreman.Mac.Kit.Controls.TabStrip;

namespace Foreman.Mac.UiTests.Views {
    public class PresetComparatorWindowTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string SpaceAgePresetName = "Factorio 2.0 Space Age";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedVanillaCache;
        private static DataCache? sharedSpaceAgeCache;

        private static async Task<(DataCache Vanilla, DataCache SpaceAge)> GetCachesAsync() {
            if (sharedVanillaCache is not null && sharedSpaceAgeCache is not null)
                return (sharedVanillaCache, sharedSpaceAgeCache);
            await CacheGate.WaitAsync();
            try {
                if (sharedVanillaCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedVanillaCache = cache;
                }
                if (sharedSpaceAgeCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(SpaceAgePresetName, false, false), new Progress<KeyValuePair<int, string>>());
                    sharedSpaceAgeCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return (sharedVanillaCache, sharedSpaceAgeCache);
        }

        private static DataCache NewEmptyCache() => new(filterRecipes: false);

        private static SubgroupPrototype NewSubgroup(DataCache cache) {
            var group = new GroupPrototype(cache, "g", "G", "a");
            var subgroup = new SubgroupPrototype(cache, "sg", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            return subgroup;
        }

        //--- Bucketing (ProcessObjects) -----------------------------------------------------------------

        [Fact]
        public void ProcessObjects_BucketsIntoLeftOnlyMatchedRightOnly() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype NewItem(string name) => new(cache, name, name, subgroup, "a") { Available = true };

            ItemPrototype leftOnly = NewItem("left-only");
            ItemPrototype matchedLeft = NewItem("shared");
            ItemPrototype matchedRight = NewItem("shared");
            ItemPrototype rightOnly = NewItem("right-only");

            var leftDict = new Dictionary<string, IItem> { ["left-only"] = leftOnly, ["shared"] = matchedLeft };
            var rightDict = new Dictionary<string, IItem> { ["shared"] = matchedRight, ["right-only"] = rightOnly };
            var output = new List<object>[] { [], [], [], [] };

            PresetComparatorWindow.ProcessObjects(leftDict, rightDict, output);

            Assert.Same(leftOnly, Assert.Single(output[PresetComparatorWindow.LeftOnly]));
            Assert.Same(rightOnly, Assert.Single(output[PresetComparatorWindow.RightOnly]));
            Assert.Same(matchedLeft, Assert.Single(output[PresetComparatorWindow.Left]));
            Assert.Same(matchedRight, Assert.Single(output[PresetComparatorWindow.Right]));
        }

        [Fact]
        public void ProcessObjects_ExclusiveBuckets_OrderAvailableFirstThenKey() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype unavailableB = new(cache, "b-item", "b-item", subgroup, "a") { Available = false };
            ItemPrototype availableA = new(cache, "a-item", "a-item", subgroup, "a") { Available = true };
            ItemPrototype availableZ = new(cache, "z-item", "z-item", subgroup, "a") { Available = true };

            var leftDict = new Dictionary<string, IItem> { ["b-item"] = unavailableB, ["a-item"] = availableA, ["z-item"] = availableZ };
            var rightDict = new Dictionary<string, IItem>();
            var output = new List<object>[] { [], [], [], [] };

            PresetComparatorWindow.ProcessObjects(leftDict, rightDict, output);

            Assert.Equal([availableA, availableZ, unavailableB], output[PresetComparatorWindow.LeftOnly]);
        }

        [Fact]
        public void ProcessObjects_MatchedPairs_OrderAvailableFirstThenNameOrdinal() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype lZulu = new(cache, "zulu", "zulu", subgroup, "a") { Available = true };
            ItemPrototype rZulu = new(cache, "zulu", "zulu", subgroup, "a") { Available = true };
            ItemPrototype lAlpha = new(cache, "alpha", "alpha", subgroup, "a") { Available = true };
            ItemPrototype rAlpha = new(cache, "alpha", "alpha", subgroup, "a") { Available = true };

            var leftDict = new Dictionary<string, IItem> { ["zulu"] = lZulu, ["alpha"] = lAlpha };
            var rightDict = new Dictionary<string, IItem> { ["zulu"] = rZulu, ["alpha"] = rAlpha };
            var output = new List<object>[] { [], [], [], [] };

            PresetComparatorWindow.ProcessObjects(leftDict, rightDict, output);

            Assert.Equal([lAlpha, lZulu], output[PresetComparatorWindow.Left]);
            Assert.Equal([rAlpha, rZulu], output[PresetComparatorWindow.Right]);
        }

        [Fact]
        public void ProcessMods_BucketsByModNameKey() {
            var leftMods = new Dictionary<string, string> { ["base"] = "1.0", ["shared"] = "2.0" };
            var rightMods = new Dictionary<string, string> { ["shared"] = "2.0", ["extra"] = "3.0" };
            var output = new List<object>[] { [], [], [], [] };

            PresetComparatorWindow.ProcessMods(leftMods, rightMods, output);

            Assert.Equal(["base_1.0"], output[PresetComparatorWindow.LeftOnly]);
            Assert.Equal(["shared_2.0"], output[PresetComparatorWindow.Left]);
            Assert.Equal(["shared_2.0"], output[PresetComparatorWindow.Right]);
            Assert.Equal(["extra_3.0"], output[PresetComparatorWindow.RightOnly]);
        }

        //--- Per-tab similarInternals rules --------------------------------------------------------------

        [Fact]
        public void EvaluateSimilarity_Mods_IsNameEqualityOnly() {
            (bool similar, bool names) = PresetComparatorWindow.EvaluateSimilarity(0, "base_1.0", "base_1.0", "base_1.0", "base_1.0");
            Assert.True(similar);
            Assert.True(names);

            (bool similar2, bool names2) = PresetComparatorWindow.EvaluateSimilarity(0, "base_1.0", "base_1.0", "base_2.0", "base_2.0");
            Assert.False(similar2);
            Assert.False(names2);
        }

        [Fact]
        public void EvaluateSimilarity_Items_ComparesAvailableFlagOnly() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype available = new(cache, "i", "I", subgroup, "a") { Available = true };
            ItemPrototype unavailable = new(cache, "i", "I", subgroup, "a") { Available = false };

            Assert.True(PresetComparatorWindow.EvaluateSimilarity(1, "I", available, "I", available).SimilarInternals);
            Assert.False(PresetComparatorWindow.EvaluateSimilarity(1, "I", available, "I", unavailable).SimilarInternals);
        }

        private static RecipePrototype NewRecipe(DataCache cache, SubgroupPrototype subgroup, string name, double time,
            bool available, ItemPrototype a, double aQty, ItemPrototype b, double bQty, ItemPrototype c, double cQty) {
            var recipe = new RecipePrototype(cache, name, name, subgroup, "a") { Time = time, Available = available };
            recipe.InternalOneWayAddIngredient(a, aQty);
            if (bQty > 0)
                recipe.InternalOneWayAddIngredient(b, bQty);
            recipe.InternalOneWayAddProduct(c, cQty, 0);
            return recipe;
        }

        [Fact]
        public void EvaluateSimilarity_Recipes_IdenticalAmountsAndTime_IsEqual() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype itemA = new(cache, "item-a", "Item A", subgroup, "a") { Available = true };
            ItemPrototype itemB = new(cache, "item-b", "Item B", subgroup, "a") { Available = true };
            ItemPrototype itemC = new(cache, "item-c", "Item C", subgroup, "a") { Available = true };

            RecipePrototype left = NewRecipe(cache, subgroup, "recipe-x", 1.0, true, itemA, 1, itemB, 2, itemC, 3);
            RecipePrototype right = NewRecipe(cache, subgroup, "recipe-x", 1.0, true, itemA, 1, itemB, 2, itemC, 3);

            (bool similar, bool names) = PresetComparatorWindow.EvaluateSimilarity(2, left.FriendlyName, left, right.FriendlyName, right);

            Assert.True(similar);
            Assert.True(names);
        }

        //Hand-derived: right's amounts and time are exactly 2x left's (scale = rightTime/leftTime = 2), so
        //every ingredient/product ratio lands exactly at 1.0 - similar, but the exact amounts differ, so
        //this is upstream's "1A+2B->3C is close enough to 2A+4B->6C" khaki case, not white, even though
        //both recipes carry the same name.
        [Fact]
        public void EvaluateSimilarity_Recipes_ProportionallyScaledAtDifferentTime_IsCloseEnoughNotEqual() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype itemA = new(cache, "item-a", "Item A", subgroup, "a") { Available = true };
            ItemPrototype itemB = new(cache, "item-b", "Item B", subgroup, "a") { Available = true };
            ItemPrototype itemC = new(cache, "item-c", "Item C", subgroup, "a") { Available = true };

            RecipePrototype left = NewRecipe(cache, subgroup, "recipe-x", 1.0, true, itemA, 1, itemB, 2, itemC, 3);
            RecipePrototype right = NewRecipe(cache, subgroup, "recipe-x", 2.0, true, itemA, 2, itemB, 4, itemC, 6);

            (bool similar, bool names) = PresetComparatorWindow.EvaluateSimilarity(2, left.FriendlyName, left, right.FriendlyName, right);

            Assert.True(similar);
            Assert.False(names);
        }

        //Same setup as above but right's ingredient A is 0.2% off the exact 2x scale (2.004 instead of
        //2.0) - past the 0.1% ratio tolerance, so this recipe pair reads as genuinely different (pink).
        [Fact]
        public void EvaluateSimilarity_Recipes_RatioDeviationBeyondTolerance_IsDifferent() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype itemA = new(cache, "item-a", "Item A", subgroup, "a") { Available = true };
            ItemPrototype itemB = new(cache, "item-b", "Item B", subgroup, "a") { Available = true };
            ItemPrototype itemC = new(cache, "item-c", "Item C", subgroup, "a") { Available = true };

            RecipePrototype left = NewRecipe(cache, subgroup, "recipe-x", 1.0, true, itemA, 1, itemB, 2, itemC, 3);
            RecipePrototype right = NewRecipe(cache, subgroup, "recipe-x", 2.0, true, itemA, 2.004, itemB, 4, itemC, 6);

            Assert.False(PresetComparatorWindow.EvaluateSimilarity(2, left.FriendlyName, left, right.FriendlyName, right).SimilarInternals);
        }

        [Fact]
        public void EvaluateSimilarity_Recipes_AvailableMismatch_IsDifferent() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype itemA = new(cache, "item-a", "Item A", subgroup, "a") { Available = true };
            ItemPrototype itemC = new(cache, "item-c", "Item C", subgroup, "a") { Available = true };

            RecipePrototype left = NewRecipe(cache, subgroup, "recipe-x", 1.0, true, itemA, 1, itemA, 0, itemC, 3);
            RecipePrototype right = NewRecipe(cache, subgroup, "recipe-x", 1.0, false, itemA, 1, itemA, 0, itemC, 3);

            Assert.False(PresetComparatorWindow.EvaluateSimilarity(2, left.FriendlyName, left, right.FriendlyName, right).SimilarInternals);
        }

        [Fact]
        public void EvaluateSimilarity_Recipes_IngredientCountMismatch_IsDifferent() {
            DataCache cache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(cache);
            ItemPrototype itemA = new(cache, "item-a", "Item A", subgroup, "a") { Available = true };
            ItemPrototype itemB = new(cache, "item-b", "Item B", subgroup, "a") { Available = true };
            ItemPrototype itemC = new(cache, "item-c", "Item C", subgroup, "a") { Available = true };

            RecipePrototype left = NewRecipe(cache, subgroup, "recipe-x", 1.0, true, itemA, 1, itemB, 2, itemC, 3);
            RecipePrototype right = NewRecipe(cache, subgroup, "recipe-x", 1.0, true, itemA, 1, itemA, 0, itemC, 3);

            Assert.False(PresetComparatorWindow.EvaluateSimilarity(2, left.FriendlyName, left, right.FriendlyName, right).SimilarInternals);
        }

        //Divergence (docs/upstream-divergences.md): assemblers/miners/power always report
        //similarInternals=true upstream ("//QUALITY UPDATE REQUIRED"); ported as-is, so wildly different
        //ModuleSlots still reads as similar for all three tab indices.
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void EvaluateSimilarity_AssemblersMinersPower_AlwaysSimilar_StubPortedAsIs(int tabIndex) {
            DataCache cache = NewEmptyCache();
            var left = new AssemblerPrototype(cache, "asm", "Asm", EntityType.Assembler, EnergySource.Electric) { ModuleSlots = 2 };
            var right = new AssemblerPrototype(cache, "asm", "Asm", EntityType.Assembler, EnergySource.Electric) { ModuleSlots = 9 };

            Assert.True(PresetComparatorWindow.EvaluateSimilarity(tabIndex, "Asm", left, "Asm", right).SimilarInternals);
        }

        [Fact]
        public void EvaluateSimilarity_Beacons_ComparesModuleSlots() {
            DataCache cache = NewEmptyCache();
            var left = new BeaconPrototype(cache, "beacon", "Beacon", EnergySource.Electric) { ModuleSlots = 2 };
            var sameSlots = new BeaconPrototype(cache, "beacon", "Beacon", EnergySource.Electric) { ModuleSlots = 2 };
            var diffSlots = new BeaconPrototype(cache, "beacon", "Beacon", EnergySource.Electric) { ModuleSlots = 3 };

            Assert.True(PresetComparatorWindow.EvaluateSimilarity(6, "Beacon", left, "Beacon", sameSlots).SimilarInternals);
            Assert.False(PresetComparatorWindow.EvaluateSimilarity(6, "Beacon", left, "Beacon", diffSlots).SimilarInternals);
        }

        //Pollution is deliberately excluded from the comparison, matching upstream's comment.
        [Fact]
        public void EvaluateSimilarity_Modules_ComparesFourBonusesButNotPollution() {
            DataCache cache = NewEmptyCache();
            var left = new ModulePrototype(cache, "mod", "Mod") { ProductivityBonus = 0.1, SpeedBonus = 0.2, ConsumptionBonus = -0.1, QualityBonus = 0.05, PollutionBonus = 0.3 };
            var pollutionDiffers = new ModulePrototype(cache, "mod", "Mod") { ProductivityBonus = 0.1, SpeedBonus = 0.2, ConsumptionBonus = -0.1, QualityBonus = 0.05, PollutionBonus = 0.9 };
            var productivityDiffers = new ModulePrototype(cache, "mod", "Mod") { ProductivityBonus = 0.2, SpeedBonus = 0.2, ConsumptionBonus = -0.1, QualityBonus = 0.05, PollutionBonus = 0.3 };

            Assert.True(PresetComparatorWindow.EvaluateSimilarity(7, "Mod", left, "Mod", pollutionDiffers).SimilarInternals);
            Assert.False(PresetComparatorWindow.EvaluateSimilarity(7, "Mod", left, "Mod", productivityDiffers).SimilarInternals);
        }

        //--- Window ------------------------------------------------------------------------------------------

        private static DataStore NewStore() => new();

        private sealed class DataStore {
            public readonly DataCache Left = NewEmptyCache();
            public readonly DataCache Right = NewEmptyCache();
            private readonly SubgroupPrototype leftGroup;
            private readonly SubgroupPrototype rightGroup;

            public DataStore() {
                leftGroup = NewSubgroup(Left);
                rightGroup = NewSubgroup(Right);
            }

            public DataStore Item(string name, string? leftName, bool leftAvailable, string? rightName, bool rightAvailable) {
                if (leftName is not null)
                    StoreOf(Left).Items[name] = new ItemPrototype(Left, name, leftName, leftGroup, "a") { Available = leftAvailable };
                if (rightName is not null)
                    StoreOf(Right).Items[name] = new ItemPrototype(Right, name, rightName, rightGroup, "a") { Available = rightAvailable };
                return this;
            }

            public DataStore Recipe(string name, string? leftName, string? rightName) {
                if (leftName is not null)
                    StoreOf(Left).Recipes[name] = new RecipePrototype(Left, name, leftName, leftGroup, "a") { Available = true };
                if (rightName is not null)
                    StoreOf(Right).Recipes[name] = new RecipePrototype(Right, name, rightName, rightGroup, "a") { Available = true };
                return this;
            }
        }

        private static DataCacheStore StoreOf(DataCache cache) =>
            (DataCacheStore)typeof(DataCache).GetField("_store", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(cache)!;

        private static readonly Preset LeftPreset = new("Left preset", true, true);
        private static readonly Preset RightPreset = new("Right preset", false, false);

        private static PresetComparatorWindow NewWindow(DataStore store) => new([LeftPreset, RightPreset]) {
            LoadCacheStub = preset => Task.FromResult(new DataLoadResult(preset.Name == LeftPreset.Name ? store.Left : store.Right, DataLoadOutcome.Loaded)),
        };

        private static async Task<PresetComparatorWindow> ComparedAsync(DataStore store) {
            PresetComparatorWindow window = NewWindow(store);
            window.Show();
            await window.SimulateCompareClickAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static DataStore StatusStore() => NewStore()
            .Item("same", "Same", true, "Same", true)
            .Item("renamed", "Renamed", true, "Renamed plate", true)
            .Item("changed", "Changed", true, "Changed", false)
            .Item("gone", "Gone", false, null, false)
            .Item("new", null, false, "New", true);

        private static PresetComparatorWindow NewWindowWithStubbedCaches(DataCache vanilla, DataCache spaceAge) {
            var presets = new List<Preset> { new(VanillaPresetName, true, true), new(SpaceAgePresetName, false, false) };
            return new PresetComparatorWindow(presets) {
                LoadCacheStub = preset => Task.FromResult(new DataLoadResult(preset.Name == VanillaPresetName ? vanilla : spaceAge, DataLoadOutcome.Loaded)),
            };
        }

        private static string[] Texts(Visual root) =>
            [.. root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0)];

        private static Color TokenColor(string key) {
            Assert.True(Avalonia.Application.Current!.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out object? value), key);
            return Assert.IsType<Color>(value);
        }

        private static readonly string[] StatusKeys = ["StatusForeground", "StatusWarningBackground"];
        private static readonly ThemeMode[] Themes = [ThemeMode.Light, ThemeMode.Dark];
        private static readonly string[] RetiredStrings = ["Close", "Filter:", "<-- vs -->", "Read Presets And Compare", "Preset Selection", "Show Unavailables", "Preset Comparer"];

        private static ListBoxItem ContainerFor(ListBox list, Func<ComparatorPairRow, bool> match) =>
            list.GetVisualDescendants().OfType<ListBoxItem>().Single(c => c.DataContext is ComparatorPairRow row && match(row));

        private static List<AutomationPeer> PeerTree(AutomationPeer root) {
            var all = new List<AutomationPeer>();
            void Walk(AutomationPeer peer) {
                all.Add(peer);
                foreach (AutomationPeer child in peer.GetChildren())
                    Walk(child);
            }
            Walk(root);
            return all;
        }

        [AvaloniaFact]
        public async Task Comparator_Window_TitleMinSizeCopyStatePanelsAndEscape_AreAsRuled() {
            (DataStore Store, int Tab, string Filter, string Expected)[] bothStates = [
                (NewStore().Item("same", "Same", true, "Same", true), 1, "", "Nothing differs"),
                (NewStore().Item("left", "Left", true, null, false), 6, "", "None"),
                (StatusStore(), 1, "zzz", "No matches"),
            ];
            foreach ((DataStore store, int tab, string filter, string expected) in bothStates) {
                PresetComparatorWindow compared = await ComparedAsync(store);
                compared.ComparisonTabStripControl.SelectedIndex = tab;
                compared.FilterFieldControl.Text = filter;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                TextBlock bothNoResults = compared.FindControl<TextBlock>("BothNoResults")!;
                Assert.Empty(compared.BothListControl.ItemsSource!.Cast<object>());
                Assert.True(bothNoResults.IsVisible, expected);
                Assert.Equal(expected, bothNoResults.Text);
                compared.Close();
            }

            var window = new PresetComparatorWindow([LeftPreset, RightPreset]);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("Preset Comparator", window.Title);
            Assert.Equal(720, window.MinWidth);
            Assert.Equal(480, window.MinHeight);
            Assert.Empty(window.GetVisualDescendants().OfType<TabControl>());
            string[] texts = Texts(window);
            foreach (string retired in RetiredStrings)
                Assert.DoesNotContain(retired, texts);
            Assert.DoesNotContain(texts, t => t.EndsWith(':'));
            Assert.Contains("Compare", texts);
            Assert.Contains("with", texts);
            Assert.Equal("Filter", window.FilterFieldControl.PlaceholderText);
            Assert.Equal("Show unavailable", window.ShowUnavailableCheckBoxControl.Content);
            Assert.True(window.HideIdenticalCheckBoxControl.IsChecked);
            Assert.False(window.HideSimilarCheckBoxControl.IsChecked);
            Assert.False(window.ShowUnavailableCheckBoxControl.IsChecked);

            Assert.True(window.CompareButtonControl.IsDefault);
            Assert.True(window.CompareButtonControl.IsEnabled);
            StatePanel empty = window.EmptyPanelControl;
            Assert.Equal(StatePanelShape.Empty, empty.Shape);
            Assert.True(empty.IsVisible);
            Assert.False(window.GroupsGridControl.IsVisible);
            Assert.Equal("Choose two presets to compare", empty.Headline);
            Assert.Equal("Pick a preset on each side, then click Compare. Each preset takes a few seconds to load.", empty.Sentence);

            window.RightPresetPopUpControl.SelectedItem = LeftPreset.Name;

            Assert.False(window.CompareButtonControl.IsEnabled);
            Assert.Equal("Choose a different preset on either side.", ToolTip.GetTip(window.CompareButtonControl));
            Assert.Equal("Both sides name the same preset. Choose a different one on either side.", empty.Sentence);

            bool closed = false;
            window.Closed += (_, _) => closed = true;
            window.FilterFieldControl.Focus();
            window.FilterFieldControl.Text = "plate";
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(closed);
            Assert.True(string.IsNullOrEmpty(window.FilterFieldControl.Text));
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(closed);
        }

        [AvaloniaFact]
        public void Comparator_Tabs_AreToolbarTabs_WithCategoryGlyphs_OpeningOnItems_AndCommandDigitsSelect() {
            var window = new PresetComparatorWindow([]);
            window.Show();
            TabStrip strip = window.ComparisonTabStripControl;
            string[] names = ["Mods", "Items", "Recipes", "Assemblers", "Miners", "Power", "Beacons", "Modules"];
            string[] glyphs = ["Category.Mods", "Category.Items", "Category.Recipes", "Category.Assemblers", "Category.Miners", "Category.Power", "Beacon", "Category.Modules"];

            Assert.Contains("toolbar-tabs", strip.Classes);
            Assert.Equal(names, strip.Items.Select(t => t.Text));
            for (int i = 0; i < names.Length; i++) {
                Assert.Same(window.FindResource($"Kit.Glyph.{glyphs[i]}.18"), strip.Items[i].Glyph);
                Assert.Equal($"{names[i]} \u2014 \u2318{i + 1}", strip.Items[i].ToolTip);
            }
            Assert.Same(window.FindResource("Kit.Glyph.Beacon.Fill.18"), strip.Items[6].GlyphFill);
            Assert.Equal(1, strip.SelectedIndex);

            window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.Meta);

            Assert.Equal(2, strip.SelectedIndex);
        }

        [AvaloniaFact]
        public async Task Comparator_AfterCompare_GroupsRowsAndPairedHeaderFollowTheRuledGeometry_AndReadAsATable() {
            PresetComparatorWindow window = await ComparedAsync(StatusStore());
            window.HideIdenticalCheckBoxControl.IsChecked = false;
            window.ShowUnavailableCheckBoxControl.IsChecked = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.False(window.EmptyPanelControl.IsVisible);
            string[] texts = Texts(window);
            Assert.Contains("Only in Left preset", texts);
            Assert.Contains("In both", texts);
            Assert.Contains("Only in Right preset", texts);
            Assert.Contains("Match", texts);
            DockPanel[] groups = [.. window.GroupsGridControl.Children.OfType<DockPanel>()];
            Assert.Equal(groups[0].Bounds.Width * 2, groups[1].Bounds.Width, 1.5);
            Assert.Equal(groups[0].Bounds.Width, groups[2].Bounds.Width, 1);
            Assert.Equal(12, groups[1].Bounds.Left - groups[0].Bounds.Right, 0.5);
            Assert.Equal(12, groups[2].Bounds.Left - groups[1].Bounds.Right, 0.5);
            Assert.Equal(3, window.BothListControl.ItemsSource!.Cast<ComparatorPairRow>().Count(r => !r.Left.FilterKey.StartsWith("§§", StringComparison.Ordinal)));
            Assert.All(window.BothListControl.GetVisualDescendants().OfType<ListBoxItem>(), c => Assert.Equal(26, c.Bounds.Height));
            Assert.Equal("Identical", ((ComparatorPairRow)ContainerFor(window.BothListControl, r => r.Left.Name == "Same").DataContext!).MatchText);

            foreach (ListBox list in new[] { window.LeftOnlyListControl, window.BothListControl, window.RightOnlyListControl }) {
                ListBoxItem row = list.GetVisualDescendants().OfType<ListBoxItem>().First();
                Image icon = row.GetVisualDescendants().OfType<Image>().First();
                Assert.Equal(8, icon.TranslatePoint(default, row)!.Value.X, 0.5);
            }
            Border frame = window.FindControl<Border>("BothFrame")!;
            double InsideFrame(Visual v) => v.TranslatePoint(default, frame)!.Value.X - frame.BorderThickness.Left;
            Image[] cells = [.. ContainerFor(window.BothListControl, r => r.Left.Name == "Changed").GetVisualDescendants().OfType<Image>()];
            Assert.Equal(10, InsideFrame(HeaderCell(window, "PART_LeftHeader")), 0.5);
            Assert.Equal(10, InsideFrame(cells[0]), 0.5);
            Assert.Equal(InsideFrame(cells[1]), InsideFrame(HeaderCell(window, "PART_RightHeader")), 0.5);

            ComparatorPairRow similarRow = window.BothListControl.ItemsSource!.Cast<ComparatorPairRow>().Single(r => r.Similarity == PresetComparatorWindow.RowSimilarity.CloseEnough);
            window.BothListControl.SelectedItem = similarRow;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            ListBoxItem selected = ContainerFor(window.BothListControl, r => r == similarRow);
            Assert.Equal(new CornerRadius(4), selected.CornerRadius);
            Assert.Equal(new CornerRadius(4), selected.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame").CornerRadius);
            Assert.Equal(TokenColor("Foreman.Color.AccentSoft"), ((ISolidColorBrush)selected.Background!).Color);

            AutomationPeer table = ControlAutomationPeer.CreatePeerForElement(window.BothListControl);
            Assert.Equal(AutomationControlType.Table, table.GetAutomationControlType());
            AutomationPeer header = table.GetChildren()[0];
            Assert.Equal(AutomationControlType.Header, header.GetAutomationControlType());
            Assert.Equal(["Left preset", "Right preset", "Match"],
                header.GetChildren().Where(p => p.GetAutomationControlType() == AutomationControlType.HeaderItem).Select(p => p.GetName()));
            ListBoxItem gone = window.LeftOnlyListControl.GetVisualDescendants().OfType<ListBoxItem>().Single(c => c.DataContext is ComparatorRow { Name: "Gone" });
            Assert.EndsWith("unavailable", ControlAutomationPeer.CreatePeerForElement(gone).GetName());

            List<AutomationPeer> tree = PeerTree(ControlAutomationPeer.CreatePeerForElement(window));
            AutomationPeer onlyHeader = Assert.Single(tree, p => p.GetAutomationControlType() == AutomationControlType.Header);
            Assert.Same(onlyHeader, table.GetChildren()[0]);
            Assert.Same(table, onlyHeader.GetParent());
            Assert.True(onlyHeader.IsControlElement());
            foreach (DockPanel group in groups) {
                AutomationPeer groupPeer = ControlAutomationPeer.CreatePeerForElement(group);
                Assert.Contains(groupPeer, tree);
                Assert.Equal(AutomationControlType.Group, groupPeer.GetAutomationControlType());
                Assert.True(groupPeer.IsControlElement());
            }
            window.Close();

            System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            try {
                PresetComparatorWindow counted = await ComparedAsync(CountStore());
                counted.ComparisonTabStripControl.SelectedIndex = 2;
                counted.HideIdenticalCheckBoxControl.IsChecked = false;
                counted.FilterFieldControl.Text = "keep";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(["Only in Left preset, 1 recipe", "In both, 2 recipes", "Only in Right preset, 0 recipes"], GroupNames(counted));
                counted.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Meta);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(["Only in Left preset, 1 item", "In both, 2 items", "Only in Right preset, 0 items"], GroupNames(counted));
                counted.FilterFieldControl.Text = "";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                string[] shown = [.. CountNames.Select(n => counted.FindControl<TextBlock>(n)!.Text!)];
                Assert.Equal(["1", counted.BothListControl.ItemsSource!.Cast<object>().Count().ToString("N0", System.Globalization.CultureInfo.CurrentCulture), "1"], shown);
                Assert.Matches(@"^1,0\d\d$", shown[1]);
                Assert.Equal(["Only in Left preset, 1 item", $"In both, {shown[1]} items", "Only in Right preset, 1 item"], GroupNames(counted));
                counted.Close();
            } finally {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        private static TextBlock HeaderCell(PresetComparatorWindow window, string part) =>
            window.BothListControl.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == part);

        private static string[] GroupNames(PresetComparatorWindow window) =>
            [.. window.GroupsGridControl.Children.OfType<DockPanel>().Select(g => ControlAutomationPeer.CreatePeerForElement(g).GetName() ?? "")];

        private static readonly string[] CountNames = ["LeftOnlyCount", "BothCount", "RightOnlyCount"];

        private static DataStore CountStore() {
            DataStore store = NewStore();
            foreach ((string key, string name, bool left, bool right) in new[] {
                ("keep1", "Keep one", true, false), ("keep2", "Keep two", true, true), ("keep3", "Keep three", true, true),
                ("drop4", "Drop four", true, true), ("drop5", "Drop five", false, true) }) {
                store.Item(key, left ? name : null, true, right ? name : null, true);
                store.Recipe(key, left ? name : null, right ? name : null);
            }
            for (int i = 0; i < 1000; i++)
                store.Item($"bulk{i:D4}", $"Bulk {i}", true, $"Bulk {i}", true);
            return store;
        }

        [AvaloniaFact]
        public async Task Comparator_Rows_ResolveTheSemanticStatusTokens_InBothThemes() {
            var app = (App)Avalonia.Application.Current!;
            try {
                foreach (ThemeMode mode in Themes) {
                    app.ApplyTheme(mode);
                    PresetComparatorWindow window = await ComparedAsync(StatusStore().Item("flipped", "Flipped", false, "Flipped", true));
                    window.ShowUnavailableCheckBoxControl.IsChecked = true;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                    ListBoxItem similar = ContainerFor(window.BothListControl, r => r.Similarity == PresetComparatorWindow.RowSimilarity.CloseEnough);
                    ListBoxItem different = ContainerFor(window.BothListControl, r => r.Left.Name == "Changed");
                    ListBoxItem flipped = ContainerFor(window.BothListControl, r => r.Left.Name == "Flipped");
                    Assert.Equal("Changed, Changed, unavailable, different", ControlAutomationPeer.CreatePeerForElement(different).GetName());
                    Assert.Equal("Flipped, unavailable, Flipped, different", ControlAutomationPeer.CreatePeerForElement(flipped).GetName());
                    Assert.EndsWith("unavailable in this preset", ((ComparatorPairRow)different.DataContext!).Right.Tooltip);
                    Assert.Equal(TokenColor("Foreman.Color.ErrorSoft"), ((ISolidColorBrush)flipped.Background!).Color);
                    Assert.Equal(TokenColor("Foreman.Color.WarningSoft"), ((ISolidColorBrush)similar.Background!).Color);
                    Assert.Equal("Similar", ((ComparatorPairRow)similar.DataContext!).MatchText);
                    Assert.Equal(TokenColor("Foreman.Color.ErrorSoft"), ((ISolidColorBrush)different.Background!).Color);
                    Assert.Equal("Different", ((ComparatorPairRow)different.DataContext!).MatchText);

                    TextBlock gone = window.LeftOnlyListControl.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Gone");
                    Assert.Equal(TokenColor("Foreman.Color.TextTertiary"), ((ISolidColorBrush)gone.Foreground!).Color);
                    Assert.Equal(FontStyle.Italic, gone.FontStyle);
                    foreach ((ListBoxItem pair, int unavailableSide) in new[] { (different, 1), (flipped, 0) }) {
                        TextBlock[] names = [.. pair.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("comparator-name"))];
                        Assert.Equal(TokenColor("Foreman.Color.Text"), ((ISolidColorBrush)names[1 - unavailableSide].Foreground!).Color);
                        Assert.Equal(FontStyle.Normal, names[1 - unavailableSide].FontStyle);
                        Assert.Equal(TokenColor("Foreman.Color.TextTertiary"), ((ISolidColorBrush)names[unavailableSide].Foreground!).Color);
                        Assert.Equal(FontStyle.Italic, names[unavailableSide].FontStyle);
                    }

                    Color[] status = [.. StatusKeys.Select(k => TokenColor("Foreman.Color." + k))];
                    var offenders = new List<string>();
                    foreach (Visual v in window.GetVisualDescendants()) {
                        IBrush?[] own = v switch {
                            TextBlock t => [t.Foreground, t.Background],
                            TemplatedControl c => [c.Background],
                            Border b => [b.Background, b.BorderBrush],
                            Panel p => [p.Background],
                            _ => [],
                        };
                        if (own.OfType<ISolidColorBrush>().Any(b => status.Contains(b.Color)))
                            offenders.Add($"{v.GetType().Name}#{(v as Control)?.Name}<{v.GetVisualParent()?.GetType().Name}>");
                    }
                    Assert.Empty(offenders);
                    window.Close();
                }
            } finally {
                app.ApplyTheme(ThemeMode.Light);
            }
        }

        [AvaloniaFact]
        public async Task Comparator_RealTabClicks_RepaintTheRows_AndAFilterThatEmptiesAListShowsNoMatches() {
            (DataCache vanilla, DataCache spaceAge) = await GetCachesAsync();
            PresetComparatorWindow window = NewWindowWithStubbedCaches(vanilla, spaceAge);
            window.Show();
            await window.SimulateCompareClickAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            ClickTab(window, "Mods");
            string[] modRows = [.. window.RightOnlyListControl.ItemsSource!.Cast<ComparatorRow>().Select(r => r.Name)];
            Assert.NotEmpty(modRows);

            ClickTab(window, "Recipes");
            List<string> realized = [.. window.RightOnlyListControl.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];
            Assert.NotEmpty(realized);
            Assert.DoesNotContain(realized, modRows.Contains);

            SheetFixture.Click(window, window.FilterFieldControl);
            window.KeyTextInput("zzz-does-not-match-anything-zzz");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.LeftOnlyListControl.ItemsSource!.Cast<object>());
            Assert.True(window.LeftOnlyNoResultsControl.IsVisible);
            Assert.Equal("No matches", window.LeftOnlyNoResultsControl.Text);
        }

        [AvaloniaFact]
        public async Task Comparator_ChooseAnotherOnEitherSide_FocusesAndOpensThatSidesPopUp_KeepingTheOldResults() {
            DataStore store = StatusStore();
            PresetComparatorWindow window = await ComparedAsync(store);
            object[] rowsBefore = [.. window.BothListControl.ItemsSource!.Cast<object>()];
            Assert.NotEmpty(rowsBefore);
            DataCache freshLeft = NewEmptyCache();
            window.LoadCacheStub = preset => Task.FromResult(preset.Name == LeftPreset.Name
                ? new DataLoadResult(freshLeft, DataLoadOutcome.Loaded)
                : new DataLoadResult(null, DataLoadOutcome.ChooseAnother));

            await window.SimulateCompareClickAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(window.Compared);
            string[] texts = Texts(window);
            Assert.Contains("Only in Left preset", texts);
            Assert.Contains("Only in Right preset", texts);
            Assert.Equal(rowsBefore, window.BothListControl.ItemsSource!.Cast<object>());
            Assert.Same(store.Left, window.LeftCache);
            Assert.Same(store.Right, window.RightCache);
            Assert.True(window.RightPresetPopUpControl.IsFocused);
            Assert.True(window.RightPresetPopUpControl.IsOpen);
            window.RightPresetPopUpControl.IsOpen = false;

            window.LoadCacheStub = preset => Task.FromResult(preset.Name == LeftPreset.Name
                ? new DataLoadResult(null, DataLoadOutcome.ChooseAnother)
                : new DataLoadResult(NewEmptyCache(), DataLoadOutcome.Loaded));
            await window.SimulateCompareClickAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(window.Compared);
            Assert.Equal(rowsBefore, window.BothListControl.ItemsSource!.Cast<object>());
            Assert.Same(store.Left, window.LeftCache);
            Assert.Same(store.Right, window.RightCache);
            Assert.True(window.LeftPresetPopUpControl.IsFocused);
            Assert.True(window.LeftPresetPopUpControl.IsOpen);
            Assert.False(window.RightPresetPopUpControl.IsOpen);
            window.LeftPresetPopUpControl.IsOpen = false;
            window.Close();
        }

        internal static void ClickTab(PresetComparatorWindow window, string label) {
            TextBlock text = window.ComparisonTabStripControl.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label);
            SheetFixture.Click(window, (Control)text.GetVisualParent()!);
        }
    }
}
