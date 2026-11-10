using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Services {
    public enum AddKind { Item, Recipe }

    public sealed record AddCandidate(AddKind Kind, IDataObjectBase Entry, IItem? Item, IRecipe? Recipe, string Name, string KindLabel);

    public sealed class AddCatalog {
        private readonly DataCache cache;
        private readonly AppSettings settings;

        public AddCatalog(DataCache cache, AppSettings settings) {
            this.cache = cache;
            this.settings = settings;
        }

        public bool ShowsQuality => cache.AvailableQualities.Count() > 1;

        public IQuality LowestQuality => RecipeQualityLock.LowestEnabled(cache);

        public IReadOnlyList<AddCandidate> Search(string query, AddScope scope, AddLinkContext? link, int limit = 200) {
            string q = query.Trim();
            if (q.Length == 0 && link is null)
                return [];

            var rows = new List<(int tier, AddCandidate row)>();

            if (scope != AddScope.Recipes && link is null)
                foreach (IItem item in cache.Items.Values)
                    if (IsVisible(item) && Matches(item, q, out int itemTier))
                        rows.Add((itemTier, Candidate(item)));

            if (scope != AddScope.Items)
                foreach (IRecipe recipe in cache.Recipes.Values)
                    if (IsVisible(recipe) && (link is null || IsCompatible(recipe, link)) && Matches(recipe, q, out int recipeTier))
                        rows.Add((recipeTier, Candidate(recipe)));

            return [.. rows.OrderBy(r => r.tier)
                           .ThenBy(r => r.row.Name, StringComparer.CurrentCultureIgnoreCase)
                           .ThenBy(r => r.row.Kind)
                           .Select(r => r.row)
                           .Take(limit)];
        }

        public bool IsCompatible(IRecipe recipe, AddLinkContext link) {
            if (link.Item.Item is not IItem keyItem)
                return false;
            bool includeConsumers = link.NodeType == NewNodeType.Consumer;
            bool includeSuppliers = link.NodeType == NewNodeType.Supplier;
            bool ignoreAssembler = settings.IgnoreAssemblerStatus;
            FRange range = link.TempRange;
            return (includeConsumers && recipe.IngredientSet.ContainsKey(keyItem) && (range.Ignore || recipe.IngredientTemperatureMap[keyItem].Contains(range))) ||
                   (includeSuppliers && recipe.ProductSet.ContainsKey(keyItem) && (range.Ignore || range.Contains(recipe.ProductTemperatureMap[keyItem]))) ||
                   (includeConsumers && keyItem.FuelsEntities.Count > 0 && recipe.Assemblers.Any(a => a.Fuels.Contains(keyItem) && (a.Enabled || ignoreAssembler))) ||
                   (includeSuppliers && keyItem.FuelOrigin is IItem fuelOrigin && recipe.Assemblers.Any(a => a.Fuels.Contains(fuelOrigin) && (a.Enabled || ignoreAssembler)));
        }

        public bool IsVisible(IDataObjectBase entry) {
            if (!entry.Available && !settings.ShowUnavailable)
                return false;
            if (!entry.Enabled && !settings.ShowHidden)
                return false;
            return entry is not IRecipe recipe || settings.IgnoreAssemblerStatus || recipe.Assemblers.Any(a => a.Enabled);
        }

        private static bool Matches(IDataObjectBase entry, string q, out int tier) {
            if (q.Length == 0) { tier = 1; return true; }
            string friendly = entry.LFriendlyName;
            if (friendly.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) { tier = 0; return true; }
            if (friendly.Contains(q, StringComparison.CurrentCultureIgnoreCase) || entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) { tier = 1; return true; }
            tier = 2;
            return false;
        }

        public static AddCandidate Candidate(IDataObjectBase entry) => entry switch {
            IItem item => Candidate(item),
            IRecipe recipe => Candidate(recipe),
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };

        private static AddCandidate Candidate(IItem item) => new(AddKind.Item, item, item, null, item.FriendlyName, "item");
        private static AddCandidate Candidate(IRecipe recipe) => new(AddKind.Recipe, recipe, null, recipe, recipe.FriendlyName, "recipe");
    }
}
