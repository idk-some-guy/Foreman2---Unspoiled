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
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using System.Windows.Input;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class PopUpTests {
        private static ControlTheme KitPopUpTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PopUp", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PopUp Themed(params string[] items) {
            var popup = new PopUp { Theme = KitPopUpTheme() };
            foreach (string item in items)
                popup.Items.Add(item);
            if (items.Length > 0)
                popup.SelectedItem = items[0];
            return popup;
        }

        private static Border Frame(PopUp popup) => popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
        private static Border Indicator(PopUp popup) => popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
        private static Path Chevron(PopUp popup) => popup.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_PulldownChevron");
        private static TextBlock Label(PopUp popup) => popup.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");

        private sealed class TestCommand : ICommand {
            public object? Parameter;
            public bool Invoked;
            public event System.EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { Invoked = true; Parameter = parameter; }
        }

        [AvaloniaFact]
        public void KitPopUp_IsAControlThemeTargetingPopUp() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(PopUp), KitPopUpTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Assert.Equal(Assert.IsType<double>(height), popup.Height);

            Border indicator = Indicator(popup);
            Application.Current!.TryGetResource("Foreman.Size.PopUpIndicator", Application.Current.ActualThemeVariant, out object? indicatorSize);
            Application.Current!.TryGetResource("Foreman.Radius.PopUpIndicator", Application.Current.ActualThemeVariant, out object? indicatorRadius);
            Assert.Equal(Assert.IsType<double>(indicatorSize), indicator.Width);
            Assert.Equal(Assert.IsType<CornerRadius>(indicatorRadius), indicator.CornerRadius);
        }

        [AvaloniaFact]
        public void Indicator_FillIsAccentAndGlyphFillIsTextOnAccent() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Border indicator = Indicator(popup);
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), indicator.Background?.ToImmutable());

            Path glyph = indicator.GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), glyph.Fill?.ToImmutable());
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.PopUpChevrons.16"), glyph.Data);
        }

        [AvaloniaFact]
        public void PopUpChevrons_IsNotTheSharedChevronGeometry() {
            using var fixture = new KitFixture();
            var popUpChevrons = (Geometry)Application.Current!.FindResource("Kit.Glyph.PopUpChevrons.16")!;
            var chevron10 = (Geometry)Application.Current!.FindResource("Kit.Glyph.Chevron.10")!;
            Assert.NotEqual(popUpChevrons.Bounds, chevron10.Bounds);
        }

        [AvaloniaFact]
        public void Pulldown_HasNoIndicatorAndChevronIsTintedTextTertiary() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Add as");
            popup.IsPullDown = true;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Assert.False(Indicator(popup).IsVisible);
            Path chevron = Chevron(popup);
            Assert.True(chevron.IsVisible);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), chevron.Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_FrameBackgroundIsControlBackgroundHover() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(popup).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), Frame(popup).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Open_FrameBackgroundIsControlBackgroundPressed() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundPressed").ToImmutable(), Frame(popup).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;

            popup.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), popup.Opacity);
        }

        [AvaloniaFact]
        public void ClosedArrowKeys_StepTheAdjacentValueWithoutOpening() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s", "Power / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Down);

            Assert.Equal("Belts / s", popup.SelectedItem);
            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void Space_OpensTheMenuWithTheCurrentValueHighlighted() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            popup.SelectedItem = "Belts / s";
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);

            Assert.True(popup.IsOpen);
            Assert.Equal(1, popup.Menu.HighlightedIndex);
        }

        [AvaloniaFact]
        public void ClosedReturn_OpensTheMenu() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Enter);

            Assert.True(popup.IsOpen);
        }

        [AvaloniaFact]
        public void OpenEnter_CommitsTheHighlightedValueAndCloses() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);
            fixture.StateOf(popup).KeyDown(Key.Down);
            fixture.StateOf(popup).KeyDown(Key.Enter);

            Assert.Equal("Belts / s", popup.SelectedItem);
            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void Escape_ClosesWithoutChangingTheSelectedItem() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);
            fixture.StateOf(popup).KeyDown(Key.Down);
            fixture.StateOf(popup).KeyDown(Key.Escape);

            Assert.Equal("Items / s", popup.SelectedItem);
            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void PullDown_ShowsItsLabelAndRaisesTheCommand() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Add as blueprint", "Add as ghost");
            popup.IsPullDown = true;
            popup.Label = "Add as";
            var command = new TestCommand();
            popup.Command = command;
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Add as", Label(popup).Text);

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            popup.Menu.HighlightedIndex = 0;
            popup.Menu.ActivateHighlighted();

            Assert.True(command.Invoked);
            Assert.Equal("Add as blueprint", command.Parameter);
        }

        [AvaloniaFact]
        public void Detaching_ClosesTheFlyout() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            var host = new ContentControl { Content = popup };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);

            host.Content = null;
            Dispatcher.UIThread.RunJobs();

            Assert.False(popup.IsOpen);
        }

        [AvaloniaFact]
        public void Opening_RendersEveryRowAndSettlesWithoutRebuilding() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s", "Power / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(300);

            StackPanel items = popup.Menu.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Items");
            Assert.Equal(3, items.Children.Count);
            Assert.True(popup.Menu.Bounds.Width > 0 && popup.Menu.Bounds.Height > 0);

            int rebuilds = 0;
            int layouts = 0;
            items.Children.CollectionChanged += (_, _) => rebuilds++;
            popup.Menu.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, rebuilds);
            Assert.True(layouts <= 2, $"MenuPanel laid out {layouts} times while idle.");
        }

        [AvaloniaFact]
        public void LightDismiss_ClosesTheControlAndTheMenuReopens() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Popup host = popup.GetVisualDescendants().OfType<Popup>().Single();
            Assert.True(host.IsOpen);

            host.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.DoesNotContain(":open", popup.Classes);

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsOpen);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingAtTheControlRadius() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s");
            fixture.Window.Content = popup;

            fixture.StateOf(popup).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(popup));
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }

        [AvaloniaFact]
        public void EscapeAfterOpen_ReturnsFocusToTheControlWithARing() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            fixture.StateOf(popup).Focus();

            fixture.StateOf(popup).KeyDown(Key.Space);
            fixture.StateOf(popup).KeyDown(Key.Escape);

            Assert.True(popup.IsKeyboardFocusWithin);
            Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(popup));
        }

        [AvaloniaFact]
        public void LightDismiss_ReturnsFocusToTheControl() {
            using var fixture = new KitFixture();
            PopUp popup = Themed("Items / s", "Belts / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Popup host = popup.GetVisualDescendants().OfType<Popup>().Single();

            host.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(popup.IsKeyboardFocusWithin);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class PopUpScaleTests {
        [AvaloniaFact]
        public void IndicatorSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.PopUp", null, out object? theme));
            var popup = new PopUp { Theme = Assert.IsType<ControlTheme>(theme) };
            popup.Items.Add("Items / s");
            fixture.Window.Content = popup;
            Dispatcher.UIThread.RunJobs();

            Border indicator = popup.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
            Assert.Equal(16, indicator.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(24, indicator.Width);
        }
    }
}
