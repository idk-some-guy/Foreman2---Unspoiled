using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Foreman.Mac.Views;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Task 10 (last C1 gate item): the About panel's own window - a real .about-card (design doc), not a
    //plain MessageDialog. See MainWindowShellTests.AboutPanel_ShowsAppVersion/MatchesSpecFormatVerbatim
    //for the version-text assertions driven through MainWindow.RealShowAboutAsync.
    public class AboutWindowTests {
        [AvaloniaFact]
        public void Icon_HasAppIconSourceAt56x56() {
            var window = new AboutWindow("v 1.1.0u based on 2.4.0");

            Assert.IsAssignableFrom<Bitmap>(window.IconImageControl.Source);
            Assert.Equal(56, window.IconImageControl.Width);
            Assert.Equal(56, window.IconImageControl.Height);
        }

        [AvaloniaFact]
        public void TextBlocks_AreCenteredWithTheExpectedTexts() {
            var window = new AboutWindow("v 1.1.0u based on 2.4.0");

            Assert.Equal("Foreman 2", window.AppNameTextBlockControl.Text);
            Assert.Equal(HorizontalAlignment.Center, window.AppNameTextBlockControl.HorizontalAlignment);

            Assert.Equal("v 1.1.0u based on 2.4.0", window.VersionTextBlockControl.Text);
            Assert.Equal(HorizontalAlignment.Center, window.VersionTextBlockControl.HorizontalAlignment);

            Assert.Equal("A Factorio production-chain planner", window.TaglineTextBlockControl.Text);
            Assert.Equal(HorizontalAlignment.Center, window.TaglineTextBlockControl.HorizontalAlignment);
        }

        [AvaloniaFact]
        public void TypeTokenKeys_ResolveThroughAppResources() {
            Assert.True(Application.Current!.TryGetResource("Foreman.Type.Legacy.About.Name.FontSize", ThemeVariant.Light, out object? name));
            Assert.Equal(15d, Assert.IsType<double>(name));

            Assert.True(Application.Current!.TryGetResource("Foreman.Type.Legacy.About.Version.FontSize", ThemeVariant.Light, out object? version));
            Assert.Equal(11.5d, Assert.IsType<double>(version));

            Assert.True(Application.Current!.TryGetResource("Foreman.Type.Legacy.About.Tagline.FontSize", ThemeVariant.Light, out object? tagline));
            Assert.Equal(10.5d, Assert.IsType<double>(tagline));
        }

        [AvaloniaFact]
        public void NonResizable_SizedToContent_CenteredOnOwner() {
            var window = new AboutWindow("v 1.1.0u based on 2.4.0");

            Assert.False(window.CanResize);
            Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
            Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
        }
    }
}
