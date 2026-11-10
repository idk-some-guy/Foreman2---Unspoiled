using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class ButtonTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitButtonTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Button", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Button Themed(string text, params string[] classes) {
            var button = new Button { Theme = KitButtonTheme(), Content = text };
            foreach (string cls in classes)
                button.Classes.Add(cls);
            return button;
        }

        [AvaloniaFact]
        public void KitButton_IsAControlThemeTargetingButton() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Button), KitButtonTheme().TargetType);
        }

        [AvaloniaFact]
        public void Standard_NormalState_UsesControlBackgroundAndBorderStrong() {
            using var fixture = new KitFixture();
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), button.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), button.BorderBrush?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), button.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Standard_Hover_UsesControlBackgroundHover() {
            using var fixture = new KitFixture();
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            fixture.StateOf(button).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), button.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Standard_Pressed_UsesControlBackgroundPressed() {
            using var fixture = new KitFixture();
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            fixture.StateOf(button).Hover();
            fixture.StateOf(button).Press();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundPressed").ToImmutable(), button.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Primary_NormalState_UsesAccentFillAndTextOnAccent() {
            using var fixture = new KitFixture();
            Button button = Themed("Export…", "primary");
            fixture.Window.Content = button;

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), button.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), button.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Primary_Hover_UsesAccentHover() {
            using var fixture = new KitFixture();
            Button button = Themed("Export…", "primary");
            fixture.Window.Content = button;

            fixture.StateOf(button).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentHover").ToImmutable(), button.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Primary_Pressed_UsesAccentPressed() {
            using var fixture = new KitFixture();
            Button button = Themed("Export…", "primary");
            fixture.Window.Content = button;

            fixture.StateOf(button).Hover();
            fixture.StateOf(button).Press();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentPressed").ToImmutable(), button.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Destructive_NormalState_UsesErrorFillAndTextOnAccent() {
            using var fixture = new KitFixture();
            Button button = Themed("Discard", "destructive");
            fixture.Window.Content = button;

            Assert.Equal(fixture.Brush("Foreman.Color.Error").ToImmutable(), button.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), button.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Destructive_Hover_UsesErrorHover() {
            using var fixture = new KitFixture();
            Button button = Themed("Discard", "destructive");
            fixture.Window.Content = button;

            fixture.StateOf(button).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ErrorHover").ToImmutable(), button.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Destructive_Pressed_UsesErrorPressed() {
            using var fixture = new KitFixture();
            Button button = Themed("Discard", "destructive");
            fixture.Window.Content = button;

            fixture.StateOf(button).Hover();
            fixture.StateOf(button).Press();

            Assert.Equal(fixture.Brush("Foreman.Color.ErrorPressed").ToImmutable(), button.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            button.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), button.Opacity);
        }

        [AvaloniaFact]
        public void Standard_Label_UsesCalloutSizeAndCalloutEmphasizedWeight() {
            using var fixture = new KitFixture();
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.Callout.Emphasized.FontWeight", Application.Current.ActualThemeVariant, out object? weight);

            Assert.Equal(Assert.IsType<double>(size), button.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), button.FontWeight);
        }

        [AvaloniaFact]
        public void Primary_Label_UsesCalloutSizeAndEmphasizedBodyWeight() {
            using var fixture = new KitFixture();
            Button button = Themed("Export…", "primary");
            fixture.Window.Content = button;

            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontWeight", Application.Current.ActualThemeVariant, out object? weight);

            Assert.Equal(Assert.IsType<double>(size), button.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), button.FontWeight);
        }

        [AvaloniaFact]
        public void Destructive_Label_UsesCalloutSizeAndEmphasizedBodyWeight() {
            using var fixture = new KitFixture();
            Button button = Themed("Discard", "destructive");
            fixture.Window.Content = button;

            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontWeight", Application.Current.ActualThemeVariant, out object? weight);

            Assert.Equal(Assert.IsType<double>(size), button.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), button.FontWeight);
        }

        [AvaloniaFact]
        public void Plain_NormalState_TransparentFillAndBorder_SameWeightAsStandard() {
            using var fixture = new KitFixture();
            Button standard = Themed("Cancel");
            Button plain = Themed("Reset to defaults", "plain");
            var stack = new StackPanel { Children = { standard, plain } };
            fixture.Window.Content = stack;

            Assert.Equal(Brushes.Transparent.ToImmutable(), plain.Background?.ToImmutable());
            Assert.Equal(Brushes.Transparent.ToImmutable(), plain.BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(6, 0), plain.Padding);
            Assert.Equal(standard.FontWeight, plain.FontWeight);
            Assert.Equal(standard.BorderThickness, plain.BorderThickness);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsTheFocusRing() {
            using var fixture = new KitFixture();
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            fixture.StateOf(button).Focus();

            Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(button));
        }

        [AvaloniaFact]
        public void Transitions_AreAnOpacityOnlyFadeAtTheButtonStateDuration() {
            using var fixture = new KitFixture();
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            var opacity = Assert.Single(button.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.ButtonStateDuration, opacity.Duration);
            Assert.DoesNotContain(button.Transitions!, t => t is not DoubleTransition);
            Assert.IsType<CubicEaseOut>(opacity.Easing);
        }

        [AvaloniaFact]
        public void ReduceMotion_TransitionsStayOpacityOnlyWithCrossFadeEasing() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            Button button = Themed("Cancel");
            fixture.Window.Content = button;

            var opacity = Assert.Single(button.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.DoesNotContain(button.Transitions!, t => t is not DoubleTransition);
            Assert.IsType<CubicEaseInOut>(opacity.Easing);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class ButtonScaleTests {
        [AvaloniaFact]
        public void Height_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Button", null, out object? theme));
            var button = new Button { Theme = Assert.IsType<ControlTheme>(theme), Content = "Cancel" };
            fixture.Window.Content = button;

            Assert.Equal(22, button.Height);

            ScaleContract.Apply(1.5);

            Assert.Equal(33, button.Height);
        }
    }
}
