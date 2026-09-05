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

    //Group sorting/visibility counting ports RecipeChooserPanel.GetSortedGroups/ItemChooserPanel (Task 7 retires both).
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

        public AddBrowserViewModel(AddCatalog catalog, DataCache cache, AppSettings settings, Func<SKBitmap, int, IImage?> bake) {
            this.catalog = catalog;
            this.cache = cache;
            this.settings = settings;
            this.bake = bake;
            Groups = new ReadOnlyObservableCollection<BrowserGroup>(groups);
            Sections = new ReadOnlyObservableCollection<BrowserSection>(sections);
            Qualities = [.. cache.AvailableQualities];
            quality = cache.DefaultQuality;
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

        public IReadOnlyList<IQuality> Qualities { get; }
        public string QualityLabel => $"Quality: {Quality?.FriendlyName}";
        public bool ShowsQuality => catalog.ShowsQuality;

        public ReadOnlyObservableCollection<BrowserGroup> Groups { get; }
        public ReadOnlyObservableCollection<BrowserSection> Sections { get; }

        public string SearchPlaceholder => Segment == BrowserSegment.Recipes ? "Search recipes…" : "Search items…";

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

        public void Seed(AddRequest request) {
            Segment = request.Scope == AddScope.Recipes ? BrowserSegment.Recipes : BrowserSegment.Items;
            SearchText = request.SeedText;
        }

        partial void OnSegmentChanged(BrowserSegment value) => Refresh();
        partial void OnSearchTextChanged(string value) => RefreshSections();
        partial void OnQualityChanged(IQuality? value) => OnPropertyChanged(nameof(QualityLabel));

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
                groups.Add(new BrowserGroup(group, group.FriendlyName, bake(group.Icon, GroupIconSize), group.Equals(current)));
        }

        private void RefreshSections() {
            AddCandidate? previouslySelected = SelectedCell?.Candidate;

            sections.Clear();
            string text = SearchText.Trim();
            if (text.Length == 0)
                BuildGroupSections();
            else
                BuildSearchSection(text);

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
                    ? subgroup.Items.Where(catalog.IsVisible).Select(CandidateFor)
                    : subgroup.Recipes.Where(catalog.IsVisible).Select(CandidateFor);
                var cells = candidates.Select(BuildCell).ToList();
                if (cells.Count > 0)
                    sections.Add(new BrowserSection(subgroup.FriendlyName, cells));
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
                    candidates.Add(CandidateFor(recipe));
                    seen.Add(recipe);
                }
            }

            sections.Add(new BrowserSection("Results", [.. candidates.Select(BuildCell)]));
        }

        private static AddCandidate CandidateFor(IDataObjectBase entry) => entry switch {
            IItem item => new AddCandidate(AddKind.Item, item, item, null, item.FriendlyName, "item"),
            IRecipe recipe => new AddCandidate(AddKind.Recipe, recipe, null, recipe, recipe.FriendlyName, "recipe"),
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };

        private BrowserCell BuildCell(AddCandidate candidate) => new(
            candidate,
            candidate.Name,
            bake(candidate.Entry.Icon, CellIconSize),
            candidate.Entry.Available,
            SelectedCell is not null && candidate.Equals(SelectedCell.Candidate));
    }
}
