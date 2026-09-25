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
    //RecipeCardViewModel in isolation (contract §03 CSS 708-718): the recipe card folded into the
    //identity header, replacing the canvas's retired hover tooltip. Built from the real vanilla iron-plate
    //recipe (Factorio 2.0 Vanilla preset: 1 iron ore -> 1 iron plate, 3.2s) - the brief's own worked
    //example uses illustrative numbers, but the crafting-time/base-rate formula it pins matches exactly.
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
