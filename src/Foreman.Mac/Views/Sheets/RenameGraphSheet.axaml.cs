using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Kit.Composites;

namespace Foreman.Mac.Views.Sheets {
    public partial class RenameGraphSheet : SheetContent<string> {
        private readonly string original;
        private readonly TextBox nameInput;
        private readonly Button cancelButton;
        private readonly Button renameButton;

        public RenameGraphSheet() : this("") {
        }

        public RenameGraphSheet(string currentName) {
            InitializeComponent();
            original = currentName;
            WidthClass = SheetWidthClass.Alert;
            Title = "Rename graph";

            nameInput = this.FindControl<TextBox>("NameInput")!;
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            renameButton = SheetButtons.Make("Rename", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(cancelButton, renameButton);
            DefaultButton = renameButton;

            nameInput.Text = currentName;
            UpdateRenameEnabled();
            nameInput.PropertyChanged += (_, e) => {
                if (e.Property == TextBox.TextProperty)
                    UpdateRenameEnabled();
            };
            renameButton.Click += (_, _) => Commit();
            cancelButton.Click += (_, _) => Cancel();
            Loaded += (_, _) => { nameInput.Focus(); nameInput.SelectAll(); };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private string Trimmed => (nameInput.Text ?? "").Trim();

        private void UpdateRenameEnabled() => renameButton.IsEnabled = Trimmed.Length > 0 && Trimmed != original;

        private void Commit() {
            if (renameButton.IsEnabled)
                Complete(Trimmed);
        }

        internal TextBox NameInputControl => nameInput;
        internal Button CancelButtonControl => cancelButton;
        internal Button RenameButtonControl => renameButton;
    }
}
