using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

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

        //--- SyncedListPair -------------------------------------------------------------------------------

        [AvaloniaFact]
        public void SyncedListPair_ScrollingOneList_MovesBuddyToSameOffset() {
            var items = Enumerable.Range(0, 100).Select(i => "row " + i).ToList();
            var left = new ListBox { Width = 100, Height = 100, ItemsSource = items };
            var right = new ListBox { Width = 100, Height = 100, ItemsSource = items };
            var pair = new SyncedListPair(left, right);
            var panel = new StackPanel();
            panel.Children.Add(left);
            panel.Children.Add(right);
            var window = new Window { Content = panel, Width = 300, Height = 300 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.NotNull(pair.LeftScrollViewer);
            Assert.NotNull(pair.RightScrollViewer);

            pair.LeftScrollViewer!.Offset = new Vector(0, 40);

            Assert.Equal(40, pair.RightScrollViewer!.Offset.Y);
        }

        [AvaloniaFact]
        public void SyncedListPair_SelectingOneList_SelectsSameIndexInBuddy() {
            var left = new ListBox { ItemsSource = new List<string> { "a", "b", "c" } };
            var right = new ListBox { ItemsSource = new List<string> { "a", "b", "c" } };
            _ = new SyncedListPair(left, right);

            left.SelectedIndex = 1;

            Assert.Equal(1, right.SelectedIndex);
        }

        //--- Window shell -----------------------------------------------------------------------------

        //Phase5b hands-on gate (Finding 2): the human reported every tab rendering the Mods diff under real
        //clicks. The data model (ItemsSource) retargets correctly on tab change - ProcessObjects/ProcessMods
        //rebuild unfilteredSelectedTabRows and UpdateFilteredLists reassigns leftOnlyListView.ItemsSource -
        //but it reassigns the SAME List<ComparatorRow> reference every time (the 4 lists are shared across
        //tabs by design). Avalonia's StyledProperty setter short-circuits on reference-equal values, so
        //ListBox never sees a change notification and keeps its previously realized rows on screen even
        //though the underlying list's contents changed. A test that only reads ItemsSource back can't catch
        //this (it always reflects current contents); only inspecting the realized visual tree can. Real
        //clicks drive both the tab switch and the filter typing through the actual input pipeline.
        [AvaloniaFact]
        public async Task RealClickOnTabHeader_SwitchesSelectionAndRepaintsRealizedRows() {
            (DataCache vanilla, DataCache spaceAge) = await GetCachesAsync();
            PresetComparatorWindow window = NewWindowWithStubbedCaches(vanilla, spaceAge);
            window.Show();
            await window.SimulateProcessPresetsClickAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            //Space Age's own mods (space-age, quality, elevated-rails, ...) are exclusive to it, so
            //RightOnlyListView is the bucket guaranteed non-empty on the Mods tab.
            string[] modNames = [.. window.RightOnlyListViewControl.ItemsSource!.Cast<PresetComparatorWindow.ComparatorRow>().Select(r => r.Name)];
            Assert.NotEmpty(modNames);
            Assert.NotEmpty(RealizedRowTexts(window.RightOnlyListViewControl));

            TabItem itemsTab = window.ComparisonTabControlControl.Items.OfType<TabItem>().ElementAt(1);
            Avalonia.Point tabScreen = itemsTab.TranslatePoint(new Avalonia.Point(itemsTab.Bounds.Width / 2, itemsTab.Bounds.Height / 2), window)!.Value;
            window.MouseDown(tabScreen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(tabScreen, MouseButton.Left, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, window.ComparisonTabControlControl.SelectedIndex);
            List<object> itemsRows = window.RightOnlyListViewControl.ItemsSource!.Cast<object>().ToList();
            List<string> realizedAfterItemsTab = RealizedRowTexts(window.RightOnlyListViewControl);

            Assert.NotEmpty(realizedAfterItemsTab);
            Assert.DoesNotContain(realizedAfterItemsTab, text => modNames.Contains(text));
            //The virtualized panel only realizes rows near the top of the viewport, so assert against the
            //first row it actually reports rather than an arbitrarily chosen off-screen item.
            string firstItemRowName = itemsRows.Cast<PresetComparatorWindow.ComparatorRow>().First().Name;
            Assert.Contains(realizedAfterItemsTab, text => text == firstItemRowName);

            TabItem recipesTab = window.ComparisonTabControlControl.Items.OfType<TabItem>().ElementAt(2);
            Avalonia.Point recipesScreen = recipesTab.TranslatePoint(new Avalonia.Point(recipesTab.Bounds.Width / 2, recipesTab.Bounds.Height / 2), window)!.Value;
            window.MouseDown(recipesScreen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(recipesScreen, MouseButton.Left, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            List<string> realizedAfterRecipesTab = RealizedRowTexts(window.RightOnlyListViewControl);
            Assert.NotEmpty(realizedAfterRecipesTab);
            Assert.DoesNotContain(realizedAfterRecipesTab, text => realizedAfterItemsTab.Contains(text));

            Avalonia.Point filterScreen = window.FilterTextBoxControl.TranslatePoint(new Avalonia.Point(5, window.FilterTextBoxControl.Bounds.Height / 2), window)!.Value;
            window.MouseDown(filterScreen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(filterScreen, MouseButton.Left, RawInputModifiers.None);
            window.KeyTextInput("zzz-does-not-match-anything-zzz");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.RightOnlyListViewControl.ItemsSource!.Cast<object>());
            Assert.Empty(RealizedRowTexts(window.RightOnlyListViewControl));
        }

        private static List<string> RealizedRowTexts(ListBox listBox) =>
            [.. listBox.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

        [AvaloniaFact]
        public void ComparisonTabControl_HasEightTabsInOrder() {
            var window = new PresetComparatorWindow([]);

            var headers = window.ComparisonTabControlControl.Items.OfType<TabItem>().Select(t => t.Header).ToList();

            Assert.Equal(["Mods", "Items", "Recipes", "Assemblers", "Miners", "Power", "Beacons", "Modules"], headers);
        }

        [AvaloniaFact]
        public void FilterRow_HideEqualDefaultsChecked_OthersDefaultUnchecked() {
            var window = new PresetComparatorWindow([]);

            Assert.True(window.HideEqualObjectsCheckBoxControl.IsChecked);
            Assert.False(window.HideSimilarObjectsCheckBoxControl.IsChecked);
            Assert.False(window.ShowUnavailableCheckBoxControl.IsChecked);
        }

        //Imp#2 (final fix wave, upstream PresetComparatorForm.Designer.cs:605): Close is the window's
        //CancelButton, so Escape must reach it without a click.
        [AvaloniaFact]
        public void EscapeKey_ClosesWindow() {
            var window = new PresetComparatorWindow([]);
            window.Show();
            bool closed = false;
            window.Closed += (_, _) => closed = true;

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.True(closed);
        }

        //Ports PresetSelectionBox_SelectedValueChanged's tri-state caption (reference PresetComparatorForm.cs:404-408).
        [AvaloniaFact]
        public void ProcessPresetsButton_SamePresetBothSides_ShowsCantCompareCaption() {
            var presets = new List<Preset> { new("A", true, true), new("B", false, false) };
            var window = new PresetComparatorWindow(presets);

            Assert.True(window.ProcessPresetsButtonControl.IsEnabled);
            Assert.Equal("Read Presets And Compare", window.ProcessPresetsButtonControl.Content);

            window.RightPresetSelectionBoxControl.SelectedIndex = 0;

            Assert.False(window.ProcessPresetsButtonControl.IsEnabled);
            Assert.Equal("Cant Compare Preset To Itself", window.ProcessPresetsButtonControl.Content);
        }

        //--- Full pipeline (real presets, loaded once and shared/injected via LoadCacheStub) -------------

        private static PresetComparatorWindow NewWindowWithStubbedCaches(DataCache vanilla, DataCache spaceAge) {
            var presets = new List<Preset> { new(VanillaPresetName, true, true), new(SpaceAgePresetName, false, false) };
            var window = new PresetComparatorWindow(presets);
            window.LoadCacheStub = preset => Task.FromResult<DataCache?>(preset.Name == VanillaPresetName ? vanilla : spaceAge);
            return window;
        }

        [AvaloniaFact]
        public async Task ProcessPresetsButton_Click_TogglesComparingStateAndPopulatesModsTab() {
            (DataCache vanilla, DataCache spaceAge) = await GetCachesAsync();
            PresetComparatorWindow window = NewWindowWithStubbedCaches(vanilla, spaceAge);

            await window.SimulateProcessPresetsClickAsync();

            Assert.True(window.Comparing);
            Assert.Equal("Select Other Presets", window.ProcessPresetsButtonControl.Content);
            Assert.False(window.PresetSelectionGroupControl.IsEnabled);
            Assert.NotEmpty(window.RightOnlyListViewControl.ItemsSource!.Cast<object>());
        }

        [AvaloniaFact]
        public async Task HideEqualObjectsCheckBox_DefaultChecked_HidesMoreThanWhenUnchecked() {
            (DataCache vanilla, DataCache spaceAge) = await GetCachesAsync();
            PresetComparatorWindow window = NewWindowWithStubbedCaches(vanilla, spaceAge);
            await window.SimulateProcessPresetsClickAsync();

            int hiddenCount = window.LeftListViewControl.ItemsSource!.Cast<object>().Count();
            window.HideEqualObjectsCheckBoxControl.IsChecked = false;
            int shownCount = window.LeftListViewControl.ItemsSource!.Cast<object>().Count();

            Assert.True(shownCount > hiddenCount, "Unchecking Hide Equal should reveal at least the equal-version shared mods.");
        }

        //Regression: the Fluent theme's default ListBoxItem carries generous Padding/MinHeight, leaving
        //upstream's dense WinForms rows (24px icon, minimal chrome) looking sparse and hard to scan.
        [AvaloniaFact]
        public async Task ComparatorRows_AreDenseLikeUpstream() {
            (DataCache vanilla, DataCache spaceAge) = await GetCachesAsync();
            PresetComparatorWindow window = NewWindowWithStubbedCaches(vanilla, spaceAge);
            window.Show();
            await window.SimulateProcessPresetsClickAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            ListBoxItem realizedRow = window.RightOnlyListViewControl.GetVisualDescendants().OfType<ListBoxItem>().First();
            Assert.True(realizedRow.Bounds.Height <= 28, $"Expected a dense row (<=28px), got {realizedRow.Bounds.Height}px.");
        }

        [AvaloniaFact]
        public async Task ProcessPresetsButton_Click_AgainReturnsToSelectingAndClearsLists() {
            //Uses throwaway caches (not the class-shared vanilla/spaceAge ones): toggling back to
            //Selecting calls DataCache.Clear() on whatever LeftCache/RightCache hold, and the shared
            //caches must stay intact for every other test in this class.
            PresetComparatorWindow window = NewWindowWithStubbedCaches(NewEmptyCache(), NewEmptyCache());
            await window.SimulateProcessPresetsClickAsync();

            await window.SimulateProcessPresetsClickAsync();

            Assert.False(window.Comparing);
            Assert.Equal("Read Presets And Compare", window.ProcessPresetsButtonControl.Content);
            Assert.True(window.PresetSelectionGroupControl.IsEnabled);
            Assert.Null(window.LeftOnlyListViewControl.ItemsSource);
        }
    }
}
