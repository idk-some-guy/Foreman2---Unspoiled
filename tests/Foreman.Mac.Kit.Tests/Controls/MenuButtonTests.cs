using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class MenuButtonTests {
        private static ControlTheme KitMenuButtonTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.MenuButton", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static MenuButton Themed(string label = "Filter") {
            var button = new MenuButton { Theme = KitMenuButtonTheme(), Label = label };
            button.Items.Add(new MenuButtonItem { Text = "Show unavailable objects", IsDefault = false });
            button.Items.Add(new MenuButtonItem { Text = "Show hidden objects", IsDefault = false });
            button.Items.Add(new MenuButtonItem { Text = "Show only compatible recipes", IsDefault = true, IsChecked = true });
            return button;
        }

        private static Border Frame(MenuButton button) => button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        [AvaloniaFact]
        public void KitMenuButton_IsAControlThemeTargetingMenuButton() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(MenuButton), KitMenuButtonTheme().TargetType);
        }

        [AvaloniaFact]
        public void Default_FrameIsControlBackgroundWithBorderStrong() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Assert.False(button.IsFilled);
            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), Frame(button).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Frame(button).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void NonDefaultValue_IsFilledAndBrushesAreAccentSoftAccentAccentText() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            button.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            button.Menu.Items[0].Activate!.Invoke();
            button.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(button.IsFilled);
            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), Frame(button).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Frame(button).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void FilledLabel_HasAParenthesizedNonDefaultCount() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            button.Menu.Items.ElementAt(0).Activate!.Invoke();

            Assert.Equal("Filter (1)", button.DisplayLabel);
        }

        [AvaloniaFact]
        public void MenuHasCheckedItemsASeparatorThenReset() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            var items = button.Menu.Items.ToList();
            Assert.Equal(5, items.Count);
            Assert.False(items[0].IsSeparator);
            Assert.False(items[1].IsSeparator);
            Assert.False(items[2].IsSeparator);
            Assert.True(items[3].IsSeparator);
            Assert.Equal("Reset to defaults", items[4].Text);
            Assert.False(items[4].HasCheckSlot);
        }

        [AvaloniaFact]
        public void Reset_DisabledWhenNothingIsNonDefault() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Assert.False(button.Menu.Items.Last().IsEnabled);
        }

        [AvaloniaFact]
        public void CheckingAnItem_StaysOpenAndEnablesReset() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            button.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            button.Menu.Items[0].Activate!.Invoke();

            Assert.True(button.IsOpen);
            Assert.True(button.Menu.Items.Last().IsEnabled);
        }

        [AvaloniaFact]
        public void Reset_ClosesAndRestoresEveryItemToItsDefault() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            button.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            button.Menu.HighlightedIndex = 0;
            button.Menu.ActivateHighlighted();
            button.Menu.HighlightedIndex = button.Menu.Items.Count - 1;
            button.Menu.ActivateHighlighted();

            Assert.False(button.IsOpen);
            Assert.False(button.IsFilled);
            foreach (MenuButtonItem item in button.Items)
                Assert.Equal(item.IsDefault, item.IsChecked);
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;

            button.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), button.Opacity);
        }

        [AvaloniaFact]
        public void Padding_ComesFromTheMenuButtonPaddingToken() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, Frame(button).Padding.Top);
            Assert.Equal(8, Frame(button).Padding.Left);
        }

        [AvaloniaFact]
        public void Overflow_HasNoFillOrBorderUntilHover() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            button.Classes.Add("overflow");
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Brushes.Transparent.ToImmutable(), Frame(button).Background?.ToImmutable());
            Assert.Equal(new Thickness(0), Frame(button).BorderThickness);

            fixture.StateOf(button).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), Frame(button).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Overflow_Is28By40WithTheOverflowGlyphVisible() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            button.Classes.Add("overflow");
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(28, button.Width);
            Assert.Equal(40, button.Height);

            var overflowGlyph = button.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_OverflowGlyph");
            Assert.True(overflowGlyph.IsVisible);
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.Overflow.16"), overflowGlyph.Data);
        }

        [AvaloniaFact]
        public void OpenArrowKeys_SkipTheSeparatorAndTheDisabledReset() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;
            fixture.StateOf(button).Focus();

            //Nothing is non-default, so Reset starts disabled; three Downs from item 0 must skip both it and the separator, wrapping to item 0.
            fixture.StateOf(button).KeyDown(Key.Space);
            for (int i = 0; i < 3; i++)
                fixture.StateOf(button).KeyDown(Key.Down);

            Assert.Equal(0, button.Menu.HighlightedIndex);
        }

        [AvaloniaFact]
        public void Detaching_ClosesTheFlyout() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            var host = new ContentControl { Content = button };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            button.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsOpen);

            host.Content = null;
            Dispatcher.UIThread.RunJobs();

            Assert.False(button.IsOpen);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingAtTheControlRadius() {
            using var fixture = new KitFixture();
            MenuButton button = Themed();
            fixture.Window.Content = button;

            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(button));
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }
    }
}
