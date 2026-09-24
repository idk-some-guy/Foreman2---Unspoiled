using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Views;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    public class AboutWindowTests {
        private static readonly string[] RetiredAboutKeys = ["Name", "Tagline", "Version"];

        private static object Token(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value), key);
            return value!;
        }

        private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

        private static Color TokenColor(string key) => Token(key) switch {
            Color c => c,
            ISolidColorBrush b => b.Color,
            object other => throw new Xunit.Sdk.XunitException($"{key} is {other.GetType()}"),
        };

        private static AboutWindow Shown() {
            var window = new AboutWindow("v 1.1.0u based on 2.4.0");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static Rect BoundsIn(Visual root, Control control) =>
            new(control.TranslatePoint(new Point(0, 0), root)!.Value, control.Bounds.Size);

        [AvaloniaFact]
        public void About_IsAPlainFixedWindow_WithNoButtonAndNoCard() {
            AboutWindow window = Shown();

            Assert.Equal("About Foreman 2", window.Title);
            Assert.False(window.CanResize);
            Assert.Equal(280, window.Width);
            Assert.Equal(SizeToContent.Height, window.SizeToContent);
            Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
            Assert.Equal(TokenColor("Foreman.Color.WindowBackground"), BrushColor(window.Background));
            Assert.Empty(window.GetVisualDescendants().OfType<Button>());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.CornerRadius == new CornerRadius(12));

            bool closed = false;
            window.Closed += (_, _) => closed = true;
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(closed);
        }

        [AvaloniaFact]
        public void About_Content_Is64PointIcon_ThenNameVersionTagline_OnTheRampAtTheRuledGaps() {
            AboutWindow window = Shown();
            Window root = window;

            Rect icon = BoundsIn(root, window.IconImageControl);
            Rect name = BoundsIn(root, window.AppNameTextBlockControl);
            Rect version = BoundsIn(root, window.VersionTextBlockControl);
            Rect tagline = BoundsIn(root, window.TaglineTextBlockControl);

            Assert.IsAssignableFrom<Bitmap>(window.IconImageControl.Source);
            Assert.Equal(new Size(64, 64), icon.Size);
            Assert.Equal(24, icon.Top, 1);
            Assert.Equal(icon.Bottom + 12, name.Top, 0.5);
            Assert.Equal(name.Bottom + 2, version.Top, 0.5);
            Assert.Equal(version.Bottom + 8, tagline.Top, 0.5);
            Assert.Equal(tagline.Bottom + 24, window.ClientSize.Height, 0.5);
            Color secondary = TokenColor("Foreman.Color.TextSecondary");

            Assert.Equal("Foreman 2", window.AppNameTextBlockControl.Text);
            Assert.Equal((double)Token("Foreman.Type.EmphasizedBody.FontSize"), window.AppNameTextBlockControl.FontSize);
            Assert.Equal(FontWeight.SemiBold, window.AppNameTextBlockControl.FontWeight);
            Assert.Equal(TokenColor("Foreman.Color.Text"), BrushColor(window.AppNameTextBlockControl.Foreground));

            Assert.Equal((double)Token("Foreman.Type.Caption.FontSize"), window.VersionTextBlockControl.FontSize);
            Assert.Equal(secondary, BrushColor(window.VersionTextBlockControl.Foreground));
            Assert.IsType<SelectableTextBlock>(window.VersionTextBlockControl);

            Assert.Equal("A Factorio production-chain planner", window.TaglineTextBlockControl.Text);
            Assert.Equal((double)Token("Foreman.Type.Callout.FontSize"), window.TaglineTextBlockControl.FontSize);
            Assert.Equal(secondary, BrushColor(window.TaglineTextBlockControl.Foreground));
            Assert.DoesNotContain(RetiredAboutKeys,
                n => Application.Current!.TryGetResource($"Foreman.Type.Legacy.About.{n}.FontSize", null, out _));
        }

        [AvaloniaFact]
        public void About_ShowsTheVersion_InTheSameFormatAsTheSettingsFooter() {
            var about = new AboutWindow();
            var settings = new SettingsWindow(new SettingsWindow.SettingsWindowOptions(new DataCache(filterRecipes: true)) { Presets = [] });

            Assert.Equal(AppVersion.VersionedDisplay, about.VersionTextBlockControl.Text);
            Assert.Equal("Foreman 2 · " + about.VersionTextBlockControl.Text, settings.FooterTextControl.Text);
        }
    }
}
