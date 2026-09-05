using Avalonia.Headless.XUnit;
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

        [AvaloniaFact]
        public void LinkContext_ListsTheItemFirstThenOnlyCompatibleRecipes() {
            CatalogFixture fx = CatalogFixture.WithProductionChain();
            var catalog = new AddCatalog(fx.Cache, new AppSettings());
            AddLinkContext link = fx.LinkContextFor("Iron Plate", NewNodeType.Supplier);
            var rows = catalog.Search("", AddScope.All, link);
            Assert.Equal("Iron Plate|item", $"{rows[0].Name}|{rows[0].KindLabel}");
            Assert.Equal(["Iron Plate"], rows.Skip(1).Select(r => r.Name));
            var consumers = catalog.Search("", AddScope.All, fx.LinkContextFor("Iron Plate", NewNodeType.Consumer));
            Assert.Equal(["Iron Gear"], consumers.Skip(1).Select(r => r.Name));
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

        [Fact]
        public void ShowsQuality_OnlyWhenMoreThanOneQualityIsAvailable() {
            Assert.False(new AddCatalog(CatalogFixture.WithItemsAndRecipes(items: ["A"], recipes: []).Cache, new AppSettings()).ShowsQuality);
            Assert.True(new AddCatalog(CatalogFixture.WithQualities(["normal", "legendary"]).Cache, new AppSettings()).ShowsQuality);
        }
    }
}
