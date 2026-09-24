using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitSheetFrameTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitSheetFrameTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.SheetFrame", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitButtonRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ButtonRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitButtonTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Button", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Button Btn(string text, ButtonRowRole role) {
            var button = new Button { Theme = KitButtonTheme(), Content = text };
            ButtonRow.SetRole(button, role);
            return button;
        }

        private static ButtonRow RowWithCancelAndDefault() {
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(Btn("Cancel", ButtonRowRole.Cancel));
            row.Items.Add(Btn("Save", ButtonRowRole.Default));
            return row;
        }

        private static ButtonRow RowWithDestructiveAndCancel() {
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(Btn("Discard", ButtonRowRole.Destructive));
            row.Items.Add(Btn("Cancel", ButtonRowRole.Cancel));
            return row;
        }

        private static SheetFrame Themed(string? title = "Export an image", object? content = null, ButtonRow? buttons = null) =>
            new() {
                Theme = KitSheetFrameTheme(),
                Title = title,
                Content = content ?? new TextBox(),
                Buttons = buttons ?? RowWithCancelAndDefault(),
            };

        private static Border Panel(SheetFrame sheet) =>
            sheet.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

        private static Window Shown(SheetFrame sheet, double width = 600, double height = 400) {
            var window = new Window { Width = width, Height = height, Content = sheet };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        [AvaloniaFact]
        public void KitSheetFrame_IsAControlThemeTargetingSheetFrame() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(SheetFrame), KitSheetFrameTheme().TargetType);
        }

        [AvaloniaFact]
        public void Panel_MatchesTheSpecChrome() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            sheet.IsOpen = true;
            Shown(sheet);

            Border panel = Panel(sheet);
            Application.Current!.TryGetResource("Foreman.Radius.SheetBottom", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), panel.CornerRadius);
            Assert.Equal(0, panel.BorderThickness.Top);
            Assert.Equal(new Thickness(20), panel.Padding);
        }

        [AvaloniaFact]
        public void WidthClass_ResolvesToTheNamedToken() {
            using var fixture = new KitFixture();
            SheetFrame alert = Themed();
            alert.WidthClass = SheetWidthClass.Alert;
            Shown(alert);
            Assert.Equal(300, Panel(alert).Width);

            SheetFrame standard = Themed();
            standard.WidthClass = SheetWidthClass.Standard;
            Shown(standard);
            Assert.Equal(360, Panel(standard).Width);

            SheetFrame wide = Themed();
            wide.WidthClass = SheetWidthClass.Wide;
            Shown(wide);
            Assert.Equal(460, Panel(wide).Width);
        }

        [AvaloniaFact]
        public void MaxWidth_ClampsToOwnerWidthMinus24() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            Shown(sheet, width: 500);
            Assert.Equal(476, Panel(sheet).MaxWidth);
        }

        [AvaloniaFact]
        public void Headline_EqualsTitle_AsEmphasizedBody() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed(title: "Export an image");
            Shown(sheet);

            TextBlock headline = sheet.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            Assert.Equal("Export an image", headline.Text);
            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Assert.Equal((double)size!, headline.FontSize);
        }

        [AvaloniaFact]
        public void Open_FocusesTheFirstFocusableControl_NotTheDefaultButton() {
            using var fixture = new KitFixture();
            var field = new TextBox();
            SheetFrame sheet = Themed(content: field);
            Shown(sheet);

            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(field.IsFocused);
        }

        [AvaloniaFact]
        public void Close_ReturnsFocusToTheOpener() {
            using var fixture = new KitFixture();
            var opener = new Button { Content = "Open" };
            var root = new StackPanel { Children = { opener } };
            SheetFrame sheet = Themed(content: new TextBox());
            root.Children.Add(sheet);

            var window = new Window { Width = 500, Height = 400, Content = root };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            opener.Focus();
            Dispatcher.UIThread.RunJobs();

            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(opener.IsFocused);

            sheet.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(opener.IsFocused);
        }

        [AvaloniaFact]
        public void Escape_RaisesCancelled_ViaTheRowsCancelButton() {
            using var fixture = new KitFixture();
            ButtonRow row = RowWithCancelAndDefault();
            SheetFrame sheet = Themed(buttons: row);
            sheet.IsOpen = true;
            Shown(sheet);

            int cancelled = 0;
            sheet.Cancelled += (_, _) => cancelled++;

            fixture.StateOf(sheet).KeyDown(Key.Escape);

            Assert.Equal(1, cancelled);
        }

        [AvaloniaFact]
        public void Enter_RaisesClosed_ViaTheRowsDefaultButton() {
            using var fixture = new KitFixture();
            ButtonRow row = RowWithCancelAndDefault();
            SheetFrame sheet = Themed(buttons: row);
            sheet.IsOpen = true;
            Shown(sheet);

            int closed = 0;
            sheet.Closed += (_, _) => closed++;

            fixture.StateOf(sheet).KeyDown(Key.Enter);

            Assert.Equal(1, closed);
        }

        [AvaloniaFact]
        public void IsBusy_DisablesTheDefaultButton_AndKeepsCancelLive() {
            using var fixture = new KitFixture();
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Button save = Btn("Save", ButtonRowRole.Default);
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(cancel);
            row.Items.Add(save);

            SheetFrame sheet = Themed(buttons: row);
            sheet.IsOpen = true;
            sheet.IsBusy = true;
            Shown(sheet);

            Assert.False(save.IsEnabled);
            Assert.True(cancel.IsEnabled);
        }

        [AvaloniaFact]
        public void StateContent_ReplacesContent_WhenSet() {
            using var fixture = new KitFixture();
            var body = new TextBlock { Text = "body" };
            var error = new TextBlock { Text = "error" };
            SheetFrame sheet = Themed(content: body);
            sheet.IsOpen = true;
            Shown(sheet);

            var contentHost = sheet.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "PART_ContentHost");
            var stateHost = sheet.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "PART_StateHost");
            Assert.True(contentHost.IsVisible);
            Assert.False(stateHost.IsVisible);

            sheet.StateContent = error;
            Dispatcher.UIThread.RunJobs();

            Assert.False(contentHost.IsVisible);
            Assert.True(stateHost.IsVisible);
        }

        [AvaloniaFact]
        public void StateContent_AcceptsAKitStatePanel_AndHidesTheSheetsOwnContent() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            sheet.IsOpen = true;
            Shown(sheet);

            Assert.True(Application.Current!.TryGetResource("Kit.StatePanel", null, out object? theme));
            var row = RowWithCancelAndDefault();
            sheet.StateContent = new StatePanel {
                Theme = (ControlTheme)theme!,
                Shape = StatePanelShape.Error,
                Headline = "Couldn’t read that save file",
                Sentence = "The file ends partway through the blueprint section.",
                Buttons = row,
            };
            Dispatcher.UIThread.RunJobs();

            var contentHost = sheet.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "PART_ContentHost");
            Assert.False(contentHost.IsVisible);
            Assert.Single(sheet.GetVisualDescendants().OfType<StatePanel>());
        }

        [AvaloniaFact]
        public void Tab_TrapsFocusInsideTheSheet_WrappingPastTheLastControl() {
            using var fixture = new KitFixture();
            var field = new TextBox();
            ButtonRow row = RowWithCancelAndDefault();
            SheetFrame sheet = Themed(content: field, buttons: row);
            sheet.IsOpen = true;
            Shown(sheet);

            Button save = row.Items.OfType<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Default);
            save.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(sheet).KeyDown(Key.Tab);

            Assert.True(field.IsFocused, "Tab off the last control should wrap back to the first.");
        }

        [AvaloniaFact]
        public void ReturnOnADestructiveAlert_RaisesCancelled_NotClosed() {
            using var fixture = new KitFixture();
            ButtonRow row = RowWithDestructiveAndCancel();
            SheetFrame sheet = Themed(buttons: row);
            sheet.IsOpen = true;
            Shown(sheet);

            int closed = 0, cancelled = 0;
            sheet.Closed += (_, _) => closed++;
            sheet.Cancelled += (_, _) => cancelled++;

            fixture.StateOf(sheet).KeyDown(Key.Enter);

            Assert.Equal(0, closed);
            Assert.Equal(1, cancelled);
        }

        [AvaloniaFact]
        public void CmdPeriod_RaisesCancelled_EvenWithFocusInsideAContentTextBox() {
            using var fixture = new KitFixture();
            var field = new TextBox();
            ButtonRow row = RowWithCancelAndDefault();
            SheetFrame sheet = Themed(content: field, buttons: row);
            sheet.IsOpen = true;
            Shown(sheet);

            int cancelled = 0;
            sheet.Cancelled += (_, _) => cancelled++;
            field.Focus();
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(field).KeyDown(Key.OemPeriod, KeyModifiers.Meta);

            Assert.Equal(1, cancelled);
        }

        [AvaloniaFact]
        public void CmdBackspace_InvokesTheDestructiveAction_EvenWithFocusInsideAContentTextBox() {
            using var fixture = new KitFixture();
            var field = new TextBox();
            ButtonRow row = RowWithDestructiveAndCancel();
            SheetFrame sheet = Themed(content: field, buttons: row);
            sheet.IsOpen = true;
            Shown(sheet);

            Button discard = row.Items.OfType<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Destructive);
            int discardHits = 0;
            discard.Click += (_, _) => discardHits++;
            field.Focus();
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(field).KeyDown(Key.Back, KeyModifiers.Meta);

            Assert.Equal(1, discardHits);
        }

        [AvaloniaFact]
        public void AClosedFrame_LetsAClickThroughToContentBehindIt() {
            using var fixture = new KitFixture();
            int clicks = 0;
            var behind = new Button { Width = 400, Height = 300 };
            behind.Click += (_, _) => clicks++;

            SheetFrame sheet = Themed();
            var root = new Panel { Width = 400, Height = 300, Children = { behind, sheet } };
            fixture.Window.Content = root;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(sheet).Click();

            Assert.Equal(1, clicks);
        }

        [AvaloniaFact]
        public void AutomationPeer_IsAWindowControlType_NamedFromTitle() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed(title: "Export an image");
            Shown(sheet);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(sheet);

            Assert.Equal(AutomationControlType.Window, peer.GetAutomationControlType());
            Assert.Equal("Export an image", peer.GetName());
        }

        [AvaloniaFact]
        public void IsBusy_False_NeverForceEnablesAConsumerDisabledDefault() {
            using var fixture = new KitFixture();
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Button save = Btn("Save", ButtonRowRole.Default);
            save.IsEnabled = false;
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(cancel);
            row.Items.Add(save);

            SheetFrame sheet = Themed(buttons: row);
            Shown(sheet);

            Assert.False(save.IsEnabled);
        }

        [AvaloniaFact]
        public void IsBusy_RoundTrip_RestoresAnEnabledDefault() {
            using var fixture = new KitFixture();
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Button save = Btn("Save", ButtonRowRole.Default);
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(cancel);
            row.Items.Add(save);

            SheetFrame sheet = Themed(buttons: row);
            Shown(sheet);

            sheet.IsBusy = true;
            Assert.False(save.IsEnabled);

            sheet.IsBusy = false;
            Assert.True(save.IsEnabled);
        }

        [AvaloniaFact]
        public void IsBusy_RoundTrip_LeavesAConsumerDisabledDefaultDisabled() {
            using var fixture = new KitFixture();
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Button save = Btn("Save", ButtonRowRole.Default);
            save.IsEnabled = false;
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(cancel);
            row.Items.Add(save);

            SheetFrame sheet = Themed(buttons: row);
            Shown(sheet);

            sheet.IsBusy = true;
            Assert.False(save.IsEnabled);

            sheet.IsBusy = false;
            Assert.False(save.IsEnabled);
        }

        [AvaloniaFact]
        public void Buttons_SwappedWhileBusy_RestoresTheOutgoingDefaultsPriorValue() {
            using var fixture = new KitFixture();
            Button oldSave = Btn("Save", ButtonRowRole.Default);
            var oldRow = new ButtonRow { Theme = KitButtonRowTheme() };
            oldRow.Items.Add(Btn("Cancel", ButtonRowRole.Cancel));
            oldRow.Items.Add(oldSave);

            SheetFrame sheet = Themed(buttons: oldRow);
            sheet.IsBusy = true;
            Shown(sheet);

            Assert.False(oldSave.IsEnabled);

            Button newSave = Btn("Retry", ButtonRowRole.Default);
            var newRow = new ButtonRow { Theme = KitButtonRowTheme() };
            newRow.Items.Add(Btn("Cancel", ButtonRowRole.Cancel));
            newRow.Items.Add(newSave);

            sheet.Buttons = newRow;

            Assert.True(oldSave.IsEnabled);
            Assert.False(newSave.IsEnabled);
        }

        [AvaloniaFact]
        public void Constructor_SetsIsHitTestVisibleFromIsOpen() {
            var sheet = new SheetFrame();
            Assert.False(sheet.IsHitTestVisible);
        }

        [AvaloniaFact]
        public void Detach_DisposesTheWidthBinding_ReCreatedOnReattach() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            sheet.WidthClass = SheetWidthClass.Standard;
            var host = new Panel();
            fixture.Window.Content = host;
            host.Children.Add(sheet);
            Dispatcher.UIThread.RunJobs();
            Border panel = Panel(sheet);
            Assert.Equal(360, panel.Width);

            host.Children.Remove(sheet);
            Dispatcher.UIThread.RunJobs();

            Application.Current!.Resources["Foreman.Metrics.SheetWidthStandard"] = 999.0;
            try {
                host.Children.Add(sheet);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(999, panel.Width);
            } finally {
                Application.Current!.Resources.Remove("Foreman.Metrics.SheetWidthStandard");
            }
        }

        [AvaloniaFact]
        public void ReduceMotion_PanelTransitions_AreOpacityOnly_AtTheSpecDurations() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            SheetFrame sheet = Themed();
            Shown(sheet);
            Border panel = Panel(sheet);

            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();

            var opacity = Assert.Single(panel.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetOpenDuration, opacity.Duration);
            Assert.DoesNotContain(panel.Transitions!, t => t is not DoubleTransition);

            sheet.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            var closeOpacity = Assert.Single(panel.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetCloseDuration, closeOpacity.Duration);
            Assert.DoesNotContain(panel.Transitions!, t => t is not DoubleTransition);
        }

        [AvaloniaFact]
        public void Scrim_TransitionDuration_Is200OnOpen_150OnClose() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            Shown(sheet);
            Scrim scrim = sheet.GetVisualDescendants().OfType<Scrim>().Single();

            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            var openOpacity = Assert.Single(scrim.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetOpenDuration, openOpacity.Duration);

            sheet.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            var closeOpacity = Assert.Single(scrim.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetCloseDuration, closeOpacity.Duration);
        }

        [AvaloniaFact]
        public void OpenAndClose_SettleToTheirFinalOpacity_WithBoundedWork() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            Shown(sheet);
            Border panel = Panel(sheet);

            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            Assert.Equal(1, panel.Opacity);

            sheet.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            Assert.Equal(0, panel.Opacity);
        }

        [AvaloniaFact]
        public void OpenSheet_Settles_ThenPumpsWithZeroLayoutGrowth() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            Shown(sheet);

            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            int layouts = 0;
            sheet.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, layouts);
        }

        private static double Token(Control scope, string key) {
            Assert.True(scope.TryFindResource(key, scope.ActualThemeVariant, out object? value));
            return Assert.IsType<double>(value);
        }

        private static double TranslateY(Border panel) => panel.RenderTransform?.Value.M32 ?? 0;

        [AvaloniaFact]
        public void ClosedPanel_SitsSheetSlideAboveItsPlace_OpenPanelAtIdentity() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            Shown(sheet);
            Border panel = Panel(sheet);

            Assert.Equal(12, Token(sheet, "Foreman.Metrics.SheetSlide"));
            Assert.Equal(-12, TranslateY(panel), 3);

            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            Assert.Equal(0, TranslateY(panel), 3);

            sheet.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            Assert.Equal(-12, TranslateY(panel), 3);
        }

        [AvaloniaFact]
        public void ReduceMotion_ToggledWhileOpen_CloseIsOpacityOnlyInPlace_AndTogglingBackRestoresTheSlide() {
            using var fixture = new KitFixture();
            SheetFrame sheet = Themed();
            Shown(sheet);
            Border panel = Panel(sheet);
            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            double openTop = panel.TranslatePoint(new Point(0, 0), sheet)!.Value.Y;

            Motion.ReduceMotion = true;
            sheet.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            var closeOpacity = Assert.Single(panel.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.SheetCloseDuration, closeOpacity.Duration);
            Assert.DoesNotContain(panel.Transitions!, t => t is TransformOperationsTransition);
            Assert.Equal(openTop, panel.TranslatePoint(new Point(0, 0), sheet)!.Value.Y, 3);
            AnimationClock.Settle();
            Assert.Equal(openTop, panel.TranslatePoint(new Point(0, 0), sheet)!.Value.Y, 3);

            Motion.ReduceMotion = false;
            sheet.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(panel.Transitions!, t => t is TransformOperationsTransition);
            Assert.True(panel.TranslatePoint(new Point(0, 0), sheet)!.Value.Y < openTop - 1);
            AnimationClock.Settle();
            Assert.Equal(openTop, panel.TranslatePoint(new Point(0, 0), sheet)!.Value.Y, 3);

            sheet.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(panel.Transitions!, t => t is TransformOperationsTransition);
            AnimationClock.Settle();
            Assert.Equal(openTop - 12, panel.TranslatePoint(new Point(0, 0), sheet)!.Value.Y, 3);
        }

        private static ColorWell OpenWellIn(out SheetFrame sheet) {
            Assert.True(Application.Current!.TryGetResource("Kit.ColorWell", null, out object? wellTheme));
            var well = new ColorWell { Theme = Assert.IsType<ControlTheme>(wellTheme), Color = Avalonia.Media.Color.Parse("#FF9500"), AccessibleLabel = "Fill" };
            sheet = Themed(content: well);
            sheet.IsOpen = true;
            Shown(sheet);
            AnimationClock.Settle();
            well.IsOpen = true;
            AnimationClock.Settle(300);
            return well;
        }

        //A popover anchored in the body draws above the panel and takes the clicks over it.
        [AvaloniaFact]
        public void Popover_OpenedFromTheBody_IsHostedByTheFrame_AboveThePanel() {
            using var fixture = new KitFixture();
            ColorWell well = OpenWellIn(out SheetFrame sheet);

            PopoverFrame popover = well.Frame!;
            Assert.Same(sheet, popover.FindAncestorOfType<SheetFrame>());
            PickCell cell = popover.GetVisualDescendants().OfType<PickCell>().First();
            Window window = (Window)TopLevel.GetTopLevel(sheet)!;
            Point centre = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
            Assert.Contains(cell, window.InputHitTest(centre) is Visual hit ? hit.GetSelfAndVisualAncestors() : []);
        }

        [AvaloniaFact]
        public void Escape_WithAPopoverOpen_ClosesThePopover_NotTheSheet() {
            using var fixture = new KitFixture();
            ColorWell well = OpenWellIn(out SheetFrame sheet);
            int cancelled = 0;
            sheet.Cancelled += (_, _) => cancelled++;

            fixture.StateOf(well.Popover!.Grid!).KeyDown(Key.Escape);

            Assert.False(well.IsOpen);
            Assert.Equal(0, cancelled);
            Assert.True(sheet.IsOpen);
        }

        [AvaloniaFact]
        public void Return_WithAPopoverOpen_NeverFiresTheDefault() {
            using var fixture = new KitFixture();
            ColorWell well = OpenWellIn(out SheetFrame sheet);
            int closed = 0;
            sheet.Closed += (_, _) => closed++;

            fixture.StateOf(well.Popover!.HexField!).KeyDown(Key.Enter);

            Assert.Equal(0, closed);
            Assert.True(well.IsOpen);
        }

        //A multi-line field keeps plain Return for its newline; Command-Return still reaches the default button.
        [AvaloniaFact]
        public void CommandReturn_InAMultiLineField_FiresTheDefault_PlainReturnDoesNot() {
            using var fixture = new KitFixture();
            var field = new TextBox { AcceptsReturn = true };
            SheetFrame sheet = Themed(content: field);
            sheet.IsOpen = true;
            Shown(sheet);
            int closed = 0;
            sheet.Closed += (_, _) => closed++;

            fixture.StateOf(field).KeyDown(Key.Enter);
            Assert.Equal(0, closed);

            fixture.StateOf(field).KeyDown(Key.Enter, KeyModifiers.Meta);
            Assert.Equal(1, closed);
        }

        private static ColorWell KitWell(string hex) {
            Assert.True(Application.Current!.TryGetResource("Kit.ColorWell", null, out object? wellTheme));
            return new ColorWell { Theme = Assert.IsType<ControlTheme>(wellTheme), Color = Avalonia.Media.Color.Parse(hex), AccessibleLabel = "Fill" };
        }

        private static InputElement? Focused(SheetFrame sheet) =>
            TopLevel.GetTopLevel(sheet)?.FocusManager?.GetFocusedElement() as InputElement;

        private static void AssertNotInAClosedPopover(SheetFrame sheet) {
            InputElement? focused = Focused(sheet);
            Assert.NotNull(focused);
            PopoverFrame? frame = focused as PopoverFrame ?? focused!.FindAncestorOfType<PopoverFrame>();
            Assert.True(frame is null || frame.IsOpen, $"focus landed in a closed popover ({focused})");
        }

        //A popover that opened once stays in the host's surface; closed, it must never join the sheet's tab cycle.
        [AvaloniaFact]
        public void Tab_AfterAPopoverOpenedAndClosed_StillWrapsBetweenTheBodyAndTheButtons() {
            using var fixture = new KitFixture();
            var field = new TextBox();
            ColorWell well = KitWell("#FF9500");
            ButtonRow row = RowWithCancelAndDefault();
            SheetFrame sheet = Themed(content: new StackPanel { Children = { field, well } }, buttons: row);
            sheet.IsOpen = true;
            Shown(sheet);
            AnimationClock.Settle();
            well.IsOpen = true;
            AnimationClock.Settle(300);
            fixture.StateOf(well.Popover!.Grid!).KeyDown(Key.Escape);
            AnimationClock.Settle(300);
            Assert.False(well.IsOpen);

            Button save = row.Items.OfType<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Default);
            save.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(save).KeyDown(Key.Tab);
            AssertNotInAClosedPopover(sheet);
            Assert.True(field.IsFocused, $"Tab off the last button should wrap to the first body control, not {Focused(sheet)}.");

            fixture.StateOf(field).KeyDown(Key.Tab, KeyModifiers.Shift);
            AssertNotInAClosedPopover(sheet);
            Assert.True(save.IsFocused, $"Shift+Tab off the first body control should wrap to the last button, not {Focused(sheet)}.");
        }

        //Tab alternates between the grid and the Hex field; focus never leaves the popover for the sheet's controls.
        [AvaloniaFact]
        public void Tab_InsideAnOpenPopover_StaysInThePopover() {
            using var fixture = new KitFixture();
            ColorWell well = OpenWellIn(out SheetFrame sheet);
            PickGrid grid = well.Popover!.Grid!;
            TextBox hex = well.Popover.HexField!;
            Assert.True(grid.IsKeyboardFocusWithin);

            fixture.StateOf((Control)Focused(sheet)!).KeyDown(Key.Tab);
            Assert.True(hex.IsKeyboardFocusWithin, $"Tab from the grid should reach the Hex field, not {Focused(sheet)}.");
            fixture.StateOf(hex).KeyDown(Key.Tab);
            Assert.True(grid.IsKeyboardFocusWithin, $"Tab from the Hex field should return to the grid, not {Focused(sheet)}.");
            fixture.StateOf((Control)Focused(sheet)!).KeyDown(Key.Tab, KeyModifiers.Shift);
            Assert.True(hex.IsKeyboardFocusWithin);
            Assert.True(well.IsOpen);
        }

        //An Escape the sheet routes to an open popover from outside it cancels, the same as one pressed inside.
        [AvaloniaFact]
        public void Escape_FromOutsideAnOpenPopover_RestoresTheColourItHadWhenItOpened() {
            using var fixture = new KitFixture();
            ColorWell well = OpenWellIn(out SheetFrame sheet);
            Avalonia.Media.Color opened = well.Color;
            well.Color = Avalonia.Media.Color.Parse("#007AFF");
            int cancelled = 0;
            sheet.Cancelled += (_, _) => cancelled++;

            fixture.StateOf(well).KeyDown(Key.Escape);

            Assert.False(well.IsOpen);
            Assert.Equal(opened, well.Color);
            Assert.Equal(0, cancelled);
        }

        [AvaloniaFact]
        public void CommandPeriod_WithAPopoverOpen_CancelsThePopoverFirst_FromInsideAndOutside_ThenTheSheet() {
            using var fixture = new KitFixture();
            ColorWell well = OpenWellIn(out SheetFrame sheet);
            int sheetCancelled = 0;
            sheet.Cancelled += (_, _) => sheetCancelled++;

            foreach (bool fromGrid in new[] { true, false }) {
                if (!well.IsOpen) {
                    well.IsOpen = true;
                    AnimationClock.Settle(300);
                }
                Avalonia.Media.Color opened = well.Color;
                PopoverFrame popover = well.Frame!;
                int popoverCancelled = 0;
                popover.Cancelled += (_, _) => popoverCancelled++;
                well.Color = Avalonia.Media.Color.Parse("#007AFF");

                fixture.StateOf(fromGrid ? well.Popover!.Grid! : well).KeyDown(Key.OemPeriod, KeyModifiers.Meta);

                Assert.False(well.IsOpen);
                Assert.Equal(opened, well.Color);
                Assert.Equal(1, popoverCancelled);
                Assert.Equal(0, sheetCancelled);
                Assert.True(sheet.IsOpen);
            }

            fixture.StateOf(well).KeyDown(Key.OemPeriod, KeyModifiers.Meta);

            Assert.Equal(1, sheetCancelled);
        }
    }
}
