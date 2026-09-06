using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using System.Linq;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Inspector {
    //Task 5's reusable icon-cell grid: an ItemsControl over plain Button cells, so pointer clicks and
    //keyboard Enter/Space both come from Button's own Click plumbing - AddHandler(Button.ClickEvent, ...)
    //on the PickGrid root catches every cell.
    public class PickGridTests {
        private static (AvaloniaWindow Window, PickGrid Grid) NewGrid(params PickGridItem[] items) {
            var grid = new PickGrid { Items = items };
            var window = new AvaloniaWindow { Content = grid, Width = 400, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, grid);
        }

        private static void Click(AvaloniaWindow window, Button button) {
            Avalonia.Point center = new(button.Bounds.Width / 2, button.Bounds.Height / 2);
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(button, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        //Checks the margin directly - WrapPanel's arranged Bounds aren't reliable in the headless runner.
        [AvaloniaFact]
        public void Cell_Is26x26_WithRadius5_AndA5PixelMarginGap() {
            PickGridItem[] items = [new PickGridItem("a", "Alpha", "", null, false, true)];
            (_, PickGrid grid) = NewGrid(items);

            Button button = grid.GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal(26, button.Bounds.Width);
            Assert.Equal(26, button.Bounds.Height);
            Assert.Equal(new Avalonia.CornerRadius(5), button.CornerRadius);
            Assert.Equal(new Thickness(2.5), button.Margin);
        }

        [AvaloniaFact]
        public void RendersOneCellPerItem() {
            PickGridItem[] items = [
                new PickGridItem("a", "Alpha", "", null, false, true),
                new PickGridItem("b", "Beta", "", null, false, true),
                new PickGridItem("c", "Gamma", "", null, false, true),
            ];
            (_, PickGrid grid) = NewGrid(items);

            Assert.Equal(3, grid.GetVisualDescendants().OfType<Button>().Count());
        }

        [AvaloniaFact]
        public void SelectedItem_CellCarriesSelectedClass() {
            PickGridItem[] items = [
                new PickGridItem("a", "Alpha", "", null, true, true),
                new PickGridItem("b", "Beta", "", null, false, true),
            ];
            (_, PickGrid grid) = NewGrid(items);

            var buttons = grid.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Contains("selected", buttons[0].Classes);
            Assert.DoesNotContain("selected", buttons[1].Classes);
        }

        [AvaloniaFact]
        public void UnavailableItem_CellCarriesUnavailableClass() {
            PickGridItem[] items = [
                new PickGridItem("a", "Alpha", "", null, false, false),
                new PickGridItem("b", "Beta", "", null, false, true),
            ];
            (_, PickGrid grid) = NewGrid(items);

            var buttons = grid.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Contains("unavailable", buttons[0].Classes);
            Assert.DoesNotContain("unavailable", buttons[1].Classes);
        }

        [AvaloniaFact]
        public void Click_RaisesItemActivated_WithTheClickedItem() {
            PickGridItem[] items = [
                new PickGridItem("a", "Alpha", "", null, false, true),
                new PickGridItem("b", "Beta", "", null, false, true),
            ];
            (AvaloniaWindow window, PickGrid grid) = NewGrid(items);
            PickGridItem? activated = null;
            grid.ItemActivated += (_, item) => activated = item;

            Button second = grid.GetVisualDescendants().OfType<Button>().ElementAt(1);
            Click(window, second);

            Assert.Equal(items[1], activated);
        }

        [AvaloniaFact]
        public void EnterOnFocusedCell_RaisesItemActivated() {
            PickGridItem[] items = [new PickGridItem("a", "Alpha", "", null, false, true)];
            (_, PickGrid grid) = NewGrid(items);
            PickGridItem? activated = null;
            grid.ItemActivated += (_, item) => activated = item;

            Button button = grid.GetVisualDescendants().OfType<Button>().Single();
            button.Focus();
            Dispatcher.UIThread.RunJobs();
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(items[0], activated);
        }

        [AvaloniaFact]
        public void SpaceOnFocusedCell_RaisesItemActivated() {
            PickGridItem[] items = [new PickGridItem("a", "Alpha", "", null, false, true)];
            (_, PickGrid grid) = NewGrid(items);
            PickGridItem? activated = null;
            grid.ItemActivated += (_, item) => activated = item;

            Button button = grid.GetVisualDescendants().OfType<Button>().Single();
            button.Focus();
            Dispatcher.UIThread.RunJobs();
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(items[0], activated);
        }
    }
}
