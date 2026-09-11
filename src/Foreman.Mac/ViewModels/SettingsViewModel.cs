using CommunityToolkit.Mvvm.ComponentModel;
using Foreman;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    //Settings window's Graph Options tab and Enabled Objects lists, split off SettingsWindow.axaml.cs's
    //former LoadGraphOptionsTab/CommitGraphOptionsTab/UpdateFilteredLists (Task 6). Presets-tab
    //orchestration (window services: dialogs, preset-list rebuilds) stays in the code-behind.
    public sealed partial class SettingsViewModel : ObservableObject {
        //R5 live apply: every AppSettings-backed property raises this on change so SettingsSession can
        //react (theme/scale/resolve/save). The Enabled Objects tab's own search/filter state isn't
        //AppSettings-backed, so it's excluded rather than spamming a session with nothing to apply.
        private static readonly HashSet<string> ExcludedFromSettingChanged = [
            nameof(FilterText), nameof(ShowUnavailablesFilterChecked),
            nameof(AssemblerObjects), nameof(MinerObjects), nameof(PowerObjects), nameof(BeaconObjects),
            nameof(ModuleObjects), nameof(RecipeObjects), nameof(QualityObjects),
        ];

        public event Action<string>? SettingChanged;

        protected override void OnPropertyChanged(PropertyChangedEventArgs e) {
            base.OnPropertyChanged(e);
            if (e.PropertyName is string name && !ExcludedFromSettingChanged.Contains(name))
                SettingChanged?.Invoke(name);
        }

        [ObservableProperty]
        private decimal? qualityStepsValue;

        [ObservableProperty]
        private bool lowLodChecked;

        [ObservableProperty]
        private bool mediumLodChecked;

        [ObservableProperty]
        private bool highLodChecked;

        //Mutual exclusion normally comes free from the RadioButtons sharing a GroupName in XAML - these
        //hooks keep the VM internally consistent even when a property is set directly (tests, or a future
        //caller that isn't a bound RadioButton).
        partial void OnLowLodCheckedChanged(bool value) {
            if (value) {
                MediumLodChecked = false;
                HighLodChecked = false;
            }
        }

        partial void OnMediumLodCheckedChanged(bool value) {
            if (value) {
                LowLodChecked = false;
                HighLodChecked = false;
            }
        }

        partial void OnHighLodCheckedChanged(bool value) {
            if (value) {
                LowLodChecked = false;
                MediumLodChecked = false;
            }
        }

        [ObservableProperty]
        private decimal? nodeCountForSimpleViewValue;

        [ObservableProperty]
        private decimal? iconsSizeValue;

        [ObservableProperty]
        private bool arrowsOnLinksChecked;

        [ObservableProperty]
        private bool dynamicLWChecked;

        [ObservableProperty]
        private bool abbreviateSciPackChecked;

        [ObservableProperty]
        private bool showNodeRecipeChecked;

        [ObservableProperty]
        private bool roundAssemblerCountChecked;

        [ObservableProperty]
        private bool flagOUSupplyNodesChecked;

        [ObservableProperty]
        private ThemeMode flagDarkMode;

        [ObservableProperty]
        private bool errorArrowsChecked;

        [ObservableProperty]
        private bool warningArrowsChecked;

        [ObservableProperty]
        private bool disconnectedArrowsChecked;

        [ObservableProperty]
        private bool ouSuppliedArrowsChecked;

        [ObservableProperty]
        private int assemblerSelectorStyleIndex;

        [ObservableProperty]
        private int moduleSelectorStyleIndex;

        [ObservableProperty]
        private int nodeDirectionIndex;

        [ObservableProperty]
        private bool smartNodeDirectionChecked;

        [ObservableProperty]
        private bool simplePassthroughNodesChecked;

        [ObservableProperty]
        private bool showProductivityBonusOnAllChecked;

        [ObservableProperty]
        private bool showUnavailablesChecked;

        [ObservableProperty]
        private bool loadBarrelingChecked;

        [ObservableProperty]
        private decimal? lowPriorityPowerValue;

        [ObservableProperty]
        private bool pullConsumerNodesChecked;

        [ObservableProperty]
        private decimal? pullConsumerNodesPowerValue;

        //UI Scale ComboBox (Task 3): discrete steps only, so the widget carries a formatted label rather
        //than the raw double - keeps the XAML side identical in shape to FlagDarkModeDropDown's plain
        //ItemsSource/SelectedItem binding.
        private static readonly (string Label, double Scale)[] UiScaleSteps = [
            ("100%", 1.00), ("110%", 1.10), ("125%", 1.25), ("150%", 1.50), ("200%", 2.00),
        ];

        public static IReadOnlyList<string> UiScaleLabels { get; } = [.. UiScaleSteps.Select(s => s.Label)];

        [ObservableProperty]
        private string uiScaleLabel = "100%";

        //Ports LoadGraphOptionsTab (reference SettingsWindow.axaml.cs): seeds every widget-backing
        //property from an AppSettings snapshot. NodeCountForSimpleView/LowPriorityPower/
        //PullConsumerNodesPower keep the widget-Maximum clamp their NumericUpDown counterparts carry in
        //XAML (2000/6/5) - a stale value above the visible range would otherwise show past it.
        public void LoadFrom(AppSettings settings) {
            QualityStepsValue = settings.QualitySteps;
            LowLodChecked = settings.LevelOfDetail == (int)LevelOfDetail.Low;
            HighLodChecked = settings.LevelOfDetail == (int)LevelOfDetail.High;
            MediumLodChecked = !LowLodChecked && !HighLodChecked;
            NodeCountForSimpleViewValue = Math.Min(2000m, settings.NodeCountForSimpleView);
            IconsSizeValue = settings.IconsSize;

            ArrowsOnLinksChecked = settings.ArrowsOnLinks;
            SimplePassthroughNodesChecked = settings.SimplePassthroughNodes;
            DynamicLWChecked = settings.DynamicLineWidth;
            AbbreviateSciPackChecked = settings.AbbreviateSciPacks;
            ShowNodeRecipeChecked = settings.ShowRecipeToolTip;
            RoundAssemblerCountChecked = settings.RoundAssemblerCount;
            FlagOUSupplyNodesChecked = settings.FlagOUSuppliedNodes;
            FlagDarkMode = settings.FlagDarkMode;

            ErrorArrowsChecked = settings.ShowErrorArrows;
            WarningArrowsChecked = settings.ShowWarningArrows;
            DisconnectedArrowsChecked = settings.ShowDisconnectedArrows;
            OuSuppliedArrowsChecked = settings.ShowOUSuppliedArrows;

            AssemblerSelectorStyleIndex = (int)settings.DefaultAssemblerOption;
            ModuleSelectorStyleIndex = (int)settings.DefaultModuleOption;
            NodeDirectionIndex = settings.DefaultNodeDirection == NodeDirection.Down ? 1 : 0;
            SmartNodeDirectionChecked = settings.SmartNodeDirection;

            ShowProductivityBonusOnAllChecked = settings.EnableExtraProductivityForNonMiners;
            ShowUnavailablesChecked = settings.ShowUnavailable;
            LoadBarrelingChecked = !settings.UseRecipeBWfilters;

            LowPriorityPowerValue = Math.Min(6m, settings.LowPriorityPower);
            PullConsumerNodesChecked = settings.PullConsumerNodes;
            PullConsumerNodesPowerValue = Math.Min(5m, settings.PullConsumerNodesPower);

            UiScaleLabel = UiScaleSteps.OrderBy(step => Math.Abs(step.Scale - settings.UiScale)).First().Label;
        }

        //Ports CommitGraphOptionsTab (reference SettingsWindow.axaml.cs). FlagDarkMode round-trips
        //losslessly through the tri-state ComboBox - unlike upstream's strictly binary SetDarkMode/
        //SetLightMode pair, System survives a Confirm that never touched appearance.
        public void ApplyTo(AppSettings settings) {
            settings.QualitySteps = (int)QualityStepsValue.GetValueOrDefault();
            settings.LevelOfDetail = (int)(LowLodChecked ? LevelOfDetail.Low : HighLodChecked ? LevelOfDetail.High : LevelOfDetail.Medium);
            settings.NodeCountForSimpleView = (int)NodeCountForSimpleViewValue.GetValueOrDefault();
            settings.IconsSize = (int)IconsSizeValue.GetValueOrDefault();

            settings.ArrowsOnLinks = ArrowsOnLinksChecked;
            settings.SimplePassthroughNodes = SimplePassthroughNodesChecked;
            settings.DynamicLineWidth = DynamicLWChecked;
            settings.AbbreviateSciPacks = AbbreviateSciPackChecked;
            settings.ShowRecipeToolTip = ShowNodeRecipeChecked;
            settings.RoundAssemblerCount = RoundAssemblerCountChecked;
            settings.FlagOUSuppliedNodes = FlagOUSupplyNodesChecked;
            settings.FlagDarkMode = FlagDarkMode;

            settings.ShowErrorArrows = ErrorArrowsChecked;
            settings.ShowWarningArrows = WarningArrowsChecked;
            settings.ShowDisconnectedArrows = DisconnectedArrowsChecked;
            settings.ShowOUSuppliedArrows = OuSuppliedArrowsChecked;

            settings.DefaultAssemblerOption = (AssemblerSelector.Style)AssemblerSelectorStyleIndex;
            settings.DefaultModuleOption = (ModuleSelector.Style)ModuleSelectorStyleIndex;
            settings.DefaultNodeDirection = NodeDirectionIndex == 1 ? NodeDirection.Down : NodeDirection.Up;
            settings.SmartNodeDirection = SmartNodeDirectionChecked;

            settings.EnableExtraProductivityForNonMiners = ShowProductivityBonusOnAllChecked;
            settings.ShowUnavailable = ShowUnavailablesChecked;
            settings.UseRecipeBWfilters = !LoadBarrelingChecked;

            settings.LowPriorityPower = LowPriorityPowerValue.GetValueOrDefault();
            settings.PullConsumerNodes = PullConsumerNodesChecked;
            settings.PullConsumerNodesPower = PullConsumerNodesPowerValue.GetValueOrDefault();

            //M-7 (final fix wave): FirstOrDefault, not First - UiScaleLabel is free-form storage even
            //though every real caller only ever puts a list-matching value in it, so a mismatch falls back
            //to 100% instead of throwing.
            (string Label, double Scale) fallbackStep = ("100%", 1.0);
            settings.UiScale = UiScaleSteps.FirstOrDefault(step => step.Label == UiScaleLabel, fallbackStep).Scale;
        }

        //--- Enabled Objects tab ------------------------------------------------------------------------

        [ObservableProperty]
        private string? filterText = "";

        [ObservableProperty]
        private bool showUnavailablesFilterChecked;

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> assemblerObjects = [];

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> minerObjects = [];

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> powerObjects = [];

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> beaconObjects = [];

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> moduleObjects = [];

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> recipeObjects = [];

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> qualityObjects = [];

        private List<EnabledObjectsListItem> unfilteredAssemblers = [];
        private List<EnabledObjectsListItem> unfilteredMiners = [];
        private List<EnabledObjectsListItem> unfilteredPower = [];
        private List<EnabledObjectsListItem> unfilteredBeacons = [];
        private List<EnabledObjectsListItem> unfilteredModules = [];
        private List<EnabledObjectsListItem> unfilteredRecipes = [];
        private List<EnabledObjectsListItem> unfilteredQualities = [];

        //Called once after the window bakes every category's icons/tooltips (reference LoadUnfilteredLists
        //in SettingsWindow.axaml.cs) - the baked EnabledObjectsListItem instances are handed over as-is,
        //still shared with the window's own PropertyChanged/Options.EnabledObjects wiring.
        public void LoadUnfilteredLists(
            List<EnabledObjectsListItem> assemblers,
            List<EnabledObjectsListItem> miners,
            List<EnabledObjectsListItem> power,
            List<EnabledObjectsListItem> beacons,
            List<EnabledObjectsListItem> modules,
            List<EnabledObjectsListItem> recipes,
            List<EnabledObjectsListItem> qualities) {
            unfilteredAssemblers = assemblers;
            unfilteredMiners = miners;
            unfilteredPower = power;
            unfilteredBeacons = beacons;
            unfilteredModules = modules;
            unfilteredRecipes = recipes;
            unfilteredQualities = qualities;
            RefreshFilteredLists();
        }

        partial void OnFilterTextChanged(string? value) => RefreshFilteredLists();
        partial void OnShowUnavailablesFilterCheckedChanged(bool value) => RefreshFilteredLists();

        //Ports UpdateFilteredLists (reference SettingsWindow.axaml.cs): reassigns every filtered property
        //to a fresh list on every call - FRESH-LIST rule, ItemsSource reference-equality short-circuits a
        //mutated-in-place reassignment and the bound ListBox never re-renders.
        public void RefreshFilteredLists() {
            AssemblerObjects = FilterList(unfilteredAssemblers);
            MinerObjects = FilterList(unfilteredMiners);
            PowerObjects = FilterList(unfilteredPower);
            BeaconObjects = FilterList(unfilteredBeacons);
            ModuleObjects = FilterList(unfilteredModules);
            RecipeObjects = FilterList(unfilteredRecipes);
            QualityObjects = FilterList(unfilteredQualities);
        }

        private IReadOnlyList<EnabledObjectsListItem> FilterList(List<EnabledObjectsListItem> unfiltered) {
            string filter = FilterText ?? "";
            return [.. unfiltered.Where(item =>
                (ShowUnavailablesFilterChecked || item.DataObject.Available) &&
                (string.IsNullOrEmpty(filter) || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))];
        }
    }
}
