using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public class CheckBoxTests {
        private static ControlTheme KitCheckBoxTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.CheckBox", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static CheckBox Themed() => new() { Theme = KitCheckBoxTheme() };

        private static Border Box(CheckBox box) => box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Box");
        private static Path Check(CheckBox box) => box.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Check");
        private static Rectangle Dash(CheckBox box) => box.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_Dash");

        [AvaloniaFact]
        public void KitCheckBox_IsAControlThemeTargetingCheckBox() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(CheckBox), KitCheckBoxTheme().TargetType);
        }

        [AvaloniaFact]
        public void Normal_BoxUsesWellBackgroundAndBorderStrongAndHidesTheCheck() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Border part = Box(box);
            Assert.Equal(fixture.Brush("Foreman.Color.WellBackground").ToImmutable(), part.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), part.BorderBrush?.ToImmutable());
            Assert.False(Check(box).IsVisible);
            Assert.False(Dash(box).IsVisible);
        }

        [AvaloniaFact]
        public void Checked_BoxFillsAccentAndShowsTheCheckGlyph() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;

            box.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Border part = Box(box);
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), part.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), part.BorderBrush?.ToImmutable());
            Assert.True(Check(box).IsVisible);
            Assert.False(Dash(box).IsVisible);
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), Check(box).Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void Indeterminate_BoxFillsAccentAndShowsTheDashInsteadOfTheCheck() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            box.IsThreeState = true;
            fixture.Window.Content = box;

            box.IsChecked = null;
            Dispatcher.UIThread.RunJobs();

            Border part = Box(box);
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), part.Background?.ToImmutable());
            Assert.True(Dash(box).IsVisible);
            Assert.False(Check(box).IsVisible);
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), Dash(box).Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_BorderTurnsAccent() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;

            fixture.StateOf(box).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Box(box).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void CheckedPressed_BoxDarkensToAccentPressed() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;

            box.IsChecked = true;
            fixture.StateOf(box).Hover();
            fixture.StateOf(box).Press();

            Border part = Box(box);
            Assert.Equal(fixture.Brush("Foreman.Color.AccentPressed").ToImmutable(), part.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentPressed").ToImmutable(), part.BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;

            box.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), box.Opacity);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.CheckBox", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Radius.CheckBox", Application.Current.ActualThemeVariant, out object? radius);

            Border part = Box(box);
            Assert.Equal(Assert.IsType<double>(size), part.Width);
            Assert.Equal(Assert.IsType<double>(size), part.Height);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), part.CornerRadius);
        }

        [AvaloniaFact]
        public void SpaceKey_WhileFocused_TogglesChecked() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;

            fixture.StateOf(box).Focus();
            fixture.StateOf(box).KeyDown(Key.Space);
            fixture.StateOf(box).KeyUp(Key.Space);
            Dispatcher.UIThread.RunJobs();

            Assert.True(box.IsChecked);
        }

        [AvaloniaFact]
        public void ThreeState_SpaceCyclesUncheckedThenCheckedThenIndeterminate() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            box.IsThreeState = true;
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();
            Assert.False(box.IsChecked);

            fixture.StateOf(box).Focus();
            fixture.StateOf(box).KeyDown(Key.Space);
            fixture.StateOf(box).KeyUp(Key.Space);
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsChecked);

            fixture.StateOf(box).KeyDown(Key.Space);
            fixture.StateOf(box).KeyUp(Key.Space);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(box.IsChecked);
        }

        [AvaloniaFact]
        public void TemplateStackPanel_SpacingComesFromTheSelectionLabelGapToken() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            StackPanel panel = box.GetVisualDescendants().OfType<StackPanel>().Single();
            Application.Current!.TryGetResource("Foreman.Size.SelectionLabelGap", Application.Current.ActualThemeVariant, out object? gap);
            Assert.Equal(Assert.IsType<double>(gap), panel.Spacing);
        }

        [AvaloniaFact]
        public void NoContent_BoundsWidthEqualsTheCheckBoxToken() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.CheckBox", Application.Current.ActualThemeVariant, out object? size);
            Assert.Equal(Assert.IsType<double>(size), box.Bounds.Width);
        }

        [AvaloniaFact]
        public void ClickTheGapBetweenBoxAndLabel_TogglesChecked() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            box.Content = "Compatible recipes only";
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Border part = Box(box);
            Point rightEdge = Avalonia.VisualExtensions.TranslatePoint(part, new Point(part.Bounds.Width, part.Bounds.Height / 2), fixture.Window)!.Value;
            Point gapPoint = rightEdge + new Vector(2, 0);

            fixture.Window.MouseDown(gapPoint, MouseButton.Left, RawInputModifiers.None);
            fixture.Window.MouseUp(gapPoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(box.IsChecked);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsTheFocusRingWithTheCheckBoxCornerRadius() {
            using var fixture = new KitFixture();
            CheckBox box = Themed();
            fixture.Window.Content = box;

            fixture.StateOf(box).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(box));

            Application.Current!.TryGetResource("Foreman.Radius.CheckBox", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class CheckBoxScaleTests {
        [AvaloniaFact]
        public void BoxSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.CheckBox", null, out object? theme));
            var box = new CheckBox { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Border part = box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Box");
            Assert.Equal(15, part.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22.5, part.Width);
        }
    }
}
