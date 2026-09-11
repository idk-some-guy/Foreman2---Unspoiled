using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Foreman.Mac.Views.Sheets {
    public enum AlertButtons { Ok, YesNo, YesNoCancel }

    //One sheet, one button row shaped by AlertButtons; the affirmative button is always the DefaultButton.
    public partial class AlertSheet : SheetContent<ConfirmChoice> {
        private readonly TextBlock titleTextBlock;
        private readonly TextBlock messageTextBlock;
        private readonly Button cancelButton;
        private readonly Button noButton;
        private readonly Button yesButton;
        private readonly AlertButtons buttons;

        public AlertSheet() : this("", "", AlertButtons.Ok) {
        }

        public AlertSheet(string title, string message, AlertButtons buttons) {
            InitializeComponent();
            this.buttons = buttons;
            SheetWidth = 360;
            Title = title;

            titleTextBlock = this.FindControl<TextBlock>("TitleTextBlock")!;
            messageTextBlock = this.FindControl<TextBlock>("MessageTextBlock")!;
            cancelButton = this.FindControl<Button>("CancelButton")!;
            noButton = this.FindControl<Button>("NoButton")!;
            yesButton = this.FindControl<Button>("YesButton")!;

            titleTextBlock.IsVisible = !string.IsNullOrEmpty(title);
            titleTextBlock.Text = title;
            messageTextBlock.Text = message;

            cancelButton.IsVisible = buttons == AlertButtons.YesNoCancel;
            noButton.IsVisible = buttons != AlertButtons.Ok;
            yesButton.Content = buttons == AlertButtons.Ok ? "OK" : "Yes";
            DefaultButton = yesButton;

            cancelButton.Click += (_, _) => Complete(ConfirmChoice.Cancel);
            noButton.Click += (_, _) => Complete(ConfirmChoice.No);
            yesButton.Click += (_, _) => Complete(ConfirmChoice.Yes);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public override void Cancel() => Complete(buttons switch {
            AlertButtons.YesNoCancel => ConfirmChoice.Cancel,
            AlertButtons.YesNo => ConfirmChoice.No,
            _ => ConfirmChoice.Yes,
        });

        //-------------------------------------------------------------------------------------------------Test-only seams

        internal TextBlock TitleTextBlockControl => titleTextBlock;
        internal TextBlock MessageTextBlockControl => messageTextBlock;
        internal Button CancelButtonControl => cancelButton;
        internal Button NoButtonControl => noButton;
        internal Button YesButtonControl => yesButton;
    }
}
