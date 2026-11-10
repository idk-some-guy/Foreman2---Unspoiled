using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    public sealed class AddBrowserPlacementTests {
        private static ChooserCell CellFor(AddBrowserWindow window, BrowserCell cell) =>
            window.GetVisualDescendants().OfType<ChooserCell>().First(c => c.FullName == cell.Label);

        [AvaloniaFact]
        public void SourcePassthroughOutputPlaceTheMatchingNodeKinds_AndKeepTheWindowOpen() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(100, 100), AddScope.Items, SeedText: "iron plate"));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.SelectCell(vm.Sections[0].Cells[0]);

            vm.Place(NodeType.Supplier);
            vm.Place(NodeType.Passthrough);
            vm.Place(NodeType.Consumer);

            Assert.Equal(3, shell.Graph.Nodes.Count());
            Assert.Single(shell.Graph.Nodes.OfType<SupplierNode>());
            Assert.Single(shell.Graph.Nodes.OfType<PassthroughNode>());
            Assert.Single(shell.Graph.Nodes.OfType<ConsumerNode>());
            Assert.Equal([100, 220, 340], shell.Graph.Nodes.Select(n => n.Location.Y).OrderBy(y => y));
            Assert.True(shell.Window.BrowserWindow!.IsVisible);
            Assert.Equal(340, shell.Window.GraphCanvas.PlacementFlashNode!.ViewModel.Location.Y);
        }

        [AvaloniaFact]
        public void RecipeDoubleClickPlacesARecipeNodeWithTheSelectedQuality() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            DataCache cache = shell.Window.DataCache!;
            IQuality legendary = cache.Qualities["legendary"];

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "wooden chest"));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.Quality = legendary;
            BrowserCell cell = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Recipe);
            vm.SelectCell(cell);

            vm.PlaceSelectedRecipe();

            RecipeNode node = Assert.Single(shell.Graph.Nodes.OfType<RecipeNode>());
            Assert.Equal("legendary", node.BaseRecipe.Quality!.Name);
        }

        [AvaloniaFact]
        public void RoleButtonsFollowTheSelectedItem() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items, SeedText: "wood"));
            AddBrowserViewModel vm = shell.BrowserVm;

            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "wood"));
            Assert.False(vm.CanSpoil);

            vm.SearchText = "raw fish";
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "raw-fish"));
            Assert.True(vm.CanSpoil);

            vm.SearchText = "spoilage";
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "spoilage"));
            Assert.True(vm.CanUnspoil);

            vm.Segment = BrowserSegment.Recipes;
            Assert.False(vm.RolesVisible);
        }

        [AvaloniaFact]
        public void UnspoilWithSeveralOriginsShowsTheOriginsChip_ThenPlacesTheChosenOrigin() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items, SeedText: "spoilage"));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "spoilage"));

            vm.Place(NodeType.Spoil, NodeDirection.Down);

            Assert.Equal("Origins of Spoilage", vm.ChipText);
            List<string> origins = [.. vm.Sections.SelectMany(s => s.Cells).Select(c => c.Candidate.Entry.Name)];
            Assert.Contains("raw-fish", origins);
            Assert.DoesNotContain("spoilage", origins);

            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "raw-fish"));
            vm.Place(NodeType.Spoil, NodeDirection.Down);

            SpoilNode node = Assert.Single(shell.Graph.Nodes.OfType<SpoilNode>());
            Assert.Equal("raw-fish", node.InputItem.Item!.Name);
            Assert.Equal("spoilage", node.OutputItem.Item!.Name);

            vm.ClearChip();
            Assert.Null(vm.ChipText);
            Assert.Contains(vm.Sections.SelectMany(s => s.Cells), c => c.Candidate.Entry.Name == "spoilage");
        }

        [AvaloniaFact]
        public void RoleButtonsRespectTheLinkDirection() {
            using ShellFixture shell = ShellFixture.OpenWithVanillaAndOneSupplier(out AddLinkContext consumerLink);

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(300, 0), AddScope.All, consumerLink));
            AddBrowserViewModel vm = shell.BrowserVm;
            //Role buttons are Items-only and a link-drag Add opens on Recipes.
            vm.Segment = BrowserSegment.Items;
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First());
            Assert.False(vm.CanSource);
            Assert.True(vm.CanPassthrough);
            Assert.True(vm.CanOutput);

            AddLinkContext supplierLink = consumerLink with { NodeType = NewNodeType.Supplier };
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(300, 0), AddScope.All, supplierLink));
            vm.Segment = BrowserSegment.Items;
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First());
            Assert.True(vm.CanSource);
            Assert.True(vm.CanPassthrough);
            Assert.False(vm.CanOutput);
        }

        [AvaloniaFact]
        public void EnterOnAnOriginCellCompletesTheOriginPick_AndClearsTheChip() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items, SeedText: "spoilage"));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "spoilage"));
            vm.Place(NodeType.Spoil, NodeDirection.Down);
            Assert.Equal("Origins of Spoilage", vm.ChipText);

            BrowserCell originCell = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "raw-fish");
            vm.SelectCell(originCell);
            Dispatcher.UIThread.RunJobs();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            ChooserCell cellButton = CellFor(window, originCell);
            CanvasKeys.KeyDownOn(cellButton, Key.Enter);

            SpoilNode node = Assert.Single(shell.Graph.Nodes.OfType<SpoilNode>());
            Assert.Equal("raw-fish", node.InputItem.Item!.Name);
            Assert.Empty(shell.Graph.Nodes.OfType<SupplierNode>());
            Assert.Null(vm.ChipText);
        }

        [AvaloniaFact]
        public void LinkContextShowsTheCompatibleChip_FiltersBothSegments_AndPlacesLinkedNodes() {
            using ShellFixture shell = ShellFixture.OpenWithVanillaAndOneSupplier(out AddLinkContext link);
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(300, 0), AddScope.All, link));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.Segment = BrowserSegment.Items;

            Assert.Equal("Compatible with Iron plate", vm.ChipText);
            Assert.Equal(["Iron plate"], vm.Sections.SelectMany(s => s.Cells).Select(c => c.Label));

            vm.Segment = BrowserSegment.Recipes;
            Assert.NotEmpty(vm.Sections.SelectMany(s => s.Cells));
            Assert.All(vm.Sections.SelectMany(s => s.Cells), c => Assert.Contains(link.Item.Item!, c.Candidate.Recipe!.IngredientSet.Keys));

            vm.SelectCell(vm.Sections[0].Cells[0]);
            vm.PlaceSelectedRecipe();
            Assert.Single(shell.Graph.NodeLinks);

            vm.ClearChip();
            Assert.Null(vm.ChipText);

            vm.Segment = BrowserSegment.Items;
            vm.SearchText = "iron plate";
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Item));
            vm.Place(NodeType.Supplier);
            Assert.Single(shell.Graph.NodeLinks);
        }

        [AvaloniaFact]
        public void PlacingAfterTheOriginNodeWasDeletedStillPlaces_Disconnected_NoException() {
            using ShellFixture shell = ShellFixture.OpenWithVanillaAndOneSupplier(out AddLinkContext link);
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(300, 0), AddScope.All, link));
            AddBrowserViewModel vm = shell.BrowserVm;

            shell.Window.GraphCanvas.Viewer.Session.Editor.DeleteNode(link.OriginElement.ViewModel.Id);

            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First());
            vm.Place(NodeType.Consumer);

            Assert.Single(shell.Graph.Nodes);
            Assert.Empty(shell.Graph.NodeLinks);
        }

        [AvaloniaFact]
        public void PaletteKeepsItsOwnAnchorAndLink_EvenWhenTheBrowserPlacesInBetween() {
            using ShellFixture shell = ShellFixture.OpenWithVanillaAndOneSupplier(out AddLinkContext link);
            var before = shell.Graph.Nodes.ToList();

            var toolbarAnchor = new DrawingPoint(500, 500);
            shell.Window.GraphCanvas.AddItemAsync(toolbarAnchor);
            QuickAddPalette palette = shell.Window.GraphCanvas.OpenPalette!;

            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.All, link));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.SelectCell(vm.Sections.SelectMany(s => s.Cells).First());
            vm.Place(NodeType.Consumer);

            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";

            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);

            SupplierNode paletteNode = Assert.Single(shell.Graph.Nodes.Except(before).OfType<SupplierNode>());
            Assert.Equal(toolbarAnchor.X, paletteNode.Location.X);
            Assert.Equal(toolbarAnchor.Y, paletteNode.Location.Y);
            Assert.Single(shell.Graph.NodeLinks);
        }

        [AvaloniaFact]
        public void LinkContextWithSearchText_OnlyShowsMatchingCompatibleRecipes() {
            using ShellFixture shell = ShellFixture.OpenWithVanillaAndOneSupplier(out AddLinkContext link);
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(300, 0), AddScope.All, link));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.Segment = BrowserSegment.Recipes;
            int unfiltered = vm.Sections.SelectMany(s => s.Cells).Count();

            vm.SearchText = "gear";

            List<string> labels = [.. vm.Sections.SelectMany(s => s.Cells).Select(c => c.Label)];
            Assert.NotEmpty(labels);
            Assert.True(labels.Count < unfiltered);
            Assert.All(labels, l => Assert.Contains("gear", l, StringComparison.CurrentCultureIgnoreCase));
        }

        [AvaloniaFact]
        public void RealPointerClickOnARecipeCellPlacesItImmediately() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "iron plate"));
            AddBrowserViewModel vm = shell.BrowserVm;
            BrowserCell cell = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Recipe);
            Dispatcher.UIThread.RunJobs();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            ChooserCell cellButton = CellFor(window, cell);
            RealClick.On(cellButton);

            Assert.Single(shell.Graph.Nodes.OfType<RecipeNode>());
            Assert.True(window.IsVisible);
        }

        //Settle the entrance animation first, or the two clicks land at different points and never count as a double-click.
        [AvaloniaFact]
        public void DoubleClickSequenceOnARecipeCellPlacesExactlyOnce() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "iron plate"));
            AddBrowserViewModel vm = shell.BrowserVm;
            BrowserCell cell = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Recipe);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            ChooserCell cellButton = CellFor(window, cell);
            RealClick.DoubleClick(cellButton);

            Assert.Single(shell.Graph.Nodes.OfType<RecipeNode>());
        }

        [AvaloniaFact]
        public void TwoSeparateSingleClicksOnARecipeCellPlaceTwice() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "iron plate"));
            AddBrowserViewModel vm = shell.BrowserVm;
            BrowserCell cell = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Recipe);
            Dispatcher.UIThread.RunJobs();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            ChooserCell cellButton = CellFor(window, cell);
            RealClick.TwoSeparateClicks(cellButton);

            Assert.Equal(2, shell.Graph.Nodes.OfType<RecipeNode>().Count());
        }

        [AvaloniaFact]
        public void RealPointerClickOnAnItemCellDoesNotPlace_SelectionOnly() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items, SeedText: "iron plate"));
            AddBrowserViewModel vm = shell.BrowserVm;
            BrowserCell cell = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Item);
            Dispatcher.UIThread.RunJobs();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            ChooserCell cellButton = CellFor(window, cell);
            RealClick.On(cellButton);

            Assert.Empty(shell.Graph.Nodes);
            Assert.Equal(cell.Candidate, vm.SelectedCell?.Candidate);
        }

        [AvaloniaFact]
        public void EnterOnASelectedCellPlaces_EscapeHidesTheWindow() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items, SeedText: "iron plate"));
            AddBrowserViewModel vm = shell.BrowserVm;
            BrowserCell cell = vm.Sections[0].Cells[0];
            vm.SelectCell(cell);
            Dispatcher.UIThread.RunJobs();

            AddBrowserWindow window = shell.Window.BrowserWindow!;
            ChooserCell cellButton = CellFor(window, cell);
            CanvasKeys.KeyDownOn(cellButton, Key.Enter);

            Assert.Single(shell.Graph.Nodes);
            Assert.True(window.IsVisible);

            CanvasKeys.KeyDownOn(window, Key.Escape);
            Assert.False(window.IsVisible);
            Assert.Same(window, shell.Window.BrowserWindow);
        }

        private static BrowserCell RecipeCell(AddBrowserViewModel vm, string recipeName) =>
            vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Kind == AddKind.Recipe && c.Candidate.Recipe!.Name == recipeName);

        [AvaloniaFact]
        public void Browser_UnsettableRecipe_PicksAtLowestTier() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge21();
            DataCache cache = shell.Window.DataCache!;
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "sulfur"));
            AddBrowserViewModel vm = shell.BrowserVm;
            var picks = new List<BrowserPick>();
            vm.PlaceRequested += (_, pick) => picks.Add(pick);
            vm.Quality = cache.Qualities["uncommon"];

            vm.SelectCell(RecipeCell(vm, "sulfur"));
            vm.PlaceSelectedRecipe();
            vm.SearchText = "iron gear wheel";
            vm.SelectCell(RecipeCell(vm, "iron-gear-wheel"));
            vm.PlaceSelectedRecipe();

            Assert.Equal(["normal", "uncommon"], picks.Select(p => p.Quality.Name));
            Assert.Equal(["normal", "uncommon"], shell.Graph.Nodes.OfType<RecipeNode>().OrderBy(n => n.NodeID).Select(n => n.BaseRecipe.Quality!.Name));
        }

        [AvaloniaFact]
        public void Browser_UnsettableRecipeSelected_CaptionNamesLock() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge21();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "sulfur"));
            AddBrowserViewModel vm = shell.BrowserVm;
            TextBlock caption = shell.Window.BrowserWindow!.FindControl<TextBlock>("SelectionCaption")!;

            vm.SelectCell(RecipeCell(vm, "sulfur"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Sulfur selected · Normal quality only", vm.SelectionCaption);
            Assert.Equal("Sulfur selected · Normal quality only", caption.Text);

            vm.SearchText = "iron gear wheel";
            vm.SelectCell(RecipeCell(vm, "iron-gear-wheel"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Iron gear wheel selected", vm.SelectionCaption);
            Assert.Equal("Iron gear wheel selected", caption.Text);
        }

        [AvaloniaFact]
        public void Browser_QualityPopUp_NeverNarrowedOrDisabled() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge21();
            DataCache cache = shell.Window.DataCache!;
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "sulfur"));
            AddBrowserViewModel vm = shell.BrowserVm;
            PopUp quality = shell.Window.BrowserWindow!.FindControl<PopUp>("QualityPopUp")!;
            int enabled = cache.AvailableQualities.Count(q => q.Enabled);
            string? before = quality.DisplayText;

            vm.SelectCell(RecipeCell(vm, "sulfur"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(quality.IsEnabled);
            Assert.True(quality.IsVisible);
            Assert.Equal(enabled, quality.Items.Count);
            Assert.Equal(before, quality.DisplayText);
        }
    }
}
