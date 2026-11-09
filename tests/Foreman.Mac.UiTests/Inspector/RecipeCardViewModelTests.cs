using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.ViewModels;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    public class RecipeCardViewModelTests {
        private static async Task<IRecipe> GetIronPlateRecipeAsync() {
            DataCache cache = await InspectorTestSupport.GetCacheAsync();
            return cache.Recipes["iron-plate"];
        }

        [AvaloniaFact]
        public async Task Build_IronPlateRecipe_FlowShowsIngredientArrowProduct() {
            IRecipe recipe = await GetIronPlateRecipeAsync();
            RecipeCardViewModel card = RecipeCardViewModel.Build(recipe, []);

            List<string> texts = [.. card.Flow.Select(item => item.Text)];
            Assert.Equal(["1× Iron ore", "→", "1× Iron plate"], texts);
            Assert.True(card.Flow.Single(item => item.Text == "→").IsArrow);
        }

        [AvaloniaFact]
        public async Task Build_IronPlateRecipe_CraftingTimeAndBaseRateMatchBrief() {
            IRecipe recipe = await GetIronPlateRecipeAsync();
            RecipeCardViewModel card = RecipeCardViewModel.Build(recipe, []);

            Assert.Equal("Crafting time: 3.2s", card.CraftingTimeText);
            Assert.Equal("Base rate: 0.3125/s", card.BaseRateText);
        }
    }
}
