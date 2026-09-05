using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Mac.UiTests.support;
using System.Linq;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    public sealed class QuickAddPaletteTests {
        private static (QuickAddPalette palette, QuickAddViewModel vm, AvaloniaWindow window) Open(string seed = "") {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Iron Plate", "Iron Plating"], recipes: ["Iron Plate"]);
            var vm = new QuickAddViewModel(new AddCatalog(fx.Cache, new AppSettings()),
                new AddRequest(new DrawingPoint(0, 0), AddScope.All, SeedText: seed), null, _ => null);
            var palette = new QuickAddPalette(vm);
            var window = new AvaloniaWindow { Content = palette, Width = 600, Height = 400 };
            window.Show();
            palette.FocusInitialControl();
            return (palette, vm, window);
        }

        [AvaloniaFact]
        public void TypingFiltersAndHighlightsTheTopRow() {
            (QuickAddPalette palette, QuickAddViewModel vm, _) = Open();
            vm.Query = "iron pl";
            Assert.Equal(["Iron Plate|item", "Iron Plate|recipe", "Iron Plating|item"], vm.Rows.Select(r => $"{r.Name}|{r.KindLabel}"));
            Assert.Equal(0, vm.HighlightedIndex);
            Assert.Equal(3, palette.FindControl<ItemsControl>("Results")!.ItemCount);
        }

        [AvaloniaFact]
        public void ArrowKeysMoveTheHighlight_AndWrap() {
            (QuickAddPalette palette, QuickAddViewModel vm, _) = Open("iron");
            var box = palette.FindControl<TextBox>("SearchBox")!;
            CanvasKeys.KeyDownOn(box, Key.Down); Assert.Equal(1, vm.HighlightedIndex);
            CanvasKeys.KeyDownOn(box, Key.Down); CanvasKeys.KeyDownOn(box, Key.Down); Assert.Equal(0, vm.HighlightedIndex);
            CanvasKeys.KeyDownOn(box, Key.Up); Assert.Equal(2, vm.HighlightedIndex);
        }

        [AvaloniaFact]
        public void EnterCommitsTheHighlightedRow_KeepsQueryAndStaysOpen() {
            (QuickAddPalette palette, QuickAddViewModel vm, _) = Open("iron");
            AddCandidate? picked = null; bool closed = false;
            palette.PickCommitted += (_, c) => picked = c;
            palette.CloseRequested += (_, _) => closed = true;
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);
            Assert.Equal("Iron Plate", picked!.Name);
            Assert.Equal("iron", vm.Query);
            Assert.False(closed);
        }

        [AvaloniaFact]
        public void EnterWithNoRowsDoesNothing() {
            (QuickAddPalette palette, _, _) = Open("zzz");
            int picks = 0; palette.PickCommitted += (_, _) => picks++;
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);
            Assert.Equal(0, picks);
        }

        [AvaloniaFact]
        public void CmdEnterRequestsBrowseWithTheQuery_AndEscapeCloses() {
            (QuickAddPalette palette, _, _) = Open("iron");
            string? browse = null; bool closed = false;
            palette.BrowseRequested += (_, q) => browse = q;
            palette.CloseRequested += (_, _) => closed = true;
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter, KeyModifiers.Meta);
            Assert.Equal("iron", browse);
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Escape);
            Assert.True(closed);
        }

        [AvaloniaFact]
        public void ClickingARowCommitsIt() {
            (QuickAddPalette palette, _, _) = Open("iron");
            AddCandidate? picked = null; palette.PickCommitted += (_, c) => picked = c;
            var second = palette.FindControl<ItemsControl>("Results")!.ContainerFromIndex(1)!;
            RealClick.On(second);
            Assert.Equal("recipe", picked!.KindLabel);
        }

        [AvaloniaFact]
        public void PaletteIsMockSized_AndShowsTheThreeHints() {
            (QuickAddPalette palette, _, _) = Open("iron");
            Assert.Equal(272, palette.Bounds.Width, 0.5);
            var hints = palette.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Place, keep open", hints); Assert.Contains("Browse…", hints); Assert.Contains("Close", hints);

            Border iconWell = palette.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("foreman-qa-row-icon"));
            Assert.Equal(22, iconWell.Bounds.Width, 0.5);
            Assert.Equal(22, iconWell.Bounds.Height, 0.5);
            Assert.Equal(new Thickness(1), iconWell.BorderThickness);
        }
    }
}
