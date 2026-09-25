using Avalonia.Media.Imaging;
using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Views;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    //One flow-row cell: either an ingredient/product icon+count+name, or the "→" separator
    //between the ingredient and product runs.
    public sealed class RecipeCardFlowItem {
        public bool IsArrow { get; init; }
        public Bitmap? Icon { get; init; }
        public string Text { get; init; } = "";
    }

    //Backs RecipeCardView (contract §03 CSS 708-718): the recipe card folded into the inspector's
    //identity header, replacing the canvas's retired hover tooltip. Reads the same recipe data
    //RecipePainter used (Canvas/RecipePainter.cs) rather than re-deriving anything.
    public sealed class RecipeCardViewModel {
        private const int IconSize = 20;

        public IReadOnlyList<RecipeCardFlowItem> Flow { get; init; } = [];
        public string CraftingTimeText { get; init; } = "";
        public string BaseRateText { get; init; } = "";

        public static RecipeCardViewModel Build(IRecipe recipe, Dictionary<SKBitmap, Bitmap> iconCache) {
            List<RecipeCardFlowItem> flow = [.. recipe.IngredientList.Select(i => Item(recipe.IngredientSet[i], recipe.GetIngredientFriendlyName(i), i.Icon, iconCache))];
            flow.Add(new RecipeCardFlowItem { IsArrow = true, Text = "→" });
            flow.AddRange(recipe.ProductList.Select(p => Item(recipe.ProductSet[p], recipe.GetProductFriendlyName(p), p.Icon, iconCache)));

            double totalProductCount = recipe.ProductList.Sum(p => recipe.ProductSet[p]);
            double baseRate = recipe.Time > 0 ? totalProductCount / recipe.Time : 0;
            return new RecipeCardViewModel {
                Flow = flow,
                CraftingTimeText = $"Crafting time: {recipe.Time.ToString("0.##", DisplayCulture.Format)}s",
                BaseRateText = $"Base rate: {baseRate.ToString("0.####", DisplayCulture.Format)}/s",
            };
        }

        private static RecipeCardFlowItem Item(double count, string name, SKBitmap? icon, Dictionary<SKBitmap, Bitmap> iconCache) => new() {
            Icon = icon is null ? null : IconBaker.GetOrBake(iconCache, icon, IconSize),
            Text = $"{count.ToString("0.##", DisplayCulture.Format)}× {name}",
        };
    }
}
