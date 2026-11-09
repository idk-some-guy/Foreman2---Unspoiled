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
        [Fact]
        public void RoundTrip_LoadThenApply_ReflectsOnlyTheMutatedFields() {
            var original = new AppSettings();
            var vm = new SettingsViewModel();
            vm.LoadFrom(original);

            vm.FlagDarkMode = ThemeMode.Dark;
            vm.LevelOfDetailIndex = 2;
            vm.LowPriorityPowerValue = 5.5m;
            vm.PullConsumerNodesPowerValue = 2.25m;

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
            Assert.Equal(original.DefaultLinkRouting, applied.DefaultLinkRouting);
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
            Assert.Equal(original.UiScale, applied.UiScale);
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

        [Theory]
        [InlineData(ThemeMode.System)]
        [InlineData(ThemeMode.Light)]
        [InlineData(ThemeMode.Dark)]
        public void LoadFromThenApplyTo_RoundTripsFlagDarkModeLosslessly(ThemeMode mode) {
            var settings = new AppSettings { FlagDarkMode = mode };
            var vm = new SettingsViewModel();
            vm.LoadFrom(settings);

            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.Equal(mode, applied.FlagDarkMode);
        }

        [Fact]
        public void ApplyTo_SystemLoadedThenUnrelatedSettingChanged_StaysSystem() {
            var settings = new AppSettings { FlagDarkMode = ThemeMode.System };
            var vm = new SettingsViewModel();
            vm.LoadFrom(settings);

            vm.LevelOfDetailIndex = 2;

            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.Equal(ThemeMode.System, applied.FlagDarkMode);
        }

        [Theory]
        [InlineData(1.00, "100 %")]
        [InlineData(1.10, "110 %")]
        [InlineData(1.25, "125 %")]
        [InlineData(1.50, "150 %")]
        [InlineData(2.00, "200 %")]
        public void LoadFromThenApplyTo_RoundTripsUiScaleThroughItsStepLabel(double scale, string expectedLabel) {
            var settings = new AppSettings { UiScale = scale };
            var vm = new SettingsViewModel();
            vm.LoadFrom(settings);

            Assert.Equal(expectedLabel, vm.UiScaleLabel);
            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.Equal(scale, applied.UiScale);
        }

        [Fact]
        public void ApplyTo_UiScaleLoadedThenUnrelatedSettingChanged_StaysSame() {
            var settings = new AppSettings { UiScale = 1.5 };
            var vm = new SettingsViewModel();
            vm.LoadFrom(settings);

            vm.LevelOfDetailIndex = 2;

            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.Equal(1.5, applied.UiScale);
        }

        [Fact]
        public void LoadFrom_OffListUiScale_SnapsToNearestStep() {
            var settings = new AppSettings { UiScale = 1.4 };
            var vm = new SettingsViewModel();

            vm.LoadFrom(settings);

            Assert.Equal("150 %", vm.UiScaleLabel);
        }

        [Fact]
        public void ApplyTo_UiScaleLabelNotOnList_FallsBackToOneHundredPercent() {
            var vm = new SettingsViewModel { UiScaleLabel = "not-a-real-step" };

            var applied = new AppSettings();
            vm.ApplyTo(applied);

            Assert.Equal(1.0, applied.UiScale);
        }

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

        [Fact]
        public void ShowFilterAndCategory_LimitTheVisibleListToTheChosenCategoryAndState() {
            var on = new EnabledObjectsListItem(FakeDataObject.NewAvailable("On"), icon: null) { IsChecked = true };
            var off = new EnabledObjectsListItem(FakeDataObject.NewAvailable("Off"), icon: null);
            var beacon = new EnabledObjectsListItem(FakeDataObject.NewAvailable("Beacon"), icon: null);
            var vm = new SettingsViewModel();
            vm.LoadUnfilteredLists([], [], [], [beacon], [], [on, off], []);

            Assert.Same(vm.AssemblerObjects, vm.VisibleObjects);
            vm.EnabledObjectsCategoryIndex = 5;
            Assert.Same(vm.RecipeObjects, vm.VisibleObjects);
            Assert.Equal([on, off], vm.VisibleObjects);

            vm.EnabledObjectsShowIndex = 1;
            Assert.Equal([on], vm.VisibleObjects);
            vm.EnabledObjectsShowIndex = 2;
            Assert.Equal([off], vm.VisibleObjects);

            Assert.Equal(["Assemblers (0)", "Miners (0)", "Power (0)", "Beacons (1)", "Modules (0)", "Recipes (2)", "Qualities (0)"],
                vm.EnabledObjectsCategoryLabels);
        }

        [Fact]
        public void SettingChanged_FiresWithThePropertyName_WhenAnAppSettingsBackedPropertyChanges() {
            var vm = new SettingsViewModel();
            var raised = new List<string>();
            vm.SettingChanged += raised.Add;

            vm.SmartNodeDirectionChecked = true;
            vm.FlagDarkMode = ThemeMode.Dark;

            Assert.Equal([nameof(vm.SmartNodeDirectionChecked), nameof(vm.FlagDarkMode)], raised);
        }

        [Fact]
        public void SettingChanged_DoesNotFire_ForFilterOrListProperties() {
            var vm = new SettingsViewModel();
            var raised = new List<string>();
            vm.SettingChanged += raised.Add;

            vm.FilterText = "iron";
            vm.ShowUnavailablesFilterChecked = true;
            vm.EnabledObjectsCategoryIndex = 5;
            vm.EnabledObjectsShowIndex = 2;

            Assert.Empty(raised);
        }

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
