using Avalonia.Controls;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Views.Sheets;
using System.IO;
using System.Threading.Tasks;

namespace Foreman.Mac {
    public enum ConfirmChoice { Yes, No }

    //Minimal UserMessages.Show substitution for Avalonia (no built-in MessageBox); later phases reuse this for other warning/info popups.
    public static class Dialogs {
        public static Task ShowWarningAsync(Window owner, string title, string message) =>
            ShowMessageAsync(owner, title, message);

        //Minimal UserMessages.Show(text) substitution (upstream's caption-less, OK-only overload) - the
        //cross-preset silent-switch notice (io-reference.md §8) is the first caller.
        public static Task ShowInfoAsync(Window owner, string message) =>
            ShowMessageAsync(owner, "", message);

        private static Task<ConfirmChoice> ShowMessageAsync(Window owner, string title, string message) =>
            SheetHost.For(owner).ShowStackedAsync(new AlertSheet(title, message, AlertButtons.Ok));

        //Minimal UserMessages.Show(..., MessageBoxButtons.YesNo) substitution - WinForms' MessageBoxButtons
        //enum has no Avalonia equivalent, so this is its own small dialog rather than a MessageDialog flag.
        public static async Task<bool> ShowConfirmAsync(Window owner, string title, string message, string? verb = null, SheetWidthClass width = SheetWidthClass.Alert) {
            ConfirmChoice choice = await SheetHost.For(owner).ShowStackedAsync(new AlertSheet(title, message, AlertButtons.YesNo, verb, width)).ConfigureAwait(true);
            return choice == ConfirmChoice.Yes;
        }

        public static async Task<bool> ShowDestructiveConfirmAsync(Window owner, string headline, string message, string verb) {
            ConfirmChoice choice = await SheetHost.For(owner).ShowStackedAsync(new AlertSheet(headline, message, AlertButtons.Destructive, verb)).ConfigureAwait(true);
            return choice == ConfirmChoice.Yes;
        }

        public static string DocumentName(string path) => MiddleTruncate(Path.GetFileNameWithoutExtension(path));

        public static string MiddleTruncate(string text, int max = 40) {
            if (text.Length <= max)
                return text;
            int head = max / 2;
            return text[..head] + "…" + text[^(max - head - 1)..];
        }
    }
}
