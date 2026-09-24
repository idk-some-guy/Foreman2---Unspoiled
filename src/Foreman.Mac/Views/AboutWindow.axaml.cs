using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;

namespace Foreman.Mac.Views {
    public partial class AboutWindow : Window, IThemeAware {
        public string VersionLabel { get; }

        private readonly WindowMinSizeScale minSizeScale;

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
            minSizeScale = new WindowMinSizeScale(this);

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) {
                e.Handled = true;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }

        public void RefreshTheme() => minSizeScale.Apply();

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
