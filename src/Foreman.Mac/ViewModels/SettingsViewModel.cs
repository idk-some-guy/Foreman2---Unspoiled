using CommunityToolkit.Mvvm.ComponentModel;
using Foreman;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Foreman.Mac.ViewModels {
    public enum SettingsPane { Presets, EnabledObjects, General, Advanced, Annotations, Appearance }

    public sealed partial class SettingsViewModel : ObservableObject {
        private static readonly HashSet<string> ExcludedFromSettingChanged = [
            nameof(FilterText), nameof(ShowUnavailablesFilterChecked),
            nameof(AssemblerObjects), nameof(MinerObjects), nameof(PowerObjects), nameof(BeaconObjects),
            nameof(ModuleObjects), nameof(RecipeObjects), nameof(QualityObjects),
            nameof(EnabledObjectsCategoryIndex), nameof(EnabledObjectsShowIndex), nameof(VisibleObjects), nameof(EnabledObjectsCategoryLabels),
            nameof(ThemeIndex),
        ];

        public event Action<string>? SettingChanged;

        protected override void OnPropertyChanged(PropertyChangedEventArgs e) {
            base.OnPropertyChanged(e);
            if (e.PropertyName is string name && !ExcludedFromSettingChanged.Contains(name))
                SettingChanged?.Invoke(name);
        }

        [ObservableProperty]
        private decimal? qualityStepsValue;

        //0 Low, 1 Medium, 2 High (LevelOfDetail).
        [ObservableProperty]
        private int levelOfDetailIndex = (int)LevelOfDetail.Medium;

        //0 Rounded, 1 Orthogonal (LinkRouting).
        [ObservableProperty]
        private int linkRoutingIndex;

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
        private bool roundAssemblerCountChecked;

        [ObservableProperty]
        private bool flagOUSupplyNodesChecked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ThemeIndex))]
        private ThemeMode flagDarkMode;

        public int ThemeIndex {
            get => (int)FlagDarkMode;
            set => FlagDarkMode = (ThemeMode)value;
        }

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

        private static readonly string[] ScaleLabels =
            [.. ScaleContract.Steps.Select(step => string.Format(CultureInfo.InvariantCulture, "{0:0} %", step * 100))];

        public static IReadOnlyList<string> UiScaleLabels => ScaleLabels;

        [ObservableProperty]
        private string uiScaleLabel = ScaleLabels[0];

        public void LoadFrom(AppSettings settings) {
            QualityStepsValue = settings.QualitySteps;
            LevelOfDetailIndex = Enum.IsDefined(typeof(LevelOfDetail), settings.LevelOfDetail) ? settings.LevelOfDetail : (int)LevelOfDetail.Medium;
            LinkRoutingIndex = settings.DefaultLinkRouting == LinkRouting.Orthogonal ? 1 : 0;
            NodeCountForSimpleViewValue = Math.Min(2000m, settings.NodeCountForSimpleView);
            IconsSizeValue = settings.IconsSize;

            ArrowsOnLinksChecked = settings.ArrowsOnLinks;
            SimplePassthroughNodesChecked = settings.SimplePassthroughNodes;
            DynamicLWChecked = settings.DynamicLineWidth;
            AbbreviateSciPackChecked = settings.AbbreviateSciPacks;
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

            UiScaleLabel = ScaleLabels[ScaleContract.Steps.Select((step, i) => (Distance: Math.Abs(step - settings.UiScale), Index: i)).Min().Index];
        }

        public void ApplyTo(AppSettings settings) {
            settings.QualitySteps = (int)QualityStepsValue.GetValueOrDefault();
            settings.LevelOfDetail = LevelOfDetailIndex;
            settings.DefaultLinkRouting = LinkRoutingIndex == 1 ? LinkRouting.Orthogonal : LinkRouting.Rounded;
            settings.NodeCountForSimpleView = (int)NodeCountForSimpleViewValue.GetValueOrDefault();
            settings.IconsSize = (int)IconsSizeValue.GetValueOrDefault();

            settings.ArrowsOnLinks = ArrowsOnLinksChecked;
            settings.SimplePassthroughNodes = SimplePassthroughNodesChecked;
            settings.DynamicLineWidth = DynamicLWChecked;
            settings.AbbreviateSciPacks = AbbreviateSciPackChecked;
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

            int step = Array.IndexOf(ScaleLabels, UiScaleLabel);
            settings.UiScale = ScaleContract.Steps[Math.Max(step, 0)];
        }

        //Per pane, in restore order; LoadBarrelingChecked goes last because changing it reloads the window.
        private static readonly Dictionary<SettingsPane, string[]> PaneProperties = new() {
            [SettingsPane.General] = [nameof(AssemblerSelectorStyleIndex), nameof(ModuleSelectorStyleIndex), nameof(NodeDirectionIndex),
                nameof(SmartNodeDirectionChecked), nameof(SimplePassthroughNodesChecked), nameof(RoundAssemblerCountChecked),
                nameof(QualityStepsValue)],
            [SettingsPane.Advanced] = [nameof(ShowProductivityBonusOnAllChecked), nameof(ShowUnavailablesChecked), nameof(LowPriorityPowerValue),
                nameof(PullConsumerNodesChecked), nameof(PullConsumerNodesPowerValue), nameof(LoadBarrelingChecked)],
            [SettingsPane.Annotations] = [nameof(ErrorArrowsChecked), nameof(WarningArrowsChecked), nameof(DisconnectedArrowsChecked),
                nameof(OuSuppliedArrowsChecked), nameof(FlagOUSupplyNodesChecked)],
            [SettingsPane.Appearance] = [nameof(FlagDarkMode), nameof(UiScaleLabel), nameof(LinkRoutingIndex), nameof(DynamicLWChecked),
                nameof(ArrowsOnLinksChecked), nameof(LevelOfDetailIndex), nameof(NodeCountForSimpleViewValue), nameof(IconsSizeValue),
                nameof(AbbreviateSciPackChecked)],
        };

        private static SettingsViewModel Defaults() {
            var defaults = new SettingsViewModel();
            defaults.LoadFrom(new AppSettings());
            return defaults;
        }

        public static bool HasDefaults(SettingsPane pane) => PaneProperties.ContainsKey(pane);

        public bool IsAtDefaults(SettingsPane pane) {
            if (!PaneProperties.TryGetValue(pane, out string[]? names))
                return true;
            SettingsViewModel defaults = Defaults();
            return names.All(name => Equals(Property(name).GetValue(this), Property(name).GetValue(defaults)));
        }

        public void RestoreDefaults(SettingsPane pane) {
            if (!PaneProperties.TryGetValue(pane, out string[]? names))
                return;
            SettingsViewModel defaults = Defaults();
            foreach (string name in names)
                Property(name).SetValue(this, Property(name).GetValue(defaults));
        }

        private static PropertyInfo Property(string name) => typeof(SettingsViewModel).GetProperty(name)!;

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

        public static IReadOnlyList<string> EnabledObjectsCategoryNames { get; } = ["Assemblers", "Miners", "Power", "Beacons", "Modules", "Recipes", "Qualities"];

        [ObservableProperty]
        private int enabledObjectsCategoryIndex;

        //0 All, 1 Enabled, 2 Disabled.
        [ObservableProperty]
        private int enabledObjectsShowIndex;

        [ObservableProperty]
        private IReadOnlyList<EnabledObjectsListItem> visibleObjects = [];

        [ObservableProperty]
        private IReadOnlyList<string> enabledObjectsCategoryLabels = [.. EnabledObjectsCategoryNames.Select(n => $"{n} (0)")];

        private List<EnabledObjectsListItem> unfilteredAssemblers = [];
        private List<EnabledObjectsListItem> unfilteredMiners = [];
        private List<EnabledObjectsListItem> unfilteredPower = [];
        private List<EnabledObjectsListItem> unfilteredBeacons = [];
        private List<EnabledObjectsListItem> unfilteredModules = [];
        private List<EnabledObjectsListItem> unfilteredRecipes = [];
        private List<EnabledObjectsListItem> unfilteredQualities = [];

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
            EnabledObjectsCategoryLabels = [.. EnabledObjectsCategoryNames.Zip(UnfilteredCategories, (name, list) => $"{name} ({list.Count})")];
            RefreshFilteredLists();
        }

        private IEnumerable<List<EnabledObjectsListItem>> UnfilteredCategories =>
            [unfilteredAssemblers, unfilteredMiners, unfilteredPower, unfilteredBeacons, unfilteredModules, unfilteredRecipes, unfilteredQualities];

        partial void OnFilterTextChanged(string? value) => RefreshFilteredLists();
        partial void OnShowUnavailablesFilterCheckedChanged(bool value) => RefreshFilteredLists();
        partial void OnEnabledObjectsShowIndexChanged(int value) => RefreshFilteredLists();
        partial void OnEnabledObjectsCategoryIndexChanged(int value) => UpdateVisibleObjects();

        //Assign fresh lists: ItemsSource short-circuits on reference equality, so an in-place mutation never re-renders
        public void RefreshFilteredLists() {
            AssemblerObjects = FilterList(unfilteredAssemblers);
            MinerObjects = FilterList(unfilteredMiners);
            PowerObjects = FilterList(unfilteredPower);
            BeaconObjects = FilterList(unfilteredBeacons);
            ModuleObjects = FilterList(unfilteredModules);
            RecipeObjects = FilterList(unfilteredRecipes);
            QualityObjects = FilterList(unfilteredQualities);
            UpdateVisibleObjects();
        }

        private void UpdateVisibleObjects() =>
            VisibleObjects = EnabledObjectsCategoryIndex switch {
                1 => MinerObjects,
                2 => PowerObjects,
                3 => BeaconObjects,
                4 => ModuleObjects,
                5 => RecipeObjects,
                6 => QualityObjects,
                _ => AssemblerObjects,
            };

        private IReadOnlyList<EnabledObjectsListItem> FilterList(List<EnabledObjectsListItem> unfiltered) {
            string filter = FilterText ?? "";
            return [.. unfiltered.Where(item =>
                (ShowUnavailablesFilterChecked || item.DataObject.Available) &&
                EnabledObjectsShowIndex switch { 1 => item.IsChecked, 2 => !item.IsChecked, _ => true } &&
                (string.IsNullOrEmpty(filter) || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))];
        }
    }
}
