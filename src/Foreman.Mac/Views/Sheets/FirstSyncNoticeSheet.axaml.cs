using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Views.Research;
using System.Collections.Generic;

namespace Foreman.Mac.Views.Sheets {
    public enum FirstSyncChoice { Cancel = 0, Clear, Keep }

    public partial class FirstSyncNoticeSheet : SheetContent<FirstSyncChoice>, IAlertContent {
        private readonly AlertFrame frame;
        private readonly Button keepButton;
        private readonly Button cancelButton;
        private readonly Button clearButton;

        public FirstSyncNoticeSheet() : this([]) {
        }

        public FirstSyncNoticeSheet(IReadOnlyList<string> nodeNames) {
            InitializeComponent();
            WidthClass = SheetWidthClass.Standard;

            bool single = nodeNames.Count == 1;
            frame = this.FindControl<AlertFrame>("Frame")!;
            frame.WidthClass = WidthClass;
            frame.Icon = AlertSheet.AppIcon;
            Title = single
                ? "One node already has extra productivity typed in."
                : ResearchText.CountWords(nodeNames.Count, true) + " nodes already have extra productivity typed in.";
            frame.Headline = Title;
            frame.InformativeText = single
                ? "Clear the typed value so it isn’t counted twice, or keep it as an extra on top of research."
                : "Research from the save now adds productivity by itself. Clear the typed values so it isn’t counted twice, or keep them as extras on top of research.";
            if (nodeNames.Count > 0)
                frame.InformativeText += "\n\n" + SaveModComparison.NameList(nodeNames);

            ButtonRow row = this.FindControl<ButtonRow>("Row")!;
            keepButton = SheetButtons.Make(single ? "Keep It" : "Keep Them", ButtonRowRole.Other);
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            clearButton = SheetButtons.Make(single ? "Clear It" : "Clear Them", ButtonRowRole.Default);
            row.Items.Add(keepButton);
            row.Items.Add(cancelButton);
            row.Items.Add(clearButton);
            Buttons = row;
            DefaultButton = clearButton;

            keepButton.Click += (_, _) => Complete(FirstSyncChoice.Keep);
            cancelButton.Click += (_, _) => Complete(FirstSyncChoice.Cancel);
            clearButton.Click += (_, _) => Complete(FirstSyncChoice.Clear);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public AlertFrame FrameControl => frame;
        internal string HeadlineText => frame.Headline ?? "";
        internal string InformativeText => frame.InformativeText ?? "";
        internal Button KeepButtonControl => keepButton;
        internal Button CancelButtonControl => cancelButton;
        internal Button ClearButtonControl => clearButton;
    }
}
