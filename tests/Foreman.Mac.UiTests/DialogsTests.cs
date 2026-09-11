using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Views.Sheets;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Covers Dialogs.cs's own routing decision (C2c Task 3): every alert presents as an AlertSheet on the
    //owner's own SheetHost via ShowStackedAsync - directly when that host is free, stacked on top of
    //whatever's already open otherwise (the deadlock a queued-behind AlertSheet would hit - see
    //SheetHostTests' own stacking coverage). AlertSheetTests covers AlertSheet's own button/default/Escape
    //wiring.
    public class DialogsTests {
        private static Window NewOwner() {
            var owner = new Window { Width = 500, Height = 400 };
            owner.Show();
            return owner;
        }

        //---- host free: routes through AlertSheet -------------------------------------------------------

        [AvaloniaFact]
        public async Task ShowConfirmAsync_HostFree_YesButtonClick_ReturnsTrue() {
            Window owner = NewOwner();

            Task<bool> task = Dialogs.ShowConfirmAsync(owner, "Confirm", "message");
            Dispatcher.UIThread.RunJobs();

            var sheet = Assert.IsType<AlertSheet>(SheetHost.For(owner).Content);
            sheet.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(await task);
        }

        [AvaloniaFact]
        public async Task ShowConfirmAsync_HostFree_NoButtonClick_ReturnsFalse() {
            Window owner = NewOwner();

            Task<bool> task = Dialogs.ShowConfirmAsync(owner, "Confirm", "message");
            Dispatcher.UIThread.RunJobs();

            var sheet = Assert.IsType<AlertSheet>(SheetHost.For(owner).Content);
            sheet.NoButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(await task);
        }

        [AvaloniaFact]
        public async Task ShowYesNoCancelAsync_HostFree_CancelButtonClick_ReturnsCancel() {
            Window owner = NewOwner();

            Task<Foreman.Mac.ConfirmChoice> task = Dialogs.ShowYesNoCancelAsync(owner, "Save?", "message");
            Dispatcher.UIThread.RunJobs();

            var sheet = Assert.IsType<AlertSheet>(SheetHost.For(owner).Content);
            sheet.CancelButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Foreman.Mac.ConfirmChoice.Cancel, await task);
        }

        [AvaloniaFact]
        public async Task ShowWarningAsync_HostFree_YesButtonClick_Completes() {
            Window owner = NewOwner();

            Task task = Dialogs.ShowWarningAsync(owner, "Cannot load save", "message");
            Dispatcher.UIThread.RunJobs();

            var sheet = Assert.IsType<AlertSheet>(SheetHost.For(owner).Content);
            Assert.Equal("Cannot load save", sheet.TitleTextBlockControl.Text);
            sheet.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task;

            Assert.False(SheetHost.For(owner).IsOpen);
        }

        //---- host busy: stacks the AlertSheet above the open sheet instead of queuing behind it ----------

        [AvaloniaFact]
        public async Task ShowConfirmAsync_HostBusyWithAnotherSheet_PresentsAStackedAlertSheet_AndTheBlockingSheetStaysOpen() {
            Window owner = NewOwner();
            SheetHost host = SheetHost.For(owner);
            var blocking = new AlertSheet("", "blocking sheet", AlertButtons.Ok);
            _ = host.ShowAsync(blocking); //left open on purpose - never completed in this test
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsOpen);

            Task<bool> task = Dialogs.ShowConfirmAsync(owner, "Confirm", "message");
            Dispatcher.UIThread.RunJobs();

            AlertSheet stacked = host.GetVisualDescendants().OfType<AlertSheet>().Single(s => !ReferenceEquals(s, blocking));
            stacked.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(await task);
            Assert.Same(blocking, host.Content); //the blocking sheet's own Result is still pending
        }

        [AvaloniaFact]
        public async Task ShowWarningAsync_HostBusyWithAnotherSheet_PresentsAStackedAlertSheet() {
            Window owner = NewOwner();
            SheetHost host = SheetHost.For(owner);
            var blocking = new AlertSheet("", "blocking sheet", AlertButtons.Ok);
            _ = host.ShowAsync(blocking);
            Dispatcher.UIThread.RunJobs();

            Task task = Dialogs.ShowWarningAsync(owner, "Cannot load save", "message");
            Dispatcher.UIThread.RunJobs();

            AlertSheet stacked = host.GetVisualDescendants().OfType<AlertSheet>().Single(s => !ReferenceEquals(s, blocking));
            Assert.Equal("Cannot load save", stacked.TitleTextBlockControl.Text);
            stacked.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await task;
            Assert.True(host.IsOpen); //the blocking sheet is still open
        }

        [AvaloniaFact]
        public async Task ShowYesNoCancelAsync_HostBusyWithAnotherSheet_PresentsAStackedAlertSheet() {
            Window owner = NewOwner();
            SheetHost host = SheetHost.For(owner);
            var blocking = new AlertSheet("", "blocking sheet", AlertButtons.Ok);
            _ = host.ShowAsync(blocking);
            Dispatcher.UIThread.RunJobs();

            Task<Foreman.Mac.ConfirmChoice> task = Dialogs.ShowYesNoCancelAsync(owner, "Save?", "message");
            Dispatcher.UIThread.RunJobs();

            AlertSheet stacked = host.GetVisualDescendants().OfType<AlertSheet>().Single(s => !ReferenceEquals(s, blocking));
            stacked.NoButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Foreman.Mac.ConfirmChoice.No, await task);
        }
    }
}
