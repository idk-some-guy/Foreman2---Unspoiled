using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Services;

namespace Foreman.Mac.Views {
    public partial class YesNoCancelDialog : Window {
        public YesNoCancelDialog() : this("") {
        }

        public YesNoCancelDialog(string message) {
            InitializeComponent();
            this.FindControl<TextBlock>("MessageTextBlock")!.Text = message;
            this.FindControl<Button>("YesButton")!.Click += (_, _) => Close(ConfirmChoice.Yes);
            this.FindControl<Button>("NoButton")!.Click += (_, _) => Close(ConfirmChoice.No);
            this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(ConfirmChoice.Cancel);
            WindowScaler.Track(this);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
