using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitButtonRowTests {
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

        private static ButtonRow Row(double width, params Button[] buttons) {
            var row = new ButtonRow { Theme = KitButtonRowTheme(), Width = width };
            foreach (Button b in buttons)
                row.Items.Add(b);
            return row;
        }

        private static ButtonRow Layout(double width, params Button[] buttons) {
            ButtonRow row = Row(width, buttons);
            var window = new Window { Width = width + 40, Height = 100, Content = row };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return row;
        }

        [AvaloniaFact]
        public void KitButtonRow_IsAControlThemeTargetingButtonRow() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ButtonRow), KitButtonRowTheme().TargetType);
        }

        [AvaloniaFact]
        public void ThreeActions_OrderLeftToRight_IsOtherCancelDefault() {
            using var fixture = new KitFixture();
            Button save = Btn("Save", ButtonRowRole.Default);
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Button dontSave = Btn("Don't Save", ButtonRowRole.Other);
            //Added out of visual order - the row, not insertion order, decides layout.
            Layout(400, save, cancel, dontSave);
            Dispatcher.UIThread.RunJobs();

            Assert.True(dontSave.Bounds.X < cancel.Bounds.X);
            Assert.True(cancel.Bounds.X < save.Bounds.X);
        }

        [AvaloniaFact]
        public void LeadingHelper_IsLeftmost_SeparatedFromClusterByAFlexibleGap() {
            using var fixture = new KitFixture();
            Button helper = Btn("Reset to defaults", ButtonRowRole.Helper);
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Button apply = Btn("Apply", ButtonRowRole.Default);
            Layout(500, apply, cancel, helper);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, helper.Bounds.X);
            Assert.True(cancel.Bounds.X < apply.Bounds.X);
            double gap = cancel.Bounds.X - helper.Bounds.Right;
            Assert.True(gap > 8, $"expected a flexible gap wider than Space2, got {gap}");
        }

        [AvaloniaFact]
        public void DestructiveButton_IsNeverDefault_CancelBecomesDefault_AndReturnResolvesToCancel() {
            using var fixture = new KitFixture();
            Button discard = Btn("Discard", ButtonRowRole.Destructive);
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            ButtonRow row = Layout(300, discard, cancel);

            Assert.DoesNotContain("primary", discard.Classes);
            Assert.Contains("primary", cancel.Classes);
            Assert.True(discard.Bounds.X < cancel.Bounds.X);

            //The single most important assertion in this suite: Return fires Cancel, not the destructive button.
            int cancelHits = 0, discardHits = 0;
            cancel.Click += (_, _) => cancelHits++;
            discard.Click += (_, _) => discardHits++;

            fixture.StateOf(cancel).KeyDown(Key.Enter);

            Assert.Equal(1, cancelHits);
            Assert.Equal(0, discardHits);
        }

        [AvaloniaFact]
        public void Escape_AndCmdPeriod_BothResolveToCancel() {
            using var fixture = new KitFixture();
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Button save = Btn("Save", ButtonRowRole.Default);
            Layout(300, save, cancel);

            int hits = 0;
            cancel.Click += (_, _) => hits++;

            fixture.StateOf(save).KeyDown(Key.Escape);
            Assert.Equal(1, hits);

            fixture.StateOf(save).KeyDown(Key.OemPeriod, KeyModifiers.Meta);
            Assert.Equal(2, hits);
        }

        [AvaloniaFact]
        public void CmdBackspace_ResolvesToTheDestructiveAction() {
            using var fixture = new KitFixture();
            Button discard = Btn("Discard", ButtonRowRole.Destructive);
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Layout(300, discard, cancel);

            int hits = 0;
            discard.Click += (_, _) => hits++;

            fixture.StateOf(cancel).KeyDown(Key.Back, KeyModifiers.Meta);

            Assert.Equal(1, hits);
        }

        [AvaloniaFact]
        public void OneButtonRow_ReturnAndEscapeBothFireIt() {
            using var fixture = new KitFixture();
            Button done = Btn("Done", ButtonRowRole.Default);
            Layout(200, done);

            int hits = 0;
            done.Click += (_, _) => hits++;

            fixture.StateOf(done).KeyDown(Key.Enter);
            Assert.Equal(1, hits);

            fixture.StateOf(done).KeyDown(Key.Escape);
            Assert.Equal(2, hits);
        }

        [AvaloniaFact]
        public void NoChildStretchesHorizontally_AndEveryButtonHasTheMinWidth() {
            using var fixture = new KitFixture();
            Button ok = Btn("OK", ButtonRowRole.Default);
            Button cancel = Btn("Cancel", ButtonRowRole.Cancel);
            Layout(300, ok, cancel);

            foreach (Button b in new[] { ok, cancel }) {
                Assert.NotEqual(HorizontalAlignment.Stretch, b.HorizontalAlignment);
                Assert.True(b.Bounds.Width >= 64, $"expected min width 64, got {b.Bounds.Width}");
            }
        }

        [AvaloniaFact]
        public void NarrowerThanTheCluster_WrapsToASecondTrailingLine_RatherThanShrinking() {
            using var fixture = new KitFixture();
            //One-character labels so every button clamps to the 64pt min width exactly.
            Button other = Btn("A", ButtonRowRole.Other);
            Button cancel = Btn("B", ButtonRowRole.Cancel);
            Button save = Btn("C", ButtonRowRole.Default);
            //3 buttons at min width 64 plus 2 gaps of 8 need 208 - 150 forces a wrap.
            Layout(150, other, cancel, save);
            Dispatcher.UIThread.RunJobs();

            Button[] all = [other, cancel, save];
            double[] rows = [.. all.Select(b => b.Bounds.Y).Distinct()];
            Assert.Equal(2, rows.Length);
            //The trailing-most (default) button stays on the bottom-most line.
            Assert.True(other.Bounds.Y < save.Bounds.Y, "the leading button should wrap up, not the trailing one");
            Assert.Equal(cancel.Bounds.Y, save.Bounds.Y);

            foreach (Button b in all) {
                Assert.True(b.Bounds.Width >= b.DesiredSize.Width - 0.5, $"{b.Content}: shrank below its desired width");
                Assert.Equal(22, b.Bounds.Height);
            }

            double gap = save.Bounds.X - cancel.Bounds.Right;
            Assert.Equal(8, gap);
        }

        [AvaloniaFact]
        public void Centered_ChangesOnlyAlignment_NotOrder() {
            using var fixture = new KitFixture();
            Button ok = Btn("OK", ButtonRowRole.Default);
            ButtonRow row = Row(300, ok);
            row.IsCentered = true;
            var window = new Window { Width = 340, Height = 100, Content = row };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            //Centered ignores the trailing/leading split entirely - a lone button sits in the middle.
            double expectedX = (row.Bounds.Width - ok.Bounds.Width) / 2;
            Assert.True(System.Math.Abs(ok.Bounds.X - expectedX) < 1, $"expected ~{expectedX}, got {ok.Bounds.X}");
        }
    }
}
