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
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class StepperTests {
        private static ControlTheme KitStepperTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Stepper", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Stepper Themed() => new() { Theme = KitStepperTheme() };

        private static TextBox ValueBox(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_ValueBox");

        private static Button UpButton(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Up");

        private static Button DownButton(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Down");

        private static Border Frame(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static TextBlock UnitText(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Unit");

        [AvaloniaFact]
        public void KitStepper_IsAControlThemeTargetingStepper() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Stepper), KitStepperTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);

            Assert.Equal(Assert.IsType<double>(height), stepper.Height);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Frame(stepper).CornerRadius);
            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), Frame(stepper).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Frame(stepper).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void InnerValueBox_UsesTheKitFieldTemplateNotFluents() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.True(Application.Current!.TryGetResource("Kit.StepperValueField", null, out object? theme));
            Assert.Same(theme, ValueBox(stepper).Theme);

            Border inner = ValueBox(stepper).GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.Equal(new Thickness(0), inner.BorderThickness);
        }

        [AvaloniaFact]
        public void InnerValueBox_ResolvesCalloutFontSizeTransparentBackgroundAndNoFocusRing() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? fontSize);
            Assert.Equal(Assert.IsType<double>(fontSize), box.FontSize);
            Assert.Equal(Brushes.Transparent.Color, Assert.IsAssignableFrom<ISolidColorBrush>(box.Background).Color);

            fixture.StateOf(box).Focus();
            Assert.Null(AdornerLayer.GetAdorner(box));
        }

        [AvaloniaFact]
        public void InitialValue_IsFormattedIntoTheField() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 4;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("4", ValueBox(stepper).Text);
        }

        [AvaloniaFact]
        public void UpButtonClick_IncrementsByIncrement() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 4;
            stepper.Increment = 2;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(UpButton(stepper)).Click();

            Assert.Equal(6, stepper.Value);
            Assert.Equal("6", ValueBox(stepper).Text);
        }

        [AvaloniaFact]
        public void DownButtonClick_DecrementsByIncrement() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 4;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(DownButton(stepper)).Click();

            Assert.Equal(3, stepper.Value);
        }

        [AvaloniaFact]
        public void KeyboardUpDown_StepsByIncrementAndShiftStepsByTen() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Value = 10;
            stepper.Increment = 1;
            stepper.Maximum = 1000;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(ValueBox(stepper)).Focus();
            fixture.StateOf(ValueBox(stepper)).KeyDown(Key.Up);
            Assert.Equal(11, stepper.Value);

            fixture.StateOf(ValueBox(stepper)).KeyDown(Key.Down, KeyModifiers.Shift);
            Assert.Equal(1, stepper.Value);
        }

        [AvaloniaFact]
        public void Value_ClampsToMinimumAndMaximum() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Minimum = 0;
            stepper.Maximum = 5;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            stepper.Value = 100;
            Assert.Equal(5, stepper.Value);

            stepper.Value = -50;
            Assert.Equal(0, stepper.Value);
        }

        [AvaloniaFact]
        public void SetValueDirectly_StillClampsViaCoercion() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Minimum = 0;
            stepper.Maximum = 5;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            stepper.SetValue(Stepper.ValueProperty, 500);

            Assert.Equal(5, stepper.Value);
        }

        [AvaloniaFact]
        public void TypedValue_CommitsOnReturn() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Maximum = 1000;
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            box.Text = "42";
            fixture.StateOf(box).Focus();
            fixture.StateOf(box).KeyDown(Key.Enter);

            Assert.Equal(42, stepper.Value);
        }

        [AvaloniaFact]
        public void TypedValue_CommitsOnBlurAndClampsToMaximum() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            stepper.Maximum = 10;
            var host = new StackPanel();
            var other = new TextBox();
            host.Children.Add(stepper);
            host.Children.Add(other);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            TextBox box = ValueBox(stepper);
            box.Text = "999";
            fixture.StateOf(box).Focus();
            fixture.StateOf(other).Focus();

            Assert.Equal(10, stepper.Value);
        }

        [AvaloniaFact]
        public void Unit_ShowsAfterFieldOnlyWhenSet() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.False(UnitText(stepper).IsVisible);

            stepper.Unit = "items/s";
            Dispatcher.UIThread.RunJobs();

            Assert.True(UnitText(stepper).IsVisible);
            Assert.Equal("items/s", UnitText(stepper).Text);
        }

        [AvaloniaFact]
        public void ArrowCellHover_TurnsTheCellAndGlyphToEmphasis() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Button up = UpButton(stepper);
            Path glyph = up.GetVisualDescendants().OfType<Path>().Single();

            fixture.StateOf(up).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), glyph.Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            stepper.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), stepper.Opacity);
            Assert.Equal(1, ValueBox(stepper).Opacity);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingAroundTheWholeControl() {
            using var fixture = new KitFixture();
            Stepper stepper = Themed();
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(ValueBox(stepper)).Focus();

            Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(stepper));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class StepperScaleTests {
        private static Grid Columns(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Columns");

        [AvaloniaFact]
        public void Height_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Stepper", null, out object? theme));
            var stepper = new Stepper { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22, stepper.Height);
            Assert.Equal(40, Columns(stepper).ColumnDefinitions[0].Width.Value);
            Assert.Equal(15, Columns(stepper).ColumnDefinitions[2].Width.Value);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33, stepper.Height);
            Assert.Equal(60, Columns(stepper).ColumnDefinitions[0].Width.Value);
            Assert.Equal(22.5, Columns(stepper).ColumnDefinitions[2].Width.Value);
        }

        [AvaloniaFact]
        public void ChevronViewbox_TracksScaleContractWhileThePathStaysAtItsMasterSize() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Stepper", null, out object? theme));
            var stepper = new Stepper { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = stepper;
            Dispatcher.UIThread.RunJobs();

            Button up = stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Up");
            Path chevron = up.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Glyph");
            Viewbox box = up.GetVisualDescendants().OfType<Viewbox>().Single();
            Assert.Equal(10, box.Bounds.Width);
            Assert.Equal(10, chevron.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(15, box.Bounds.Width);
            Assert.Equal(10, chevron.Bounds.Width);
        }
    }
}
