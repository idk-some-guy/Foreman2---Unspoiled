using Avalonia.Controls;
using Foreman.Mac.Views.Sheets;
using System.Threading.Tasks;

namespace Foreman.Mac {
    //Mirrors WinForms' DialogResult subset that MessageBoxButtons.YesNoCancel can return.
    public enum ConfirmChoice { Yes, No, Cancel }

    //Minimal UserMessages.Show substitution for Avalonia (no built-in MessageBox). Every alert presents as
    //an AlertSheet on the owner's own SheetHost, via ShowStackedAsync - stacking on top when that host is
    //already showing another sheet (e.g. a Task 2 sheet's own pre-flight warning) instead of queuing behind
    //its dismissal, which would deadlock.
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
        public static async Task<bool> ShowConfirmAsync(Window owner, string title, string message) {
            ConfirmChoice choice = await SheetHost.For(owner).ShowStackedAsync(new AlertSheet(title, message, AlertButtons.YesNo)).ConfigureAwait(true);
            return choice == ConfirmChoice.Yes;
        }

        //Minimal UserMessages.Show(..., MessageBoxButtons.YesNoCancel) substitution, for
        //TestGraphSavedStatus's "graph has been modified" prompt (reference io-reference.md §2).
        public static Task<ConfirmChoice> ShowYesNoCancelAsync(Window owner, string title, string message) =>
            SheetHost.For(owner).ShowStackedAsync(new AlertSheet(title, message, AlertButtons.YesNoCancel));
    }
}
