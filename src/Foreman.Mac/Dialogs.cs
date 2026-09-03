using Avalonia.Controls;
using Foreman.Mac.Views;
using System.Threading.Tasks;

namespace Foreman.Mac {
    //Mirrors WinForms' DialogResult subset that MessageBoxButtons.YesNoCancel can return.
    public enum ConfirmChoice { Yes, No, Cancel }

    //Minimal UserMessages.Show substitution for Avalonia (no built-in MessageBox); later phases reuse this for other warning/info popups.
    public static class Dialogs {
        public static Task ShowWarningAsync(Window owner, string title, string message) {
            var dialog = new MessageDialog(message) { Title = title };
            return dialog.ShowDialog(owner);
        }

        //Minimal UserMessages.Show(text) substitution (upstream's caption-less, OK-only overload) - the
        //cross-preset silent-switch notice (io-reference.md §8) is the first caller.
        public static Task ShowInfoAsync(Window owner, string message) {
            var dialog = new MessageDialog(message);
            return dialog.ShowDialog(owner);
        }

        //Minimal UserMessages.Show(..., MessageBoxButtons.YesNo) substitution - WinForms' MessageBoxButtons
        //enum has no Avalonia equivalent, so this is its own small dialog rather than a MessageDialog flag.
        public static async Task<bool> ShowConfirmAsync(Window owner, string title, string message) {
            var dialog = new ConfirmDialog(message) { Title = title };
            bool? result = await dialog.ShowDialog<bool?>(owner).ConfigureAwait(true);
            return result == true;
        }

        //Minimal UserMessages.Show(..., MessageBoxButtons.YesNoCancel) substitution, for
        //TestGraphSavedStatus's "graph has been modified" prompt (reference io-reference.md §2).
        public static async Task<ConfirmChoice> ShowYesNoCancelAsync(Window owner, string title, string message) {
            var dialog = new YesNoCancelDialog(message) { Title = title };
            ConfirmChoice? result = await dialog.ShowDialog<ConfirmChoice?>(owner).ConfigureAwait(true);
            return result ?? ConfirmChoice.Cancel;
        }
    }
}
