using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman.Mac;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Sheets {
    //Covers AlertSheet's own wiring (C2c Task 3, replaces MessageDialog/ConfirmDialog/YesNoCancelDialog on
    //a window that has a SheetHost - Dialogs.cs's own tests cover the routing/fallback decision): button
    //visibility per mode, the affirmative default button, and Escape mapping to the "dismissed without
    //confirming" choice for each mode.
    public class AlertSheetTests {
        [AvaloniaFact]
        public void SheetWidth_Is360() {
            var sheet = new AlertSheet("", "message", AlertButtons.Ok);

            Assert.Equal(360, sheet.SheetWidth);
        }

        [AvaloniaFact]
        public void EmptyTitle_HidesTheTitleTextBlock() {
            var sheet = new AlertSheet("", "message", AlertButtons.Ok);

            Assert.False(sheet.TitleTextBlockControl.IsVisible);
        }

        [AvaloniaFact]
        public void NonEmptyTitle_ShowsItVerbatim() {
            var sheet = new AlertSheet("Confirm Delete", "message", AlertButtons.Ok);

            Assert.True(sheet.TitleTextBlockControl.IsVisible);
            Assert.Equal("Confirm Delete", sheet.TitleTextBlockControl.Text);
        }

        [AvaloniaFact]
        public void MessageTextBlock_ShowsTheMessageVerbatim() {
            var sheet = new AlertSheet("", "This preset name is already in use.", AlertButtons.Ok);

            Assert.Equal("This preset name is already in use.", sheet.MessageTextBlockControl.Text);
        }

        //---- Ok: one button, labeled OK, the sheet's own default -----------------------------------------

        [AvaloniaFact]
        public void OkMode_OnlyYesButtonVisible_LabeledOK() {
            var sheet = new AlertSheet("", "message", AlertButtons.Ok);

            Assert.False(sheet.CancelButtonControl.IsVisible);
            Assert.False(sheet.NoButtonControl.IsVisible);
            Assert.True(sheet.YesButtonControl.IsVisible);
            Assert.Equal("OK", sheet.YesButtonControl.Content);
            Assert.Same(sheet.YesButtonControl, sheet.DefaultButton);
        }

        [AvaloniaFact]
        public async Task OkMode_Escape_ResolvesYes() {
            var window = new Window { Width = 500, Height = 400 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new AlertSheet("", "message", AlertButtons.Ok);

            Task<ConfirmChoice> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet.YesButtonControl, Key.Escape);

            Assert.Equal(ConfirmChoice.Yes, await task);
        }

        //---- YesNo: Cancel hidden, Yes is the default, Escape resolves No --------------------------------

        [AvaloniaFact]
        public void YesNoMode_CancelHidden_YesAndNoVisible_YesLabeled() {
            var sheet = new AlertSheet("", "message", AlertButtons.YesNo);

            Assert.False(sheet.CancelButtonControl.IsVisible);
            Assert.True(sheet.NoButtonControl.IsVisible);
            Assert.True(sheet.YesButtonControl.IsVisible);
            Assert.Equal("Yes", sheet.YesButtonControl.Content);
            Assert.Same(sheet.YesButtonControl, sheet.DefaultButton);
        }

        [AvaloniaFact]
        public async Task YesNoMode_Escape_ResolvesNo() {
            var window = new Window { Width = 500, Height = 400 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new AlertSheet("", "message", AlertButtons.YesNo);

            Task<ConfirmChoice> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet.YesButtonControl, Key.Escape);

            Assert.Equal(ConfirmChoice.No, await task);
        }

        [AvaloniaFact]
        public async Task YesNoMode_NoButtonClick_ResolvesNo() {
            var window = new Window { Width = 500, Height = 400 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new AlertSheet("", "message", AlertButtons.YesNo);

            Task<ConfirmChoice> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            sheet.NoButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(ConfirmChoice.No, await task);
        }

        //---- YesNoCancel: three buttons, Yes is the default, Escape resolves Cancel ----------------------

        [AvaloniaFact]
        public void YesNoCancelMode_AllThreeButtonsVisible_YesIsTheDefault() {
            var sheet = new AlertSheet("", "message", AlertButtons.YesNoCancel);

            Assert.True(sheet.CancelButtonControl.IsVisible);
            Assert.True(sheet.NoButtonControl.IsVisible);
            Assert.True(sheet.YesButtonControl.IsVisible);
            Assert.Same(sheet.YesButtonControl, sheet.DefaultButton);
        }

        [AvaloniaFact]
        public async Task YesNoCancelMode_Escape_ResolvesCancel() {
            var window = new Window { Width = 500, Height = 400 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new AlertSheet("", "message", AlertButtons.YesNoCancel);

            Task<ConfirmChoice> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet.YesButtonControl, Key.Escape);

            Assert.Equal(ConfirmChoice.Cancel, await task);
        }

        [AvaloniaFact]
        public async Task YesNoCancelMode_Enter_ResolvesYes() {
            var window = new Window { Width = 500, Height = 400 };
            window.Show();
            SheetHost host = SheetHost.For(window);
            var sheet = new AlertSheet("", "message", AlertButtons.YesNoCancel);

            Task<ConfirmChoice> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet.YesButtonControl, Key.Enter);

            Assert.Equal(ConfirmChoice.Yes, await task);
        }
    }
}
