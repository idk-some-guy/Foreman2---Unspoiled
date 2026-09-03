using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Models;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    public class SettingsViewModelTests {
        //Round-trip contract (Task 6 Step 1): LoadFrom seeds every widget-backing property from an
        //AppSettings snapshot, ApplyTo writes them back onto a fresh snapshot - mutating a property before
        //ApplyTo must change only that field, leaving everything else exactly as LoadFrom read it.
        [Fact]
        public void RoundTrip_LoadThenApply_ReflectsOnlyTheMutatedFields() {
            var original = new AppSettings();
            var vm = new SettingsViewModel();
            vm.LoadFrom(original);

            vm.FlagDarkModeChecked = true; //bool
            vm.HighLodChecked = true; //enum-backed (LevelOfDetail)
            vm.LowPriorityPowerValue = 5.5m; //solver decimal, passes through unchanged
            vm.PullConsumerNodesPowerValue = 2.25m; //solver decimal, passes through unchanged

            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.Equal(ThemeMode.Dark, applied.FlagDarkMode);
            Assert.Equal((int)LevelOfDetail.High, applied.LevelOfDetail);
            Assert.Equal(5.5m, applied.LowPriorityPower);
            Assert.Equal(2.25m, applied.PullConsumerNodesPower);

            Assert.Equal(original.QualitySteps, applied.QualitySteps);
            Assert.Equal(original.NodeCountForSimpleView, applied.NodeCountForSimpleView);
            Assert.Equal(original.IconsSize, applied.IconsSize);
            Assert.Equal(original.ArrowsOnLinks, applied.ArrowsOnLinks);
            Assert.Equal(original.SimplePassthroughNodes, applied.SimplePassthroughNodes);
            Assert.Equal(original.DynamicLineWidth, applied.DynamicLineWidth);
            Assert.Equal(original.AbbreviateSciPacks, applied.AbbreviateSciPacks);
            Assert.Equal(original.ShowRecipeToolTip, applied.ShowRecipeToolTip);
            Assert.Equal(original.RoundAssemblerCount, applied.RoundAssemblerCount);
            Assert.Equal(original.FlagOUSuppliedNodes, applied.FlagOUSuppliedNodes);
            Assert.Equal(original.ShowErrorArrows, applied.ShowErrorArrows);
            Assert.Equal(original.ShowWarningArrows, applied.ShowWarningArrows);
            Assert.Equal(original.ShowDisconnectedArrows, applied.ShowDisconnectedArrows);
            Assert.Equal(original.ShowOUSuppliedArrows, applied.ShowOUSuppliedArrows);
            Assert.Equal(original.DefaultAssemblerOption, applied.DefaultAssemblerOption);
            Assert.Equal(original.DefaultModuleOption, applied.DefaultModuleOption);
            Assert.Equal(original.DefaultNodeDirection, applied.DefaultNodeDirection);
            Assert.Equal(original.SmartNodeDirection, applied.SmartNodeDirection);
            Assert.Equal(original.EnableExtraProductivityForNonMiners, applied.EnableExtraProductivityForNonMiners);
            Assert.Equal(original.ShowUnavailable, applied.ShowUnavailable);
            Assert.Equal(original.UseRecipeBWfilters, applied.UseRecipeBWfilters);
            Assert.Equal(original.PullConsumerNodes, applied.PullConsumerNodes);
        }

        [Fact]
        public void LoadFrom_LowPriorityPowerAboveWidgetMaximum_ClampsTo6() {
            var settings = new AppSettings { LowPriorityPower = 9m };
            var vm = new SettingsViewModel();

            vm.LoadFrom(settings);

            Assert.Equal(6m, vm.LowPriorityPowerValue);
        }

        [Fact]
        public void LoadFrom_PullConsumerNodesPowerAboveWidgetMaximum_ClampsTo5() {
            var settings = new AppSettings { PullConsumerNodesPower = 8m };
            var vm = new SettingsViewModel();

            vm.LoadFrom(settings);

            Assert.Equal(5m, vm.PullConsumerNodesPowerValue);
        }

        [Fact]
        public void LoadFrom_NodeCountForSimpleViewAboveWidgetMaximum_ClampsTo2000() {
            var settings = new AppSettings { NodeCountForSimpleView = 5000 };
            var vm = new SettingsViewModel();

            vm.LoadFrom(settings);

            Assert.Equal(2000m, vm.NodeCountForSimpleViewValue);
        }

        [Fact]
        public void LoadBarrelingChecked_IsTheInverseOfUseRecipeBWfilters() {
            var settings = new AppSettings { UseRecipeBWfilters = false };
            var vm = new SettingsViewModel();

            vm.LoadFrom(settings);

            Assert.True(vm.LoadBarrelingChecked);

            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.False(applied.UseRecipeBWfilters);
        }

        //Confirms the plain-Confirm bar (upstream MainForm.cs:36-46): unchecking Dark Mode must force
        //Light, never fall back to System, regardless of what LoadFrom originally read.
        [Fact]
        public void ApplyTo_DarkModeUnchecked_ForcesLightNeverSystem() {
            var settings = new AppSettings { FlagDarkMode = ThemeMode.System };
            var vm = new SettingsViewModel();
            vm.LoadFrom(settings);

            vm.FlagDarkModeChecked = false;
            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.Equal(ThemeMode.Light, applied.FlagDarkMode);
        }

        //FRESH-LIST rule: repopulating the unfiltered lists must swap in a brand new IReadOnlyList
        //instance, never a mutated-in-place one - Avalonia's ItemsSource binding short-circuits a
        //reference-equal reassignment and the bound ListBox never re-renders.
        [Fact]
        public void RefreshFilteredLists_ProducesANewListInstanceEveryCall() {
            var vm = new SettingsViewModel();
            vm.LoadUnfilteredLists([], [], [], [], [], [], []);
            IReadOnlyList<EnabledObjectsListItem> first = vm.RecipeObjects;

            vm.RefreshFilteredLists();

            Assert.NotSame(first, vm.RecipeObjects);
        }

        [Fact]
        public void LoadUnfilteredLists_FiltersOutUnavailableItemsByDefault() {
            var available = new EnabledObjectsListItem(FakeDataObject.NewAvailable(), icon: null);
            var unavailable = new EnabledObjectsListItem(FakeDataObject.NewUnavailable(), icon: null);
            var vm = new SettingsViewModel();

            vm.LoadUnfilteredLists([], [], [], [], [], [available, unavailable], []);

            Assert.Contains(available, vm.RecipeObjects);
            Assert.DoesNotContain(unavailable, vm.RecipeObjects);
        }

        [Fact]
        public void ShowUnavailablesFilterChecked_True_RevealsUnavailableItems() {
            var unavailable = new EnabledObjectsListItem(FakeDataObject.NewUnavailable(), icon: null);
            var vm = new SettingsViewModel();
            vm.LoadUnfilteredLists([], [], [], [], [], [unavailable], []);

            vm.ShowUnavailablesFilterChecked = true;

            Assert.Contains(unavailable, vm.RecipeObjects);
        }

        [Fact]
        public void FilterText_FiltersItemsByNameOrdinalIgnoreCase() {
            var ironPlate = new EnabledObjectsListItem(FakeDataObject.NewAvailable("Iron Plate"), icon: null);
            var copperPlate = new EnabledObjectsListItem(FakeDataObject.NewAvailable("Copper Plate"), icon: null);
            var vm = new SettingsViewModel();
            vm.LoadUnfilteredLists([], [], [], [], [], [ironPlate, copperPlate], []);

            vm.FilterText = "iron";

            Assert.Contains(ironPlate, vm.RecipeObjects);
            Assert.DoesNotContain(copperPlate, vm.RecipeObjects);
        }

        //Minimal IDataObjectBase stand-in - filtering only ever reads Available/FriendlyName, so every
        //other member is a stub.
        private sealed class FakeDataObject(string friendlyName, bool available) : IDataObjectBase {
            public static FakeDataObject NewAvailable(string friendlyName = "Widget") => new(friendlyName, available: true);
            public static FakeDataObject NewUnavailable(string friendlyName = "Widget") => new(friendlyName, available: false);

            public DataCache Owner => null!;
            public string Name { get; } = friendlyName;
            public string LFriendlyName { get; } = friendlyName.ToLowerInvariant();
            public string FriendlyName { get; } = friendlyName;
            public bool Available { get; } = available;
            public bool Enabled { get; set; }
            public SKBitmap Icon => null!;
            public Color AverageColor => Color.Black;
            public void SetIconAndColor(IconColorPair icp) { }
            public int CompareTo(IDataObjectBase? other) => 0;
        }
    }
}
