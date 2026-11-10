using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Models.Research {
    public sealed class ResearchState : IEquatable<ResearchState> {
        public static ResearchState None { get; } = new(null, new Dictionary<ResearchField, double>(), new Dictionary<ResearchField, double>(),
            new Dictionary<string, double>(), new Dictionary<string, double>(), []);

        public ResearchSource? Source { get; }
        public IReadOnlyDictionary<ResearchField, double> Values { get; }
        public IReadOnlyDictionary<ResearchField, double> SyncedValues { get; }
        public IReadOnlyDictionary<string, double> RecipeProductivity { get; }
        public IReadOnlyDictionary<string, double> SyncedRecipeProductivity { get; }
        public IReadOnlyList<ResearchTechnology> Technologies { get; }

        public ResearchState(ResearchSource? source, IReadOnlyDictionary<ResearchField, double> values, IReadOnlyDictionary<ResearchField, double> syncedValues,
            IReadOnlyDictionary<string, double> recipeProductivity, IReadOnlyDictionary<string, double> syncedRecipeProductivity, IReadOnlyList<ResearchTechnology> technologies) {
            Source = source;
            Values = values;
            SyncedValues = syncedValues;
            RecipeProductivity = recipeProductivity;
            SyncedRecipeProductivity = syncedRecipeProductivity;
            Technologies = technologies;
        }

        public double? Get(ResearchField field) => Values.TryGetValue(field, out double value) ? value : null;
        public double? GetSynced(ResearchField field) => SyncedValues.TryGetValue(field, out double value) ? value : null;

        public double? MiningProductivity => Get(ResearchField.MiningProductivity);
        public double? LabSpeed => Get(ResearchField.LabSpeed);
        public double? LabProductivity => Get(ResearchField.LabProductivity);
        public double? InserterStackBonus => Get(ResearchField.InserterStackBonus);
        public double? BulkInserterCapacityBonus => Get(ResearchField.BulkInserterCapacityBonus);
        public double? BeltStackBonus => Get(ResearchField.BeltStackBonus);
        public double? RobotSpeed => Get(ResearchField.RobotSpeed);
        public double? RobotCargo => Get(ResearchField.RobotCargo);
        public double? RobotBattery => Get(ResearchField.RobotBattery);
        public double? TrainBrakingForce => Get(ResearchField.TrainBrakingForce);

        public bool IsHandEdited(ResearchField field) => Get(field) != GetSynced(field);

        public bool IsRecipeHandAdded(string recipe) => RecipeProductivity.ContainsKey(recipe) && !SyncedRecipeProductivity.ContainsKey(recipe);

        public bool IsRecipeHandEdited(string recipe) =>
            RecipeProductivity.TryGetValue(recipe, out double value) && SyncedRecipeProductivity.TryGetValue(recipe, out double synced) && value != synced;

        public int HandEditCount =>
            ResearchFields.All.Count(IsHandEdited) + RecipeProductivity.Keys.Count(recipe => IsRecipeHandAdded(recipe) || IsRecipeHandEdited(recipe));

        public ResearchState WithValue(ResearchField field, double? value) {
            var values = new Dictionary<ResearchField, double>(Values);
            if (value is double v)
                values[field] = v;
            else
                values.Remove(field);
            return new ResearchState(Source, values, SyncedValues, RecipeProductivity, SyncedRecipeProductivity, Technologies);
        }

        public ResearchState WithRecipe(string recipe, double value) {
            var recipes = new Dictionary<string, double>(RecipeProductivity) { [recipe] = value };
            return new ResearchState(Source, Values, SyncedValues, recipes, SyncedRecipeProductivity, Technologies);
        }

        public ResearchState Revert(ResearchField field) => WithValue(field, GetSynced(field));

        public ResearchState RevertRecipe(string recipe) {
            var recipes = new Dictionary<string, double>(RecipeProductivity);
            if (SyncedRecipeProductivity.TryGetValue(recipe, out double synced))
                recipes[recipe] = synced;
            else
                recipes.Remove(recipe);
            return new ResearchState(Source, Values, SyncedValues, recipes, SyncedRecipeProductivity, Technologies);
        }

        public ResearchState RevertAll() =>
            new(Source, new Dictionary<ResearchField, double>(SyncedValues), SyncedValues, new Dictionary<string, double>(SyncedRecipeProductivity), SyncedRecipeProductivity, Technologies);

        public bool Equals(ResearchState? other) =>
            other is not null
            && Equals(Source, other.Source)
            && SameEntries(Values, other.Values)
            && SameEntries(SyncedValues, other.SyncedValues)
            && SameEntries(RecipeProductivity, other.RecipeProductivity)
            && SameEntries(SyncedRecipeProductivity, other.SyncedRecipeProductivity)
            && Technologies.SequenceEqual(other.Technologies);

        public override bool Equals(object? obj) => Equals(obj as ResearchState);

        public override int GetHashCode() => HashCode.Combine(Source, Values.Count, SyncedValues.Count, RecipeProductivity.Count, SyncedRecipeProductivity.Count, Technologies.Count);

        private static bool SameEntries<TKey>(IReadOnlyDictionary<TKey, double> a, IReadOnlyDictionary<TKey, double> b) where TKey : notnull =>
            a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out double other) && other == pair.Value);
    }
}
