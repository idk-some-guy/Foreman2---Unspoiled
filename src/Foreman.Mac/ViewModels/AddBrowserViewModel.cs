using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace Foreman.Mac.ViewModels {
    public enum BrowserSegment { Items, Recipes }

    public enum BrowserState { Content, Loading, Empty, NoResults, Error }

    public enum BrowserFailure { None, Catalogue, Search }

    public enum NoResultsAction { ClearSearch, ShowAll, RemoveConstraint, ShowItem }

    public sealed record BrowserCell(AddCandidate Candidate, string Label, IImage? Icon, bool IsAvailable, bool IsSelected, bool IconMissing = false);
    public sealed record BrowserSection(string? Group, string Subgroup, IReadOnlyList<BrowserCell> Cells) {
        public string Title => Group is null ? Subgroup : $"{Group} · {Subgroup}";
    }
    public sealed record BrowserGroup(IGroup Group, string Name, IImage? Icon, bool IsSelected);
    public sealed record BrowserRole(string Label, NodeType Role, NodeDirection? Direction);
    public sealed record BrowserPick(AddCandidate Candidate, NodeType Role, NodeDirection? SpoilPlantDirection, IQuality Quality, IItem? ExplicitOrigin = null);

    //Group sorting/visibility counting ports the retired on-canvas chooser panels' own logic.
    public sealed partial class AddBrowserViewModel : ObservableObject {
        private const int GroupIconSize = 16;
        private const int QualityIconSize = 16;
        private const int CellIconSize = 40;

        public static IReadOnlyList<BrowserRole> Roles { get; } = [
            new("Source", NodeType.Supplier, null),
            new("Passthrough", NodeType.Passthrough, null),
            new("Output", NodeType.Consumer, null),
            new("Spoil", NodeType.Spoil, NodeDirection.Up),
            new("Unspoil", NodeType.Spoil, NodeDirection.Down),
            new("Plant", NodeType.Plant, NodeDirection.Up),
            new("Unplant", NodeType.Plant, NodeDirection.Down),
        ];

        private readonly AddCatalog catalog;
        private readonly AddCatalog widenedCatalog;
        private readonly DataCache cache;
        private readonly AppSettings settings;
        private readonly Func<SKBitmap, int, IImage?> bake;
        private readonly Action<Action>? defer;
        private readonly Dictionary<IDataObjectBase, IImage?> cellIcons = [];
        private readonly HashSet<IDataObjectBase> pendingCellIcons = [];
        private readonly HashSet<IDataObjectBase> failedCellIcons = [];
        private bool iconResolvePosted;
        private bool probing;

        private BrowserFailure failure;
        private bool retrying;
        private bool userAction;
        private bool seeding;
        private int unconstrainedMatchCount;
        private BrowserRole? chosenRole;

        private readonly ObservableCollection<BrowserGroup> groups = [];
        private readonly ObservableCollection<BrowserSection> sections = [];

        //Each segment remembers its own last-picked group.
        private IGroup? selectedItemsGroup;
        private IGroup? selectedRecipesGroup;

        private AddRequest? request;
        private PendingOrigin? pendingOrigin;

        //The UnSpoil/UnPlant multi-origin chip's remembered target and its origin choices.
        private sealed record PendingOrigin(AddCandidate Target, NodeType Role, IReadOnlyCollection<IItem> Origins);

        //Defaults to the graph's own DefaultAssemblerQuality when the caller has one (MainWindow reads
        //GraphCanvas.Viewer.Graph), falling back to the cache default only when it doesn't. With a defer,
        //cell icons not yet baked resolve in a later dispatcher pass while the sidebar is already live.
        public AddBrowserViewModel(AddCatalog catalog, DataCache cache, AppSettings settings, Func<SKBitmap, int, IImage?> bake, IQuality? defaultAssemblerQuality = null, Action<Action>? defer = null) {
            this.catalog = catalog;
            widenedCatalog = new AddCatalog(cache, new AppSettings { ShowUnavailable = true, ShowHidden = true, IgnoreAssemblerStatus = true });
            this.cache = cache;
            this.settings = settings;
            this.bake = bake;
            this.defer = defer;
            Groups = new ReadOnlyObservableCollection<BrowserGroup>(groups);
            Sections = new ReadOnlyObservableCollection<BrowserSection>(sections);
            Qualities = [.. cache.AvailableQualities.Where(q => q.Enabled)];
            QualityIcons = [.. Qualities.Select(q => SafeBake(q, QualityIconSize))];
            quality = defaultAssemblerQuality ?? cache.DefaultQuality;
            selectedRole = Roles[0];
            Refresh();
        }

        [ObservableProperty]
        private BrowserSegment segment;

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        private IQuality? quality;

        [ObservableProperty]
        private BrowserCell? selectedCell;

        public event EventHandler<BrowserPick>? PlaceRequested;
        public event EventHandler? ImportPresetRequested;

        public IReadOnlyList<IQuality> Qualities { get; }
        public IReadOnlyList<IImage?> QualityIcons { get; }
        public IImage? QualityIcon => Quality is { } current && Qualities.ToList().IndexOf(current) is int i and >= 0 ? QualityIcons[i] : null;
        public string QualityLabel => $"Quality: {Quality?.FriendlyName}";
        public bool ShowsQuality => catalog.ShowsQuality;

        public ReadOnlyObservableCollection<BrowserGroup> Groups { get; }
        public ReadOnlyObservableCollection<BrowserSection> Sections { get; }

        public string SearchPlaceholder => Segment == BrowserSegment.Recipes ? "Search recipes…" : "Search items…";

        public bool IsLoading { get; private set; }

        public BrowserState State =>
            retrying ? BrowserState.Loading
            : failure != BrowserFailure.None ? BrowserState.Error
            : IsLoading ? BrowserState.Loading
            : QueryText.Length > 0 ? (sections.Count == 0 ? BrowserState.NoResults : BrowserState.Content)
            : pendingOrigin is not null ? BrowserState.Content
            : request?.Link is not null ? (sections.Count == 0 ? BrowserState.NoResults : BrowserState.Content)
            : groups.Count == 0 ? BrowserState.Empty
            : sections.Count == 0 && HiddenMatchCount > 0 ? BrowserState.NoResults
            : BrowserState.Content;

        public BrowserFailure Failure => failure;

        //Only an error raised by typing or Try Again takes focus; one present on open never does.
        public bool ErrorTakesFocus { get; private set; }

        //Test-only seams: throw from the catalogue build or the search match.
        internal Action? CatalogueFault { get; set; }
        internal Action<string>? SearchFault { get; set; }

        private string QueryText => SearchText.Trim();

        private const int QuotedQueryLimit = 24;

        //What the current query or category would show with every Filter-menu switch on; settings stay untouched.
        public int HiddenMatchCount { get; private set; }

        public bool IsSearchNoResults => QueryText.Length > 0;

        //Only a no-results panel raised by typing in this window takes focus; one present on open never does.
        public bool NoResultsTakesFocus { get; private set; }

        private string KindNoun => Segment == BrowserSegment.Recipes ? "recipe" : "item";

        private IItem? LinkItem => pendingOrigin is null && request?.Link is { Item.Item: IItem item } ? item : null;

        private bool LinkMakes => Link?.NodeType != NewNodeType.Consumer;

        public string NoResultsHeadline =>
            LinkItem is { } linkItem && !IsSearchNoResults
                ? (HiddenMatchCount > 0 ? $"Nothing to show for {linkItem.FriendlyName}" : $"No recipes {(LinkMakes ? "make" : "take")} {linkItem.FriendlyName}")
            : IsSearchNoResults
                ? $"No results for “{(QueryText.Length > QuotedQueryLimit ? QueryText[..QuotedQueryLimit] + "…" : QueryText)}”"
            : $"Nothing to show in {groups.FirstOrDefault(g => g.IsSelected)?.Name}";

        public string NoResultsSentence => LinkItem is { } linkItem ? LinkNoResultsSentence(linkItem.FriendlyName)
            : IsSearchNoResults
                ? (HiddenMatchCount > 0 ? $"{(Segment == BrowserSegment.Recipes ? "Recipes" : "Items")} that match are hidden by the Filter menu." : "Nothing in this preset matches the search.")
            : $"Every {KindNoun} in this category is hidden by the Filter menu.";

        private string LinkNoResultsSentence(string name) {
            string verb = LinkMakes ? "make" : "take";
            if (Segment == BrowserSegment.Items)
                return $"Only {name} is listed for this link.";
            if (!IsSearchNoResults)
                return HiddenMatchCount > 0 ? $"Every recipe that {verb}s {name} is hidden by the Filter menu."
                    : $"You can add {name} as {(LinkMakes ? "a source" : "an output")} instead.";
            return HiddenMatchCount > 0 ? $"Matching recipes that {verb} {name} are hidden by the Filter menu."
                : unconstrainedMatchCount > 0 ? $"Only recipes that {verb} {name} are listed for this link."
                : "Nothing in this preset matches the search.";
        }

        //Default last; under a link Remove Constraint appears only where the constraint alone hides the results.
        public IReadOnlyList<NoResultsAction> NoResultsActions {
            get {
                if (LinkItem is not null) {
                    if (Segment == BrowserSegment.Items)
                        return [NoResultsAction.ClearSearch, NoResultsAction.RemoveConstraint];
                    if (!IsSearchNoResults)
                        return HiddenMatchCount > 0 ? [NoResultsAction.ShowAll] : [NoResultsAction.RemoveConstraint, NoResultsAction.ShowItem];
                    return HiddenMatchCount > 0 ? [NoResultsAction.ClearSearch, NoResultsAction.ShowAll]
                        : unconstrainedMatchCount > 0 ? [NoResultsAction.ClearSearch, NoResultsAction.RemoveConstraint]
                        : [NoResultsAction.ClearSearch];
                }
                if (!IsSearchNoResults)
                    return [NoResultsAction.ShowAll];
                return HiddenMatchCount > 0 ? [NoResultsAction.ClearSearch, NoResultsAction.ShowAll] : [NoResultsAction.ClearSearch];
            }
        }

        public double WindowWidth {
            get => settings.BrowserWidth;
            set => settings.BrowserWidth = value;
        }

        public double WindowHeight {
            get => settings.BrowserHeight;
            set => settings.BrowserHeight = value;
        }
        public string? ChipText => pendingOrigin is { } pending ? $"Origins of {pending.Target.Name}"
            : request?.Link is { Item.Item: IItem linkItem } ? $"Compatible with {linkItem.FriendlyName}"
            : null;
        public bool HasChip => ChipText is not null;

        //MainWindow's own AddRequest only tracks the advancing placement Location; the link itself lives
        //here so ClearChip() (and any future re-seed) is the one place that can change it.
        internal AddLinkContext? Link => request?.Link;

        private IItem? SelectedItem => SelectedCell?.Candidate.Item;

        public bool CanSource => SelectedItem is not null && Link?.NodeType != NewNodeType.Consumer;
        public bool CanPassthrough => SelectedItem is not null;
        public bool CanOutput => SelectedItem is not null && Link?.NodeType != NewNodeType.Supplier;
        public bool CanSpoil => SelectedItem?.SpoilResult is not null;
        public bool CanUnspoil => SelectedItem is not null && SelectedItem.SpoilOrigins.Count > 0;
        public bool CanPlant => SelectedItem?.PlantResult is not null;
        public bool CanUnplant => SelectedItem is not null && SelectedItem.PlantOrigins.Count > 0;
        public bool RolesVisible => Segment == BrowserSegment.Items;
        public bool RolesEnabled => SelectedItem is not null;

        private bool IsRoleEnabled(int index) => index switch {
            0 => CanSource,
            1 => CanPassthrough,
            2 => CanOutput,
            3 => CanSpoil,
            4 => CanUnspoil,
            5 => CanPlant,
            6 => CanUnplant,
            _ => false,
        };

        public IReadOnlyList<string> DisabledRoles => [.. Roles.Where((_, i) => !IsRoleEnabled(i)).Select(r => r.Label)];

        [ObservableProperty]
        private BrowserRole selectedRole;

        public bool CanAdd => SelectedCell is { } cell && (Segment == BrowserSegment.Recipes
            ? cell.Candidate.Kind == AddKind.Recipe
            : pendingOrigin is not null || IsRoleEnabled(IndexOf(SelectedRole)));

        public string SelectionCaption => SelectedCell is { } cell ? $"{cell.Label} selected" : "";

        private static int IndexOf(BrowserRole role) => Roles.ToList().IndexOf(role);

        public void SelectRole(string? label) => PickRole(Roles.ToList().FindIndex(r => r.Label == label));

        //Fixed positions: a role the selection can't take, or no role at all, changes nothing.
        public void PickRole(int index) {
            if (!IsRoleEnabled(index))
                return;
            chosenRole = Roles[index];
            SelectedRole = chosenRole;
        }

        //The user's last explicit pick while it is allowed, otherwise the link's side, otherwise Source.
        private BrowserRole ResolveRole() {
            if (chosenRole is { } chosen && (SelectedItem is null || IsRoleEnabled(IndexOf(chosen))))
                return chosen;
            return Link?.NodeType == NewNodeType.Consumer ? Roles[2] : Roles[0];
        }

        public void SelectQuality(string? friendlyName) {
            if (Qualities.FirstOrDefault(q => q.FriendlyName == friendlyName) is { } picked)
                Quality = picked;
        }

        public void ClearSearch() => SearchText = "";

        //The one way to complete a link no recipe can serve: the link item itself, on the link's side.
        public void ShowLinkItem() {
            if (LinkItem is not IItem item)
                return;
            Segment = BrowserSegment.Items;
            SelectedCell = sections.SelectMany(s => s.Cells).FirstOrDefault(c => Equals(c.Candidate.Item, item));
            SelectedRole = Link?.NodeType == NewNodeType.Consumer ? Roles[2] : Roles[0];
        }

        public void ShowAll() {
            settings.ShowUnavailable = true;
            settings.ShowHidden = true;
            settings.IgnoreAssemblerStatus = true;
            RaiseFiltersChanged();
            Refresh();
        }

        //Reruns the failed step against the preset in memory, through .loading when a defer is available.
        public void Retry() {
            BrowserFailure failed = failure;
            if (failed == BrowserFailure.None)
                return;
            if (defer is null) {
                Rerun(failed);
                return;
            }
            retrying = true;
            OnPropertyChanged(nameof(State));
            defer(() => {
                retrying = false;
                Rerun(failed);
            });
        }

        private void Rerun(BrowserFailure failed) {
            userAction = true;
            try {
                if (failed == BrowserFailure.Catalogue)
                    Refresh();
                else
                    RefreshSections();
            } finally {
                userAction = false;
            }
            OnPropertyChanged(nameof(State));
        }

        public void RequestImportPreset() => ImportPresetRequested?.Invoke(this, EventArgs.Empty);

        public bool ShowUnavailable {
            get => settings.ShowUnavailable;
            set {
                if (settings.ShowUnavailable == value)
                    return;
                settings.ShowUnavailable = value;
                RaiseFiltersChanged();
                Refresh();
            }
        }

        public bool ShowHidden {
            get => settings.ShowHidden;
            set {
                if (settings.ShowHidden == value)
                    return;
                settings.ShowHidden = value;
                RaiseFiltersChanged();
                Refresh();
            }
        }

        public bool IgnoreAssembler {
            get => settings.IgnoreAssemblerStatus;
            set {
                if (settings.IgnoreAssemblerStatus == value)
                    return;
                settings.IgnoreAssemblerStatus = value;
                RaiseFiltersChanged();
                Refresh();
            }
        }

        //Clicking the already-selected group is a no-op - it must not rebuild Groups/Sections
        //(and so must not retrigger any entrance animation on cells that were already showing).
        public void SelectGroup(IGroup group) {
            if (Equals(SelectedGroupForSegment(Segment), group))
                return;
            SetSelectedGroupForSegment(Segment, group);
            if (Segment == BrowserSegment.Items)
                settings.BrowserItemsGroup = group.Name;
            else
                settings.BrowserRecipesGroup = group.Name;
            Refresh();
        }

        public void SelectCell(BrowserCell? cell) => SelectedCell = cell;

        //A link-drag's own "Add..." (Scope.All, Link set) always means a recipe search -
        //only a scope-less/link-less open (the toolbar, background menu, double-click) still defaults to Items.
        public void Seed(AddRequest request) {
            this.request = request;
            pendingOrigin = null;
            Segment = request.Link is not null || request.Scope == AddScope.Recipes ? BrowserSegment.Recipes : BrowserSegment.Items;
            seeding = true;
            try {
                SearchText = request.SeedText;
            } finally {
                seeding = false;
            }
            Refresh();
            RaiseChipChanged();
        }

        //Builds the same PendingOrigin the footer's multi-origin Place() branch does, without a SelectCell/Place round-trip.
        public void SeedOrigins(NodeType role) {
            if (request?.Link?.Item.Item is not IItem item)
                return;
            IReadOnlyCollection<IItem> origins = role == NodeType.Spoil ? item.SpoilOrigins : item.PlantOrigins;
            if (origins.Count == 0)
                return;
            pendingOrigin = new PendingOrigin(AddCatalog.Candidate(item), role, origins);
            RefreshSections();
            RaiseChipChanged();
        }

        //The x clears both the link and any origins filter, and later picks land disconnected.
        public void ClearChip() {
            pendingOrigin = null;
            if (request is not null)
                request = request with { Link = null };
            Refresh();
            RaiseChipChanged();
        }

        //UnSpoil/UnPlant with 2+ origins swaps the grid to an origin picker instead of placing directly.
        public void Place(NodeType role, NodeDirection? spoilPlantDirection = null) {
            if (SelectedCell is null)
                return;
            IQuality resolvedQuality = Quality ?? cache.DefaultQuality!;

            if (pendingOrigin is { } pending && spoilPlantDirection == NodeDirection.Down && role == pending.Role) {
                if (SelectedCell.Candidate.Item is IItem chosenOrigin) {
                    pendingOrigin = null;
                    PlaceRequested?.Invoke(this, new BrowserPick(pending.Target, role, spoilPlantDirection, resolvedQuality, chosenOrigin));
                    RefreshSections();
                    RaiseChipChanged();
                }
                return;
            }

            if (spoilPlantDirection == NodeDirection.Down && role is NodeType.Spoil or NodeType.Plant && SelectedCell.Candidate.Item is IItem item) {
                IReadOnlyCollection<IItem> origins = role == NodeType.Spoil ? item.SpoilOrigins : item.PlantOrigins;
                if (origins.Count >= 2) {
                    pendingOrigin = new PendingOrigin(SelectedCell.Candidate, role, origins);
                    RefreshSections();
                    RaiseChipChanged();
                    return;
                }
            }

            PlaceRequested?.Invoke(this, new BrowserPick(SelectedCell.Candidate, role, spoilPlantDirection, resolvedQuality));
        }

        public void PlaceSelectedRecipe() {
            if (SelectedCell?.Candidate.Kind != AddKind.Recipe)
                return;
            IQuality resolvedQuality = Quality ?? cache.DefaultQuality!;
            PlaceRequested?.Invoke(this, new BrowserPick(SelectedCell.Candidate, NodeType.Recipe, null, resolvedQuality));
        }

        //Add, double-click and Return: an active origins chip completes that pick, otherwise the segment
        //decides (a recipe in Recipes, the Add as role in Items).
        public void Add() {
            if (pendingOrigin is { } pending) {
                Place(pending.Role, NodeDirection.Down);
                return;
            }
            if (Segment == BrowserSegment.Recipes)
                PlaceSelectedRecipe();
            else if (IsRoleEnabled(IndexOf(SelectedRole)))
                Place(SelectedRole.Role, SelectedRole.Direction);
        }

        private void RaiseChipChanged() {
            OnPropertyChanged(nameof(ChipText));
            OnPropertyChanged(nameof(HasChip));
            NotifyRoleProperties();
        }

        private void RaiseFiltersChanged() {
            OnPropertyChanged(nameof(ShowUnavailable));
            OnPropertyChanged(nameof(ShowHidden));
            OnPropertyChanged(nameof(IgnoreAssembler));
        }

        private void NotifyRoleProperties() {
            OnPropertyChanged(nameof(CanSource));
            OnPropertyChanged(nameof(CanPassthrough));
            OnPropertyChanged(nameof(CanOutput));
            OnPropertyChanged(nameof(CanSpoil));
            OnPropertyChanged(nameof(CanUnspoil));
            OnPropertyChanged(nameof(CanPlant));
            OnPropertyChanged(nameof(CanUnplant));
            OnPropertyChanged(nameof(RolesVisible));
            OnPropertyChanged(nameof(RolesEnabled));
            OnPropertyChanged(nameof(DisabledRoles));
            SelectedRole = ResolveRole();
            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(SelectionCaption));
        }

        partial void OnSegmentChanged(BrowserSegment value) {
            Refresh();
            NotifyRoleProperties();
        }

        partial void OnSearchTextChanged(string value) {
            userAction = !seeding;
            try {
                RefreshSections();
            } finally {
                userAction = false;
            }
        }

        partial void OnQualityChanged(IQuality? value) {
            OnPropertyChanged(nameof(QualityLabel));
            OnPropertyChanged(nameof(QualityIcon));
        }
        partial void OnSelectedCellChanged(BrowserCell? value) => NotifyRoleProperties();
        partial void OnSelectedRoleChanged(BrowserRole value) => OnPropertyChanged(nameof(CanAdd));

        private IGroup? SelectedGroupForSegment(BrowserSegment forSegment) =>
            forSegment == BrowserSegment.Items ? selectedItemsGroup : selectedRecipesGroup;

        private void SetSelectedGroupForSegment(BrowserSegment forSegment, IGroup? group) {
            if (forSegment == BrowserSegment.Items)
                selectedItemsGroup = group;
            else
                selectedRecipesGroup = group;
        }

        //internal: MainWindow calls this on a theme flip to rebuild groups/cells against its freshly
        //cleared browserIconCache (see MainWindow.RefreshBrowserIcons). forceRebuild bypasses
        //RefreshSections' own reuse check below, which otherwise keeps the still-bound (and now
        //stale-icon) cells whenever the candidate set itself hasn't changed.
        internal void Refresh(bool forceRebuild = false) {
            if (forceRebuild) {
                cellIcons.Clear();
                failedCellIcons.Clear();
            }
            try {
                RefreshGroups();
            } catch (Exception ex) {
                Fail(BrowserFailure.Catalogue, ex);
                return;
            }
            RefreshSections(forceRebuild);
        }

        private void Fail(BrowserFailure kind, Exception ex) {
            ErrorLogging.LogException(ex, kind == BrowserFailure.Catalogue ? "Add browser: couldn't read the preset" : "Add browser: couldn't search the preset");
            failure = kind;
            ErrorTakesFocus = userAction;
            if (kind == BrowserFailure.Catalogue)
                groups.Clear();
            sections.Clear();
            HiddenMatchCount = 0;
            SelectedCell = null;
            OnPropertyChanged(nameof(Sections));
            OnPropertyChanged(nameof(Failure));
            OnPropertyChanged(nameof(State));
        }

        //A group only appears once it has at least one visible-by-availability entry for the segment.
        private void RefreshGroups() {
            CatalogueFault?.Invoke();
            var sorted = new List<IGroup>();
            foreach (IGroup group in settings.ShowUnavailable ? cache.Groups.Values : cache.AvailableGroups) {
                int count = 0;
                foreach (ISubgroup subgroup in group.Subgroups)
                    count += Segment == BrowserSegment.Items
                        ? (settings.ShowUnavailable ? subgroup.Items.Count : subgroup.Items.Count(i => i.Available))
                        : (settings.ShowUnavailable ? subgroup.Recipes.Count : subgroup.Recipes.Count(r => r.Available));
                if (count > 0)
                    sorted.Add(group);
            }
            sorted.Sort();

            IGroup? current = SelectedGroupForSegment(Segment);
            if (current is null || !sorted.Contains(current)) {
                string remembered = Segment == BrowserSegment.Items ? settings.BrowserItemsGroup : settings.BrowserRecipesGroup;
                current = sorted.FirstOrDefault(g => g.Name == remembered) ?? (sorted.Count > 0 ? sorted[0] : null);
            }
            SetSelectedGroupForSegment(Segment, current);

            groups.Clear();
            foreach (IGroup group in sorted)
                groups.Add(new BrowserGroup(group, HumanizeGroupName(group.FriendlyName, group.Name), SafeBake(group, GroupIconSize), group.Equals(current)));
        }

        private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

        //A localised name can still carry a stray newline or run of whitespace (the Foreman-internal
        //"extra" group joins three category names this way) - collapse any such run to one space.
        private static string CollapseWhitespace(string value) => WhitespaceRun.Replace(value, " ").Trim();

        //Some groups/subgroups carry no localised name - FriendlyName then either mirrors the raw internal
        //key outright or is left as Factorio's own unresolved locale placeholder ("§§SG:EXTRACTION_ITEMS").
        //Strip the placeholder prefix and turn the remaining key into a readable phrase; a genuinely
        //localised name (distinct from Name, no placeholder) passes through with its whitespace collapsed.
        //A placeholder's own kind marker (SG:, G:, any case) sits right between the "§§" and the real key -
        //only that leading run is stripped, so a colon later in a real name is never touched.
        private static readonly Regex LocalePlaceholderPrefix = new(@"^§§(?:[A-Za-z]+:)?", RegexOptions.Compiled);

        internal static string Humanize(string friendlyName, string name) {
            if (!friendlyName.StartsWith("§§", StringComparison.Ordinal) && friendlyName != name)
                return CollapseWhitespace(friendlyName);
            string key = LocalePlaceholderPrefix.Replace(friendlyName, "", 1);
            string words = CollapseWhitespace(key.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant());
            return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
        }

        //The sidebar shows group names as-is (no upper-casing heading transform), so a humanized one gets
        //title-cased here; an already-localised name is left exactly as authored (whitespace collapsed).
        private static string HumanizeGroupName(string friendlyName, string name) {
            string humanized = Humanize(friendlyName, name);
            return humanized == CollapseWhitespace(friendlyName) ? humanized : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(humanized);
        }

        //A rebuild that would produce the exact same content (compared by candidate, not
        //reference) is skipped outright - Sections stays untouched so its containers survive and don't
        //replay their entrance animation. Trailing whitespace in a re-typed search is the common case.
        private void RefreshSections(bool forceRebuild = false) {
            List<BrowserSection> candidate;
            int hidden = 0;
            int unconstrained = 0;
            try {
                candidate = BuildSections(catalog);
                if (candidate.Count == 0 && pendingOrigin is null) {
                    hidden = ProbeHiddenMatches();
                    if (hidden == 0 && request?.Link is not null && Segment == BrowserSegment.Recipes && QueryText.Length > 0)
                        unconstrained = ProbeUnconstrainedMatches();
                }
            } catch (SearchFailure ex) {
                Fail(BrowserFailure.Search, ex.InnerException!);
                return;
            } catch (Exception ex) {
                Fail(BrowserFailure.Catalogue, ex);
                return;
            }
            bool recovered = failure != BrowserFailure.None;
            failure = BrowserFailure.None;
            HiddenMatchCount = hidden;
            unconstrainedMatchCount = unconstrained;
            NoResultsTakesFocus = userAction;
            RaiseNoResultsChanged();
            ScheduleIconResolve();
            if (!forceRebuild && !recovered && SectionsMatch(sections, candidate)) {
                OnPropertyChanged(nameof(State));
                return;
            }

            AddCandidate? previouslySelected = SelectedCell?.Candidate;
            sections.Clear();
            foreach (BrowserSection section in candidate)
                sections.Add(section);

            SelectedCell = previouslySelected is null
                ? null
                : sections.SelectMany(s => s.Cells).FirstOrDefault(c => c.Candidate.Equals(previouslySelected));
            OnPropertyChanged(nameof(Sections));
            if (recovered)
                OnPropertyChanged(nameof(Failure));
            OnPropertyChanged(nameof(State));
        }

        private int ProbeHiddenMatches() {
            probing = true;
            try {
                return BuildSections(widenedCatalog).Sum(s => s.Cells.Count);
            } finally {
                probing = false;
            }
        }

        //The query without the link, every switch on: whether the constraint alone hides the matches.
        private int ProbeUnconstrainedMatches() {
            probing = true;
            try {
                var target = new List<BrowserSection>();
                BuildSearchSection(target, QueryText, widenedCatalog);
                return target.Sum(s => s.Cells.Count);
            } catch (Exception ex) {
                throw new SearchFailure(ex);
            } finally {
                probing = false;
            }
        }

        private void RaiseNoResultsChanged() {
            OnPropertyChanged(nameof(HiddenMatchCount));
            OnPropertyChanged(nameof(NoResultsHeadline));
            OnPropertyChanged(nameof(NoResultsSentence));
            OnPropertyChanged(nameof(NoResultsActions));
        }

        private sealed class SearchFailure(Exception inner) : Exception(inner.Message, inner);

        private IImage? SafeBake(IDataObjectBase entry, int size) {
            try {
                return bake(entry.Icon, size);
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, $"Add browser: couldn't decode the icon of {entry.Name}");
                return null;
            }
        }

        //A failed bake is remembered apart from a pending one: the cell draws the placeholder, not an empty well.
        private IImage? BakeCellIcon(IDataObjectBase entry) {
            try {
                return bake(entry.Icon, CellIconSize);
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, $"Add browser: couldn't decode the icon of {entry.Name}");
                failedCellIcons.Add(entry);
                return null;
            }
        }

        private void ScheduleIconResolve() {
            if (defer is null || pendingCellIcons.Count == 0 || iconResolvePosted)
                return;
            iconResolvePosted = true;
            IsLoading = true;
            OnPropertyChanged(nameof(IsLoading));
            defer(ResolvePendingIcons);
        }

        private void ResolvePendingIcons() {
            iconResolvePosted = false;
            foreach (IDataObjectBase entry in pendingCellIcons)
                cellIcons[entry] = BakeCellIcon(entry);
            pendingCellIcons.Clear();
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoading));
            if (failure == BrowserFailure.Catalogue)
                OnPropertyChanged(nameof(State));
            else
                RefreshSections(forceRebuild: true);
        }

        //Keyed on the entry, not its Icon: reading Icon is what decodes a lazily loaded preset icon.
        private IImage? CellIcon(IDataObjectBase entry) {
            if (cellIcons.TryGetValue(entry, out IImage? baked))
                return baked;
            if (probing)
                return null;
            if (defer is null)
                return cellIcons[entry] = BakeCellIcon(entry);
            pendingCellIcons.Add(entry);
            return null;
        }

        private List<BrowserSection> BuildSections(AddCatalog source) {
            var target = new List<BrowserSection>();
            if (pendingOrigin is { } pending && Segment == BrowserSegment.Items) {
                target.Add(new BrowserSection(null, "Origins", [.. pending.Origins.Select(o => BuildCell(AddCatalog.Candidate(o)))]));
            } else if (request?.Link is { } link) {
                BuildLinkSections(target, link, source);
            } else {
                string text = SearchText.Trim();
                if (text.Length == 0) {
                    BuildGroupSections(target, source);
                } else {
                    try {
                        BuildSearchSection(target, text, source);
                    } catch (Exception ex) {
                        throw new SearchFailure(ex);
                    }
                }
            }
            return target;
        }

        private static bool SectionsMatch(ObservableCollection<BrowserSection> current, List<BrowserSection> candidate) {
            if (current.Count != candidate.Count)
                return false;
            for (int i = 0; i < current.Count; i++) {
                if (current[i].Title != candidate[i].Title || current[i].Cells.Count != candidate[i].Cells.Count)
                    return false;
                for (int j = 0; j < current[i].Cells.Count; j++)
                    if (!current[i].Cells[j].Candidate.Equals(candidate[i].Cells[j].Candidate))
                        return false;
            }
            return true;
        }

        private void BuildGroupSections(List<BrowserSection> target, AddCatalog source) {
            CatalogueFault?.Invoke();
            IGroup? selected = SelectedGroupForSegment(Segment);
            if (selected is null)
                return;

            foreach (ISubgroup subgroup in selected.Subgroups) {
                IEnumerable<AddCandidate> candidates = Segment == BrowserSegment.Items
                    ? subgroup.Items.Where(source.IsVisible).Select(AddCatalog.Candidate)
                    : subgroup.Recipes.Where(source.IsVisible).Select(AddCatalog.Candidate);
                var cells = candidates.Select(BuildCell).ToList();
                if (cells.Count > 0)
                    target.Add(new BrowserSection(null, Humanize(subgroup.FriendlyName, subgroup.Name), cells));
            }
        }

        //A recipe search also matches on ingredient/product name, not just the recipe's own name.
        private void BuildSearchSection(List<BrowserSection> target, string text, AddCatalog source) {
            SearchFault?.Invoke(text);
            AddScope scope = Segment == BrowserSegment.Items ? AddScope.Items : AddScope.Recipes;
            var candidates = new List<AddCandidate>(source.Search(text, scope, link: null));

            if (Segment == BrowserSegment.Recipes) {
                var seen = new HashSet<IRecipe>(candidates.Where(c => c.Recipe is not null).Select(c => c.Recipe!));
                foreach (IRecipe recipe in cache.Recipes.Values) {
                    if (seen.Contains(recipe) || !source.IsVisible(recipe))
                        continue;
                    bool matchesIngredientOrProduct =
                        recipe.IngredientList.Any(i => i.FriendlyName.Contains(text, StringComparison.CurrentCultureIgnoreCase)) ||
                        recipe.ProductList.Any(i => i.FriendlyName.Contains(text, StringComparison.CurrentCultureIgnoreCase));
                    if (!matchesIngredientOrProduct)
                        continue;
                    candidates.Add(AddCatalog.Candidate(recipe));
                    seen.Add(recipe);
                }
            }

            AppendGroupedSections(target, candidates);
        }

        //Link-drag "Add..." (a dragged tab's Compatible-with-{item} chip): Items narrows to the dragged
        //item itself, Recipes to every recipe AddCatalog.IsCompatible allows for that link's direction -
        //SearchText (the ⌘↵-seeded query) still narrows either list further.
        private void BuildLinkSections(List<BrowserSection> target, AddLinkContext link, AddCatalog source) {
            string text = SearchText.Trim();
            if (Segment == BrowserSegment.Items) {
                if (link.Item.Item is IItem linkItem && Matches(linkItem.FriendlyName, text))
                    AppendGroupedSections(target, [AddCatalog.Candidate(linkItem)]);
            } else {
                var candidates = cache.Recipes.Values.Where(r => source.IsVisible(r) && source.IsCompatible(r, link) && Matches(r.FriendlyName, text)).Select(AddCatalog.Candidate);
                AppendGroupedSections(target, candidates);
            }
        }

        //Search/link matches sit one section per subgroup (preset order), titled "Group ·
        //Subgroup", instead of a single catch-all heading - two similarly named recipes from different
        //subgroups no longer sit ambiguously side by side under one "Results" list.
        private void AppendGroupedSections(List<BrowserSection> target, IEnumerable<AddCandidate> candidates) {
            var bySubgroup = candidates
                .Select(c => (Candidate: c, Subgroup: SubgroupOf(c)))
                .GroupBy(x => x.Subgroup)
                .OrderBy(g => g.Key, SubgroupOrderComparer.Instance);

            foreach (var group in bySubgroup)
                target.Add(SectionFor(group.Key, [.. group.Select(x => BuildCell(x.Candidate))]));
        }

        private static ISubgroup? SubgroupOf(AddCandidate candidate) => candidate.Item?.MySubgroup ?? candidate.Recipe?.MySubgroup;

        private static BrowserSection SectionFor(ISubgroup? subgroup, IReadOnlyList<BrowserCell> cells) {
            if (subgroup is null)
                return new BrowserSection(null, "Results", cells);
            string subgroupName = Humanize(subgroup.FriendlyName, subgroup.Name);
            return new BrowserSection(subgroup.MyGroup is { } group ? Humanize(group.FriendlyName, group.Name) : null, subgroupName, cells);
        }

        //Sorts by owning group first, then by the subgroup's own order within it - the same preset order
        //the sidebar and BuildGroupSections already walk.
        private sealed class SubgroupOrderComparer : IComparer<ISubgroup?> {
            public static readonly SubgroupOrderComparer Instance = new();
            public int Compare(ISubgroup? x, ISubgroup? y) {
                if (ReferenceEquals(x, y))
                    return 0;
                if (x is null)
                    return 1;
                if (y is null)
                    return -1;
                int groupOrder = x.MyGroup is { } gx && y.MyGroup is { } gy ? gx.CompareTo(gy) : 0;
                return groupOrder != 0 ? groupOrder : x.CompareTo(y);
            }
        }

        private static bool Matches(string name, string searchText) =>
            searchText.Length == 0 || name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);

        private BrowserCell BuildCell(AddCandidate candidate) => new(
            candidate,
            candidate.Name,
            CellIcon(candidate.Entry),
            candidate.Entry.Available,
            SelectedCell is not null && candidate.Equals(SelectedCell.Candidate),
            failedCellIcons.Contains(candidate.Entry));
    }
}
