using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Foreman.Mac.Kit.Composites;
using System;

namespace Foreman.Mac.Views.Sheets {
    public enum AlertButtons { Ok, YesNo, YesNoCancel, Destructive }

    //Save prompt: Return saves, ⌘D/⌘⌫ don't; destructive: Return cancels, the verb takes a click or ⌘⌫.
    public partial class AlertSheet : SheetContent<ConfirmChoice> {
        private static readonly Lazy<Bitmap> appIcon = new(() => {
            using System.IO.Stream stream = AssetLoader.Open(new Uri("avares://Foreman.Mac/Assets/unspoiled-icon-512.png"));
            return new Bitmap(stream);
        });

        private readonly AlertFrame frame;
        private readonly Button cancelButton;
        private readonly Button noButton;
        private readonly Button yesButton;
        private readonly AlertButtons buttons;

        public AlertSheet() : this("", "", AlertButtons.Ok) {
        }

        //verb labels the affirmative button: required for Destructive, optional for YesNo (then Cancel · verb).
        public AlertSheet(string title, string message, AlertButtons buttons, string? verb = null, SheetWidthClass width = SheetWidthClass.Alert) {
            InitializeComponent();
            this.buttons = buttons;
            WidthClass = width;

            frame = this.FindControl<AlertFrame>("Frame")!;
            frame.WidthClass = width;
            ButtonRow row = this.FindControl<ButtonRow>("Row")!;

            //An untitled alert leads with its message, as NSAlert's messageText does.
            bool titled = !string.IsNullOrEmpty(title);
            Title = titled ? title : message;
            frame.Headline = Title;
            frame.InformativeText = titled ? message : null;
            frame.Icon = appIcon.Value;

            cancelButton = RowButton("Cancel");
            noButton = RowButton(buttons switch {
                AlertButtons.YesNoCancel => "Don’t Save",
                AlertButtons.Destructive => "Cancel",
                AlertButtons.YesNo when verb is not null => "Cancel",
                _ => "No",
            });
            yesButton = RowButton(buttons switch {
                AlertButtons.Ok => "OK",
                AlertButtons.YesNoCancel => "Save",
                AlertButtons.Destructive => verb ?? throw new ArgumentNullException(nameof(verb)),
                _ => verb ?? "Yes",
            });

            switch (buttons) {
                case AlertButtons.Ok:
                    AddTo(row, yesButton, ButtonRowRole.Default);
                    break;
                case AlertButtons.YesNo:
                    AddTo(row, noButton, ButtonRowRole.Cancel);
                    AddTo(row, yesButton, ButtonRowRole.Default);
                    break;
                case AlertButtons.YesNoCancel:
                    AddTo(row, noButton, ButtonRowRole.Other);
                    AddTo(row, cancelButton, ButtonRowRole.Cancel);
                    AddTo(row, yesButton, ButtonRowRole.Default);
                    AddHandler(KeyDownEvent, OnSaveAlertKeyDown, RoutingStrategies.Tunnel);
                    break;
                case AlertButtons.Destructive:
                    AddTo(row, yesButton, ButtonRowRole.Destructive);
                    AddTo(row, noButton, ButtonRowRole.Cancel);
                    break;
            }
            Buttons = row;
            DefaultButton = row.ResolveDefaultButton();

            cancelButton.Click += (_, _) => Complete(ConfirmChoice.Cancel);
            noButton.Click += (_, _) => Complete(ConfirmChoice.No);
            yesButton.Click += (_, _) => Complete(ConfirmChoice.Yes);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnSaveAlertKeyDown(object? sender, KeyEventArgs e) {
            if (e.KeyModifiers != KeyModifiers.Meta || e.Key is not (Key.D or Key.Back))
                return;
            e.Handled = true;
            Complete(ConfirmChoice.No);
        }

        private static Button RowButton(string label) {
            Application.Current!.TryGetResource("Kit.Button", null, out object? theme);
            return new Button { Theme = theme as ControlTheme, Content = label };
        }

        private static void AddTo(ButtonRow row, Button button, ButtonRowRole role) {
            ButtonRow.SetRole(button, role);
            row.Items.Add(button);
        }

        public override void Cancel() => Complete(buttons switch {
            AlertButtons.YesNoCancel => ConfirmChoice.Cancel,
            AlertButtons.YesNo or AlertButtons.Destructive => ConfirmChoice.No,
            _ => ConfirmChoice.Yes,
        });

        //-------------------------------------------------------------------------------------------------Test-only seams

        internal AlertFrame FrameControl => frame;
        internal Button CancelButtonControl => cancelButton;
        internal Button NoButtonControl => noButton;
        internal Button YesButtonControl => yesButton;
    }
}
