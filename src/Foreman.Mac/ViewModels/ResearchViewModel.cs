using Foreman.Models.Research;
using System;

namespace Foreman.Mac.ViewModels {
    public sealed class ResearchViewModel(ProductionGraph graph) {
        public ResearchState State => graph.Research;

        public double? DisplayValue(ResearchField field) =>
            State.Get(field) is double stored ? ToDisplay(stored, ResearchFields.IsCount(field)) : null;

        public double RecipeDisplayPercent(string recipe) =>
            State.RecipeProductivity.TryGetValue(recipe, out double stored) ? ToDisplay(stored, isCount: false) : 0;

        public void SetField(ResearchField field, double? displayValue) {
            double? stored = displayValue is double shown ? FromDisplay(shown, ResearchFields.IsCount(field)) : null;
            if (stored == State.Get(field) || SameDisplay(DisplayValue(field), displayValue))
                return;
            graph.Research = State.WithValue(field, stored);
        }

        public void Revert(ResearchField field) => graph.Research = State.Revert(field);

        public void RevertAll() => graph.Research = State.RevertAll();

        public void SetRecipe(string recipe, double displayPercent) {
            if (SameDisplay(State.RecipeProductivity.ContainsKey(recipe) ? RecipeDisplayPercent(recipe) : null, displayPercent))
                return;
            graph.Research = State.WithRecipe(recipe, FromDisplay(displayPercent, isCount: false));
        }

        public void RevertRecipe(string recipe) => graph.Research = State.RevertRecipe(recipe);

        private static double ToDisplay(double stored, bool isCount) => isCount ? stored : Math.Round(stored * 100, 10);

        private static double FromDisplay(double shown, bool isCount) => isCount ? Math.Round(shown) : shown / 100;

        private static bool SameDisplay(double? current, double? requested) =>
            current is double a && requested is double b && Math.Round(a, 2) == Math.Round(b, 2);
    }
}
