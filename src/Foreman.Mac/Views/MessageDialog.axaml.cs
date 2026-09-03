using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Foreman.Mac.Views {
    public partial class MessageDialog : Window {
        private readonly TextBlock messageTextBlock;

        public MessageDialog() : this("") {
        }

        public MessageDialog(string message) {
            InitializeComponent();
            messageTextBlock = this.FindControl<TextBlock>("MessageTextBlock")!;
            messageTextBlock.Text = message;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
    }
}
