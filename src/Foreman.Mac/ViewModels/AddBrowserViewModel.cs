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

namespace Foreman.Mac.ViewModels {
    public enum BrowserSegment { Items, Recipes }

    public sealed record BrowserCell(AddCandidate Candidate, string Label, IImage? Icon, bool IsAvailable, bool IsSelected);
    public sealed record BrowserSection(string Title, IReadOnlyList<BrowserCell> Cells) {
        public string Heading => Title.ToUpperInvariant();
    }
    public sealed record BrowserGroup(IGroup Group, string Name, IImage? Icon, bool IsSelected);
    public sealed record BrowserPick(AddCandidate Candidate, NodeType Role, NodeDirection? SpoilPlantDirection, IQuality Quality, IItem? ExplicitOrigin = null);

    //Group sorting/visibility counting ports the retired on-canvas chooser panels' own logic.
    public sealed partial class AddBrowserViewModel : ObservableObject {
        private const int GroupIconSize = 16;
        private const int CellIconSize = 40;

        private readonly AddCatalog catalog;
        private readonly DataCache cache;
        private readonly AppSettings settings;
        private readonly Func<SKBitmap, int, IImage?> bake;

        private readonly ObservableCollection<BrowserGroup> groups = [];
        private readonly ObservableCollection<BrowserSection> sections = [];

        //Each segment remembers its own last-picked group.
        private IGroup? selectedItemsGroup;
        private IGroup? selectedRecipesGroup;

        private AddRequest? request;
        private PendingOrigin? pendingOrigin;

        //The UnSpoil/UnPlant multi-origin chip's remembered target and its origin choices.
        private sealed record PendingOrigin(AddCandidate Target, NodeType Role, IReadOnlyCollection<IItem> Origins);

        //R5: defaults to the graph's own DefaultAssemblerQuality when the caller has one (MainWindow reads
        //GraphCanvas.Viewer.Graph), falling back to the cache default only when it doesn't.
        public AddBrowserViewModel(AddCatalog catalog, DataCache cache, AppSettings settings, Func<SKBitmap, int, IImage?> bake, IQuality? defaultAssemblerQuality = null) {
            this.catalog = catalog;
            this.cache = cache;
            this.settings = settings;
            this.bake = bake;
            Groups = new ReadOnlyObservableCollection<BrowserGroup>(groups);
            Sections = new ReadOnlyObservableCollection<BrowserSection>(sections);
            Qualities = [.. cache.AvailableQualities.Where(q => q.Enabled)];
            quality = defaultAssemblerQuality ?? cache.DefaultQuality;
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

        public IReadOnlyList<IQuality> Qualities { get; }
        public string QualityLabel => $"Quality: {Quality?.FriendlyName}";
        public bool ShowsQuality => catalog.ShowsQuality;

        public ReadOnlyObservableCollection<BrowserGroup> Groups { get; }
        public ReadOnlyObservableCollection<BrowserSection> Sections { get; }

        public string SearchPlaceholder => Segment == BrowserSegment.Recipes ? "Search recipes…" : "Search items…";

        //Origins-of-{item} (UnSpoil/UnPlant, 2+ origins) wins over Compatible-with-{item} (a link-drag
        //request) - the two never overlap in practice since role buttons only show for the Items segment.
        public string? ChipText => pendingOrigin is { } pending ? $"Origins of {pending.Target.Name}"
            : request?.Link is { Item.Item: IItem linkItem } ? $"Compatible with {linkItem.FriendlyName}"
            : null;
        public bool HasChip => ChipText is not null;

        //MainWindow's own AddRequest only tracks the advancing placement Location; the link itself lives
        //here so ClearChip() (and any future re-seed) is the one place that can change it.
        internal AddLinkContext? Link => request?.Link;

        private IItem? SelectedItem => SelectedCell?.Candidate.Item;

        //A link needing a Consumer hides Source, a link needing a Supplier hides Output; Pass-Through is
        //offered either way.
        public bool CanSource => SelectedItem is not null && Link?.NodeType != NewNodeType.Consumer;
        public bool CanPassthrough => SelectedItem is not null;
        public bool CanOutput => SelectedItem is not null && Link?.NodeType != NewNodeType.Supplier;
        public bool CanSpoil => SelectedItem?.SpoilResult is not null;
        public bool CanUnspoil => SelectedItem is not null && SelectedItem.SpoilOrigins.Count > 0;
        public bool CanPlant => SelectedItem?.PlantResult is not null;
        public bool CanUnplant => SelectedItem is not null && SelectedItem.PlantOrigins.Count > 0;
        public bool RolesVisible => Segment == BrowserSegment.Items && SelectedItem is not null;

        public bool ShowUnavailable {
            get => settings.ShowUnavailable;
            set {
                if (settings.ShowUnavailable == value)
                    return;
                settings.ShowUnavailable = value;
                Refresh();
            }
        }

        public bool ShowHidden {
            get => settings.ShowHidden;
            set {
                if (settings.ShowHidden == value)
                    return;
                settings.ShowHidden = value;
                Refresh();
            }
        }

        public bool IgnoreAssembler {
            get => settings.IgnoreAssemblerStatus;
            set {
                if (settings.IgnoreAssemblerStatus == value)
                    return;
                settings.IgnoreAssemblerStatus = value;
                Refresh();
            }
        }

        public void SelectGroup(IGroup group) {
            SetSelectedGroupForSegment(Segment, group);
            Refresh();
        }

        public void SelectCell(BrowserCell? cell) => SelectedCell = cell;

        //Gate fix 7, item 3: a link-drag's own "Add..." (Scope.All, Link set) always means a recipe search -
        //only a scope-less/link-less open (the toolbar, background menu, double-click) still defaults to Items.
        public void Seed(AddRequest request) {
            this.request = request;
            pendingOrigin = null;
            Segment = request.Link is not null || request.Scope == AddScope.Recipes ? BrowserSegment.Recipes : BrowserSegment.Items;
            SearchText = request.SeedText;
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

        //R9: the x clears both the link and any origins filter, and later picks land disconnected.
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

        //Double-click/Enter on a cell: an active origins chip completes that pick, otherwise the
        //segment decides (a recipe in Recipes, a Source node in Items).
        public void PlaceDefault() {
            if (pendingOrigin is { } pending) {
                Place(pending.Role, NodeDirection.Down);
                return;
            }
            if (Segment == BrowserSegment.Recipes)
                PlaceSelectedRecipe();
            else
                Place(NodeType.Supplier);
        }

        private void RaiseChipChanged() {
            OnPropertyChanged(nameof(ChipText));
            OnPropertyChanged(nameof(HasChip));
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
        }

        partial void OnSegmentChanged(BrowserSegment value) {
            Refresh();
            NotifyRoleProperties();
        }

        partial void OnSearchTextChanged(string value) => RefreshSections();
        partial void OnQualityChanged(IQuality? value) => OnPropertyChanged(nameof(QualityLabel));
        partial void OnSelectedCellChanged(BrowserCell? value) => NotifyRoleProperties();

        private IGroup? SelectedGroupForSegment(BrowserSegment forSegment) =>
            forSegment == BrowserSegment.Items ? selectedItemsGroup : selectedRecipesGroup;

        private void SetSelectedGroupForSegment(BrowserSegment forSegment, IGroup? group) {
            if (forSegment == BrowserSegment.Items)
                selectedItemsGroup = group;
            else
                selectedRecipesGroup = group;
        }

        private void Refresh() {
            RefreshGroups();
            RefreshSections();
        }

        //R11: a group only appears once it has at least one visible-by-availability entry for the segment.
        private void RefreshGroups() {
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
            if (current is null || !sorted.Contains(current))
                current = sorted.Count > 0 ? sorted[0] : null;
            SetSelectedGroupForSegment(Segment, current);

            groups.Clear();
            foreach (IGroup group in sorted)
                groups.Add(new BrowserGroup(group, HumanizeGroupName(group.FriendlyName, group.Name), bake(group.Icon, GroupIconSize), group.Equals(current)));
        }

        //Some groups/subgroups carry no localised name - FriendlyName then either mirrors the raw internal
        //key outright or is left as Factorio's own unresolved locale placeholder ("§§SG:EXTRACTION_ITEMS").
        //Strip the placeholder prefix and turn the remaining key into a readable phrase; a genuinely
        //localised name (distinct from Name, no placeholder) passes through untouched.
        internal static string Humanize(string friendlyName, string name) {
            if (!friendlyName.StartsWith("§§", StringComparison.Ordinal) && friendlyName != name)
                return friendlyName;
            string key = friendlyName.StartsWith("§§SG:", StringComparison.Ordinal) ? friendlyName[5..] : friendlyName.TrimStart('§');
            string words = key.Replace('_', ' ').Replace('-', ' ').Trim().ToLowerInvariant();
            return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
        }

        //The sidebar shows group names as-is (no upper-casing heading transform), so a humanized one gets
        //title-cased here; an already-localised name is left exactly as authored.
        private static string HumanizeGroupName(string friendlyName, string name) {
            string humanized = Humanize(friendlyName, name);
            return humanized == friendlyName ? friendlyName : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(humanized);
        }

        private void RefreshSections() {
            AddCandidate? previouslySelected = SelectedCell?.Candidate;

            sections.Clear();
            if (pendingOrigin is { } pending && Segment == BrowserSegment.Items) {
                sections.Add(new BrowserSection("Origins", [.. pending.Origins.Select(o => BuildCell(AddCatalog.Candidate(o)))]));
            } else if (request?.Link is { } link) {
                BuildLinkSections(link);
            } else {
                string text = SearchText.Trim();
                if (text.Length == 0)
                    BuildGroupSections();
                else
                    BuildSearchSection(text);
            }

            SelectedCell = previouslySelected is null
                ? null
                : sections.SelectMany(s => s.Cells).FirstOrDefault(c => c.Candidate.Equals(previouslySelected));
        }

        private void BuildGroupSections() {
            IGroup? selected = SelectedGroupForSegment(Segment);
            if (selected is null)
                return;

            foreach (ISubgroup subgroup in selected.Subgroups) {
                IEnumerable<AddCandidate> candidates = Segment == BrowserSegment.Items
                    ? subgroup.Items.Where(catalog.IsVisible).Select(AddCatalog.Candidate)
                    : subgroup.Recipes.Where(catalog.IsVisible).Select(AddCatalog.Candidate);
                var cells = candidates.Select(BuildCell).ToList();
                if (cells.Count > 0)
                    sections.Add(new BrowserSection(Humanize(subgroup.FriendlyName, subgroup.Name), cells));
            }
        }

        //R10: a recipe search also matches on ingredient/product name, not just the recipe's own name.
        private void BuildSearchSection(string text) {
            AddScope scope = Segment == BrowserSegment.Items ? AddScope.Items : AddScope.Recipes;
            var candidates = new List<AddCandidate>(catalog.Search(text, scope, link: null));

            if (Segment == BrowserSegment.Recipes) {
                var seen = new HashSet<IRecipe>(candidates.Where(c => c.Recipe is not null).Select(c => c.Recipe!));
                foreach (IRecipe recipe in cache.Recipes.Values) {
                    if (seen.Contains(recipe) || !catalog.IsVisible(recipe))
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

            sections.Add(new BrowserSection("Results", [.. candidates.Select(BuildCell)]));
        }

        //Link-drag "Add..." (a dragged tab's Compatible-with-{item} chip): Items narrows to the dragged
        //item itself, Recipes to every recipe AddCatalog.IsCompatible allows for that link's direction -
        //SearchText (the ⌘↵-seeded query) still narrows either list further.
        private void BuildLinkSections(AddLinkContext link) {
            string text = SearchText.Trim();
            if (Segment == BrowserSegment.Items) {
                if (link.Item.Item is IItem linkItem && Matches(linkItem.FriendlyName, text))
                    sections.Add(new BrowserSection("Items", [BuildCell(AddCatalog.Candidate(linkItem))]));
            } else {
                var candidates = cache.Recipes.Values.Where(r => catalog.IsVisible(r) && catalog.IsCompatible(r, link) && Matches(r.FriendlyName, text)).Select(AddCatalog.Candidate);
                sections.Add(new BrowserSection("Recipes", [.. candidates.Select(BuildCell)]));
            }
        }

        private static bool Matches(string name, string searchText) =>
            searchText.Length == 0 || name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);

        private BrowserCell BuildCell(AddCandidate candidate) => new(
            candidate,
            candidate.Name,
            bake(candidate.Entry.Icon, CellIconSize),
            candidate.Entry.Available,
            SelectedCell is not null && candidate.Equals(SelectedCell.Candidate));
    }
}
