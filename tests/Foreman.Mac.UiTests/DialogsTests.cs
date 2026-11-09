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
    public class DialogsTests {
        private static Window NewOwner() {
            var owner = new Window { Width = 500, Height = 400 };
            owner.Show();
            return owner;
        }

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

        [Fact]
        public void DocumentName_StripsTheExtension_AndMiddleTruncatesPastFortyCharacters() {
            Assert.Equal("Iron smelting", Dialogs.DocumentName("/tmp/graphs/Iron smelting.fjson"));
            Assert.Equal("Space Age", Dialogs.MiddleTruncate("Space Age"));

            string truncated = Dialogs.MiddleTruncate("Space Age + Krastorio 2 + Bob’s Metals, Chemicals and Plates");
            Assert.Equal("Space Age + Krastori…hemicals and Plates", truncated);
            Assert.Equal(40, truncated.Length);
        }

        [AvaloniaFact]
        public async Task ShowWarningAsync_HostFree_YesButtonClick_Completes() {
            Window owner = NewOwner();

            Task task = Dialogs.ShowWarningAsync(owner, "Cannot load save", "message");
            Dispatcher.UIThread.RunJobs();

            var sheet = Assert.IsType<AlertSheet>(SheetHost.For(owner).Content);
            Assert.Equal("Cannot load save", sheet.FrameControl.Headline);
            sheet.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task;

            Assert.False(SheetHost.For(owner).IsOpen);
        }

        [AvaloniaFact]
        public async Task ShowConfirmAsync_HostBusyWithAnotherSheet_PresentsAStackedAlertSheet_AndTheBlockingSheetStaysOpen() {
            Window owner = NewOwner();
            SheetHost host = SheetHost.For(owner);
            var blocking = new AlertSheet("", "blocking sheet", AlertButtons.Ok);
            _ = host.ShowAsync(blocking);
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsOpen);

            Task<bool> task = Dialogs.ShowConfirmAsync(owner, "Confirm", "message");
            Dispatcher.UIThread.RunJobs();

            AlertSheet stacked = host.GetVisualDescendants().OfType<AlertSheet>().Single(s => !ReferenceEquals(s, blocking));
            stacked.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(await task);
            Assert.Same(blocking, host.Content);
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
            Assert.Equal("Cannot load save", stacked.FrameControl.Headline);
            stacked.YesButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await task;
            Assert.True(host.IsOpen);
        }

        private static string? RenderedText(AlertSheet sheet, string part) =>
            sheet.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == part).Text;
    }
}
