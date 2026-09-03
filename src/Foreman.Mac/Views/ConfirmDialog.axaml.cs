using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Foreman.Mac.Views {
    public partial class ConfirmDialog : Window {
        public ConfirmDialog() : this("") {
        }

        public ConfirmDialog(string message) {
            InitializeComponent();
            this.FindControl<TextBlock>("MessageTextBlock")!.Text = message;
            this.FindControl<Button>("YesButton")!.Click += (_, _) => Close(true);
            this.FindControl<Button>("NoButton")!.Click += (_, _) => Close(false);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
