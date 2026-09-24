using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Sheets {
    //Routing lives in Dialogs.cs's tests; AlertFrame's geometry and keys are pinned in the kit suite.
    public class AlertSheetTests {
        [AvaloniaFact]
        public void Message_ShowsVerbatim_AsTheHeadlineWhenUntitled_AndAsInformativeTextUnderATitle() {
            var untitled = new AlertSheet("", "This preset name is already in use.", AlertButtons.Ok);
            var titled = new AlertSheet("Couldn’t open “Iron smelting”.", "The file is too old or damaged to read.", AlertButtons.Ok);

            Assert.Equal("This preset name is already in use.", untitled.FrameControl.Headline);
            Assert.Null(untitled.FrameControl.InformativeText);
            Assert.Equal("Couldn’t open “Iron smelting”.", titled.FrameControl.Headline);
            Assert.Equal("The file is too old or damaged to read.", titled.FrameControl.InformativeText);
        }

        [AvaloniaFact]
        public void AlertSheet_Width_IsSheetWidthAlert() {
            var window = new Window { Width = 800, Height = 600 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new AlertSheet("Cannot load save", "message", AlertButtons.Ok);

            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();

            Assert.True(Application.Current!.TryGetResource("Foreman.Metrics.SheetWidthAlert", Application.Current.ActualThemeVariant, out object? width));
            Border panel = sheet.FrameControl.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");
            Assert.Equal(Assert.IsType<double>(width), panel.Bounds.Width, 0.5);
            Assert.Same(sheet, host.Children.OfType<AlertSheet>().Single());
        }

        private static (SheetHost Host, Task<ConfirmChoice> Task) Present(AlertSheet sheet) {
            var window = new Window { Width = 500, Height = 400 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            Task<ConfirmChoice> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            return (host, task);
        }

        private static string[] Labels(AlertSheet sheet) => [.. sheet.Buttons!.Items.Cast<Button>().Select(b => (string)b.Content!)];

        //---- Ok: one button, labeled OK, the sheet's own default; Escape completes it --------------------

        [AvaloniaFact]
        public async Task OkMode_OneOKButton_TheDefault_AndEscapeResolvesYes() {
            var sheet = new AlertSheet("Couldn’t open “Iron smelting”.", "The file is too old or damaged to read.", AlertButtons.Ok);
            Assert.Equal(["OK"], Labels(sheet));
            Assert.Same(sheet.YesButtonControl, sheet.DefaultButton);

            (_, Task<ConfirmChoice> task) = Present(sheet);
            CanvasKeys.KeyDownOn(sheet.YesButtonControl, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.True(task.IsCompleted);

            Assert.Equal(ConfirmChoice.Yes, await task);
        }

        //---- YesNo: the plain confirm the sheets still raise; Yes is the default, Escape and No resolve No

        [AvaloniaFact]
        public async Task YesNoMode_YesIsTheDefault_EscapeAndTheNoButtonResolveNo_AndAVerbReplacesYesNo() {
            var sheet = new AlertSheet("", "message", AlertButtons.YesNo);
            Assert.Equal([sheet.NoButtonControl, sheet.YesButtonControl], sheet.Buttons!.Items.Cast<Button>());
            Assert.Equal("Yes", sheet.YesButtonControl.Content);
            Assert.Same(sheet.YesButtonControl, sheet.DefaultButton);

            (SheetHost host, Task<ConfirmChoice> escaped) = Present(sheet);
            CanvasKeys.KeyDownOn(sheet.YesButtonControl, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.True(escaped.IsCompleted);
            Assert.Equal(ConfirmChoice.No, await escaped);
            AnimationClock.Settle();

            var clicked = new AlertSheet("", "message", AlertButtons.YesNo);
            Task<ConfirmChoice> task = host.ShowAsync(clicked);
            Dispatcher.UIThread.RunJobs();
            clicked.NoButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(task.IsCompleted);
            Assert.Equal(ConfirmChoice.No, await task);
            AnimationClock.Settle();

            //With a verb the confirm reads Cancel · verb, the verb the default Return fires.
            var verbed = new AlertSheet("Use this save anyway?", "The save’s mods don’t match the current preset’s.", AlertButtons.YesNo, "Use Save");
            Assert.Equal(["Cancel", "Use Save"], verbed.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.Equal(ButtonRowRole.Cancel, ButtonRow.GetRole(verbed.NoButtonControl));
            Assert.Same(verbed.YesButtonControl, verbed.Buttons.ResolveDefaultButton());
            Task<ConfirmChoice> confirmed = host.ShowAsync(verbed);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(verbed.YesButtonControl, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ConfirmChoice.Yes, await confirmed);
        }

        //---- YesNoCancel: the save prompt - Don't Save · Cancel · Save, Save the default -------------------

        [AvaloniaFact]
        public async Task SaveAlert_DontSaveCancelSave_SaveIsTheDefault_ReturnSaves_EscapeAndCmdPeriodCancel() {
            var sheet = new AlertSheet("Do you want to save?", "message", AlertButtons.YesNoCancel);
            Assert.Equal([sheet.NoButtonControl, sheet.CancelButtonControl, sheet.YesButtonControl], sheet.Buttons!.Items.Cast<Button>());
            Assert.Equal(["Don’t Save", "Cancel", "Save"], Labels(sheet));
            Assert.Equal(ButtonRowRole.Other, ButtonRow.GetRole(sheet.NoButtonControl));
            Assert.Same(sheet.YesButtonControl, sheet.DefaultButton);

            (SheetHost host, Task<ConfirmChoice> returned) = Present(sheet);
            Assert.Contains("primary", sheet.YesButtonControl.Classes);
            Assert.DoesNotContain(sheet.Buttons!.Items.Cast<Button>(), b => b.Classes.Contains("destructive"));
            Assert.DoesNotContain(sheet.Buttons!.Items.Cast<Button>(), b => b != sheet.YesButtonControl && b.Classes.Contains("primary"));
            CanvasKeys.KeyDownOn(sheet.NoButtonControl, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(returned.IsCompleted);
            Assert.Equal(ConfirmChoice.Yes, await returned);

            foreach ((Key key, KeyModifiers modifiers) in new[] { (Key.Escape, KeyModifiers.None), (Key.OemPeriod, KeyModifiers.Meta) }) {
                AnimationClock.Settle();
                var cancelled = new AlertSheet("Do you want to save?", "message", AlertButtons.YesNoCancel);
                Task<ConfirmChoice> task = host.ShowAsync(cancelled);
                Dispatcher.UIThread.RunJobs();
                CanvasKeys.KeyDownOn(cancelled.YesButtonControl, key, modifiers);
                Dispatcher.UIThread.RunJobs();
                Assert.True(task.IsCompleted);
                Assert.Equal(ConfirmChoice.Cancel, await task);
            }
        }

        [AvaloniaFact]
        public async Task SaveAlert_CmdDAndCmdBackspace_FireDontSave() {
            var window = new Window { Width = 500, Height = 400 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            foreach (Key key in new[] { Key.D, Key.Back }) {
                var sheet = new AlertSheet("Do you want to save?", "message", AlertButtons.YesNoCancel);
                Task<ConfirmChoice> task = host.ShowAsync(sheet);
                Dispatcher.UIThread.RunJobs();
                CanvasKeys.KeyDownOn(sheet.YesButtonControl, key, KeyModifiers.Meta);
                Dispatcher.UIThread.RunJobs();
                Assert.True(task.IsCompleted);
                Assert.Equal(ConfirmChoice.No, await task);
                AnimationClock.Settle();
            }
        }

        //---- Destructive: the verb is destructive and never the default; Cancel keeps Return -------------

        [AvaloniaFact]
        public async Task DestructiveAlert_VerbThenCancel_ReturnCancels_CmdBackspaceConfirms() {
            var sheet = new AlertSheet("Discard this unsaved graph?", "message", AlertButtons.Destructive, "Discard");
            Assert.Equal([sheet.YesButtonControl, sheet.NoButtonControl], sheet.Buttons!.Items.Cast<Button>());
            Assert.Equal(["Discard", "Cancel"], Labels(sheet));
            Assert.Equal(ButtonRowRole.Destructive, ButtonRow.GetRole(sheet.YesButtonControl));
            Assert.Equal(ButtonRowRole.Cancel, ButtonRow.GetRole(sheet.NoButtonControl));

            (SheetHost host, Task<ConfirmChoice> returned) = Present(sheet);
            Assert.Same(sheet.NoButtonControl, sheet.Buttons!.ResolveDefaultButton());
            Assert.Contains("primary", sheet.NoButtonControl.Classes);
            Assert.Contains("destructive", sheet.YesButtonControl.Classes);
            CanvasKeys.KeyDownOn(sheet.YesButtonControl, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(returned.IsCompleted);
            Assert.Equal(ConfirmChoice.No, await returned);
            AnimationClock.Settle();

            var confirmed = new AlertSheet("Discard this unsaved graph?", "message", AlertButtons.Destructive, "Discard");
            Task<ConfirmChoice> task = host.ShowAsync(confirmed);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(confirmed.NoButtonControl, Key.Back, KeyModifiers.Meta);
            Dispatcher.UIThread.RunJobs();
            Assert.True(task.IsCompleted);
            Assert.Equal(ConfirmChoice.Yes, await task);
        }
    }
}
