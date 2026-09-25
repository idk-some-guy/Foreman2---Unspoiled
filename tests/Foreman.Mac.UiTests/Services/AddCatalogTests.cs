using Avalonia.Headless.XUnit;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Services {
    public class AddCatalogTests {
        [Fact]
        public void PrefixMatchesRankAboveSubstringMatches_AndItemsBeforeSameNamedRecipes() {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Iron Plate", "Iron Plating", "Cast Iron Plate"], recipes: ["Iron Plate", "Iron Plating"]);
            var catalog = new AddCatalog(fx.Cache, new AppSettings());
            var rows = catalog.Search("iron pl", AddScope.All, null);
            Assert.Equal(["Iron Plate|item", "Iron Plate|recipe", "Iron Plating|item", "Iron Plating|recipe", "Cast Iron Plate|item"],
                [.. rows.Select(r => $"{r.Name}|{r.KindLabel}")]);
        }

        [Fact]
        public void ScopeFiltersKinds() {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Iron Plate"], recipes: ["Iron Plate"]);
            var catalog = new AddCatalog(fx.Cache, new AppSettings());
            Assert.All(catalog.Search("iron", AddScope.Items, null), r => Assert.Equal(AddKind.Item, r.Kind));
            Assert.All(catalog.Search("iron", AddScope.Recipes, null), r => Assert.Equal(AddKind.Recipe, r.Kind));
        }

        [Fact]
        public void EmptyQueryWithoutLinkContext_ReturnsNothing() {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Iron Plate"], recipes: ["Iron Plate"]);
            Assert.Empty(new AddCatalog(fx.Cache, new AppSettings()).Search("", AddScope.All, null));
        }

        //The radial's Source/Output/UnSpoil/UnPlant slices own the dragged item's own row now,
        //so a link-scoped search lists only the recipes AddCatalog.IsCompatible allows - no item row.
        [AvaloniaFact]
        public void LinkContext_ListsOnlyCompatibleRecipes() {
            CatalogFixture fx = CatalogFixture.WithProductionChain();
            var catalog = new AddCatalog(fx.Cache, new AppSettings());
            AddLinkContext supplierLink = fx.LinkContextFor("Iron Plate", NewNodeType.Supplier);
            var rows = catalog.Search("", AddScope.All, supplierLink);
            Assert.Equal(["Iron Plate"], rows.Select(r => r.Name));
            Assert.All(rows, r => Assert.Equal(AddKind.Recipe, r.Kind));

            var consumers = catalog.Search("", AddScope.All, fx.LinkContextFor("Iron Plate", NewNodeType.Consumer));
            Assert.Equal(["Iron Gear"], consumers.Select(r => r.Name));
        }

        //Ports the retired on-canvas chooser's temperature-filter test: a temperature-dependent fluid link
        //only matches recipes whose accepted ingredient range covers it.
        [AvaloniaFact]
        public void LinkContext_TemperatureRange_ExcludesOutOfRangeIngredientRecipes() {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: [], recipes: []);
            var steam = fx.AddFluid("steam", defaultTemperature: 165);
            fx.AddIngredientRecipe("Turbine Compatible", steam, minTemp: 100, maxTemp: 200);
            fx.AddIngredientRecipe("Turbine Incompatible", steam, minTemp: 500, maxTemp: 1000);
            AddLinkContext link = fx.LinkContextFor("steam", NewNodeType.Consumer, new FRange(165, 165, false));

            var catalog = new AddCatalog(fx.Cache, new AppSettings());
            var names = catalog.Search("turbine", AddScope.Recipes, link).Select(r => r.Name).ToList();

            Assert.Contains("Turbine Compatible", names);
            Assert.DoesNotContain("Turbine Incompatible", names);
        }

        [Fact]
        public void HiddenAndUnavailableFollowSettingsLive() {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Iron Plate"], recipes: ["Iron Plate"], hidden: ["Iron Plate"]);
            var settings = new AppSettings { ShowHidden = false };
            var catalog = new AddCatalog(fx.Cache, settings);
            Assert.Empty(catalog.Search("iron", AddScope.All, null));
            settings.ShowHidden = true;
            Assert.NotEmpty(catalog.Search("iron", AddScope.All, null));
        }

        //Ports the retired on-canvas chooser's Ignore Assembler toggle test.
        [Fact]
        public void IgnoreAssemblerStatus_IncludesRecipesWithNoEnabledAssembler() {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: [], recipes: ["Iron Plate"]);
            fx.Assembler.Enabled = false;
            var settings = new AppSettings { IgnoreAssemblerStatus = false };
            var catalog = new AddCatalog(fx.Cache, settings);
            Assert.Empty(catalog.Search("iron", AddScope.Recipes, null));

            settings.IgnoreAssemblerStatus = true;
            Assert.NotEmpty(catalog.Search("iron", AddScope.Recipes, null));
        }

        [Fact]
        public void ShowsQuality_OnlyWhenMoreThanOneQualityIsAvailable() {
            Assert.False(new AddCatalog(CatalogFixture.WithItemsAndRecipes(items: ["A"], recipes: []).Cache, new AppSettings()).ShowsQuality);
            Assert.True(new AddCatalog(CatalogFixture.WithQualities(["normal", "legendary"]).Cache, new AppSettings()).ShowsQuality);
        }
    }
}
