using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Services;

namespace Foreman.Mac.Views {
    //I-4 (final fix wave) verdict fix: the real About panel from the ratified mock (design doc
    //.about-card), replacing the plain MessageDialog that carried only "Foreman 2\n{version}".
    public partial class AboutWindow : Window {
        public string VersionLabel { get; }

        public AboutWindow() : this(Foreman.AppVersion.VersionedDisplay) {
        }

        public AboutWindow(string versionLabel) {
            VersionLabel = versionLabel;
            DataContext = this;
            InitializeComponent();
            appIconImage = this.FindControl<Image>("AppIconImage")!;
            appNameTextBlock = this.FindControl<TextBlock>("AppNameTextBlock")!;
            versionTextBlock = this.FindControl<TextBlock>("VersionTextBlock")!;
            taglineTextBlock = this.FindControl<TextBlock>("TaglineTextBlock")!;
            WindowScaler.Track(this);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private readonly Image appIconImage;
        private readonly TextBlock appNameTextBlock;
        private readonly TextBlock versionTextBlock;
        private readonly TextBlock taglineTextBlock;

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal Image IconImageControl => appIconImage;
        internal TextBlock AppNameTextBlockControl => appNameTextBlock;
        internal TextBlock VersionTextBlockControl => versionTextBlock;
        internal TextBlock TaglineTextBlockControl => taglineTextBlock;
    }
}
