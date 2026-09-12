using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public class RadioGroupTests {
        private static ControlTheme KitRadioButtonTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.RadioButton", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitRadioGroupTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.RadioGroup", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static RadioButton Themed(string label) => new() { Theme = KitRadioButtonTheme(), Content = label };

        private static RadioButton Bare() => new() { Theme = KitRadioButtonTheme() };

        private static Border Box(RadioButton radio) => radio.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Box");
        private static Ellipse Dot(RadioButton radio) => radio.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Name == "PART_Dot");

        [AvaloniaFact]
        public void KitRadioButton_IsAControlThemeTargetingRadioButton() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(RadioButton), KitRadioButtonTheme().TargetType);
        }

        [AvaloniaFact]
        public void Normal_BorderStrongAndDotHidden() {
            using var fixture = new KitFixture();
            RadioButton radio = Themed("Up");
            fixture.Window.Content = radio;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Box(radio).BorderBrush?.ToImmutable());
            Assert.False(Dot(radio).IsVisible);
        }

        [AvaloniaFact]
        public void Checked_BorderTurnsAccentAndShowsTheDot() {
            using var fixture = new KitFixture();
            RadioButton radio = Themed("Up");
            fixture.Window.Content = radio;

            radio.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Box(radio).BorderBrush?.ToImmutable());
            Assert.True(Dot(radio).IsVisible);
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Dot(radio).Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_BorderTurnsAccent() {
            using var fixture = new KitFixture();
            RadioButton radio = Bare();
            fixture.Window.Content = radio;

            fixture.StateOf(radio).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Box(radio).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void CheckedPressed_BorderAndDotDarkenToAccentPressed() {
            using var fixture = new KitFixture();
            RadioButton radio = Bare();
            fixture.Window.Content = radio;

            radio.IsChecked = true;
            fixture.StateOf(radio).Hover();
            fixture.StateOf(radio).Press();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentPressed").ToImmutable(), Box(radio).BorderBrush?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentPressed").ToImmutable(), Dot(radio).Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            RadioButton radio = Themed("Up");
            fixture.Window.Content = radio;

            radio.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), radio.Opacity);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsTheFocusRingWithTheRadioCornerRadius() {
            using var fixture = new KitFixture();
            RadioButton radio = Themed("Up");
            fixture.Window.Content = radio;

            fixture.StateOf(radio).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(radio));

            Application.Current!.TryGetResource("Foreman.Radius.Pill", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }

        private static RadioGroup ThemedGroup(params RadioButton[] radios) {
            var group = new RadioGroup { Theme = KitRadioGroupTheme() };
            foreach (RadioButton radio in radios)
                group.Items.Add(radio);
            return group;
        }

        [AvaloniaFact]
        public void KitRadioGroup_IsAControlThemeTargetingRadioGroup() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(RadioGroup), KitRadioGroupTheme().TargetType);
        }

        [AvaloniaFact]
        public void SelectingOneRadio_UnselectsTheOthersInTheGroup() {
            using var fixture = new KitFixture();
            RadioButton up = Themed("Up"), down = Themed("Down"), auto = Themed("Auto");
            RadioGroup group = ThemedGroup(up, down, auto);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            up.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(up.IsChecked);

            down.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(up.IsChecked);
            Assert.True(down.IsChecked);
            Assert.False(auto.IsChecked);
        }

        [AvaloniaFact]
        public void ArrowDown_MovesFocusAndSelectionToTheNextRadio() {
            using var fixture = new KitFixture();
            RadioButton up = Themed("Up"), down = Themed("Down"), auto = Themed("Auto");
            RadioGroup group = ThemedGroup(up, down, auto);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(up).Focus();
            fixture.StateOf(up).KeyDown(Key.Down);

            Assert.True(down.IsFocused);
            Assert.True(down.IsChecked);
        }

        [AvaloniaFact]
        public void ArrowUp_FromTheFirstRadio_WrapsToTheLastRadio() {
            using var fixture = new KitFixture();
            RadioButton up = Themed("Up"), down = Themed("Down"), auto = Themed("Auto");
            RadioGroup group = ThemedGroup(up, down, auto);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(up).Focus();
            fixture.StateOf(up).KeyDown(Key.Up);

            Assert.True(auto.IsFocused);
            Assert.True(auto.IsChecked);
        }

        [AvaloniaFact]
        public void ArrowDown_WithFocusOnTheGroupContainer_SelectsTheFirstRadio() {
            using var fixture = new KitFixture();
            RadioButton up = Themed("Up"), down = Themed("Down"), auto = Themed("Auto");
            RadioGroup group = ThemedGroup(up, down, auto);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(group).KeyDown(Key.Down);

            Assert.True(up.IsFocused);
            Assert.True(up.IsChecked);
        }

        [AvaloniaFact]
        public void RowGap_ComesFromTheRadioGroupRowGapToken() {
            using var fixture = new KitFixture();
            RadioButton up = Themed("Up"), down = Themed("Down");
            RadioGroup group = ThemedGroup(up, down);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            StackPanel panel = group.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Orientation == Orientation.Vertical);
            Application.Current!.TryGetResource("Foreman.Size.RadioGroupRowGap", Application.Current.ActualThemeVariant, out object? gap);
            Assert.Equal(Assert.IsType<double>(gap), panel.Spacing);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class RadioButtonScaleTests {
        [AvaloniaFact]
        public void BoxSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.RadioButton", null, out object? theme));
            var radio = new RadioButton { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = radio;
            Dispatcher.UIThread.RunJobs();

            Border part = radio.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Box");
            Assert.Equal(15, part.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22.5, part.Width);
        }
    }
}
