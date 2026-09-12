using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Foreman.Mac.Kit.Appearance;
using System;
using Xunit;

namespace Foreman.Mac.Kit.Tests.support {
    public class KitFixtureTests : IDisposable {
        private readonly KitFixture fixture = new();

        public void Dispose() {
            fixture.Dispose();
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public void Window_IsShown() {
            Assert.True(fixture.Window.IsVisible);
        }

        [AvaloniaFact]
        public void Appearance_DefaultsToNoPreference() {
            Assert.Equal(ColorContrastPreference.NoPreference, fixture.Appearance.ContrastPreference);
        }

        [AvaloniaFact]
        public void Brush_ResolvesColorResource_ToMatchingSolidColorBrush() {
            Application.Current!.TryGetResource("Foreman.Color.Text", Application.Current.ActualThemeVariant, out object? value);
            Color expected = Assert.IsType<Color>(value);

            IBrush brush = fixture.Brush("Foreman.Color.Text");

            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
        }

        [AvaloniaFact]
        public void StateOf_Focus_FocusesTheControl() {
            var button = new Button { Content = "Go", Focusable = true };
            fixture.Window.Content = button;

            fixture.StateOf(button).Focus();

            Assert.True(button.IsFocused);
        }

        [AvaloniaFact]
        public void StateOf_Click_RaisesClickThroughRealPointerEvents() {
            var button = new Button { Content = "Go", Width = 40, Height = 20 };
            fixture.Window.Content = button;
            int clicks = 0;
            button.Click += (_, _) => clicks++;

            fixture.StateOf(button).Click();

            Assert.Equal(1, clicks);
        }

        [AvaloniaFact]
        public void StateOf_KeyDownKeyUp_RaisesRoutedKeyEventsOnTheControl() {
            var button = new Button { Content = "Go" };
            fixture.Window.Content = button;
            int downs = 0, ups = 0;
            button.KeyDown += (_, e) => { if (e.Key == Key.Space) downs++; };
            button.KeyUp += (_, e) => { if (e.Key == Key.Space) ups++; };

            fixture.StateOf(button).KeyDown(Key.Space);
            fixture.StateOf(button).KeyUp(Key.Space);

            Assert.Equal(1, downs);
            Assert.Equal(1, ups);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class KitFixtureScaleTests {
        [AvaloniaFact]
        public void Dispose_ResetsScaleToOne() {
            var fixture = new KitFixture();
            ScaleContract.Apply(1.5);

            fixture.Dispose();

            Application.Current!.TryGetResource("Foreman.Type.Body.FontSize", Application.Current.ActualThemeVariant, out object? value);
            Assert.Equal(13.0, Assert.IsType<double>(value));
        }
    }
}
