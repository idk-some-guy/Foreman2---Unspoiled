using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman.Mac;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Exercises PlatformModifiers.Primary's platform fork directly (docs/upstream-divergences.md) - the
    //UseIsMacOs seam forces each branch without depending on the host OS, since this box only
    //ever runs the tests on macOS.
    public class PlatformModifiersTests {
        [Fact]
        public void UseIsMacOs_True_ResolvesPrimaryAsMeta() {
            using (PlatformModifiers.UseIsMacOs(true))
                Assert.Equal(KeyModifiers.Meta, PlatformModifiers.Primary);
        }

        [Fact]
        public void UseIsMacOs_False_ResolvesPrimaryAsControl() {
            using (PlatformModifiers.UseIsMacOs(false))
                Assert.Equal(KeyModifiers.Control, PlatformModifiers.Primary);
        }

        [Fact]
        public void UseIsMacOs_Disposed_RestoresThePreviousOverride() {
            using (PlatformModifiers.UseIsMacOs(true)) {
                using (PlatformModifiers.UseIsMacOs(false))
                    Assert.Equal(KeyModifiers.Control, PlatformModifiers.Primary);

                Assert.Equal(KeyModifiers.Meta, PlatformModifiers.Primary);
            }
        }

        private static IEnumerable<NativeMenuItem> AllItems(NativeMenu menu) {
            foreach (NativeMenuItem item in menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator)) {
                yield return item;
                if (item.Menu is NativeMenu submenu)
                    foreach (NativeMenuItem nested in AllItems(submenu))
                        yield return nested;
            }
        }

        //The window menu bakes Primary in at construction, so building it under the Linux override proves every chord maps to Ctrl.
        [AvaloniaFact]
        public void OnLinux_MenuChordsUseControl_AndNoTwoItemsShareOne() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var window = new MainWindow();
            NativeMenuItem[] items = [.. AllItems(NativeMenu.GetMenu(window)!)];
            KeyGesture? GestureOf(string header) => items.Single(i => i.Header == header).Gesture;

            Assert.DoesNotContain(items, i => i.Gesture is { } g && g.KeyModifiers.HasFlag(KeyModifiers.Meta));
            Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), GestureOf("Save"));
            Assert.Equal(new KeyGesture(Key.A, KeyModifiers.Control), GestureOf("Select All"));
            Assert.Equal(new KeyGesture(Key.A, KeyModifiers.Control | KeyModifiers.Shift), GestureOf("Deselect All"));
            Assert.Equal(new KeyGesture(Key.F, KeyModifiers.Control), GestureOf("Find Node…"));
            Assert.Equal(new KeyGesture(Key.G, KeyModifiers.Control), GestureOf("Find Next"));
            Assert.Equal(new KeyGesture(Key.G, KeyModifiers.Control | KeyModifiers.Shift), GestureOf("Find Previous"));
            Assert.Equal(new KeyGesture(Key.D1, KeyModifiers.Control | KeyModifiers.Shift), GestureOf("Welcome to Foreman"));
            Assert.Equal(new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Alt), GestureOf("Pause All Calculations"));
            Assert.Equal(new KeyGesture(Key.T, KeyModifiers.Control | KeyModifiers.Alt), GestureOf("Show Toolbar"));
            Assert.Equal(new KeyGesture(Key.OemQuestion, KeyModifiers.Control | KeyModifiers.Shift), GestureOf("Foreman 2 Help"));
            //⌃⌘F folds onto ⌃F, Find Node's chord, when Primary is Control.
            Assert.Null(GestureOf("Enter Full Screen"));
            KeyGesture[] gestures = [.. items.Select(i => i.Gesture).OfType<KeyGesture>()];
            Assert.Equal(gestures.Length, gestures.Distinct().Count());
        }

        //The kit's own sheet handler only knows ⌘.; the app command carries Ctrl+period to it on Linux.
        [AvaloniaFact]
        public async Task OnLinux_CtrlPeriod_DismissesTheFrontmostSheet() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var window = new MainWindow();
            window.Show();
            var alert = new AlertSheet("Probe", "message", AlertButtons.YesNo);
            Task<ConfirmChoice> result = SheetHost.For(window).ShowAsync(alert);
            Dispatcher.UIThread.RunJobs();

            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.OemPeriod, KeyModifiers.Control);

            Assert.True(result.IsCompleted);
            Assert.Equal(ConfirmChoice.No, await result);
        }
    }
}
