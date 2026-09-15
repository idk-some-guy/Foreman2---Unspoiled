using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitAlertFrameTests {
        private static ControlTheme KitAlertFrameTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.AlertFrame", null, out object? theme));
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

        private static RenderTargetBitmap OnePixelIcon() => new(new PixelSize(1, 1));

        private static AlertFrame Themed(string headline = "Discard changes to this graph?",
            string? informative = "You have unsaved edits made since the last save. Discarding cannot be undone.",
            ButtonRow? buttons = null) =>
            new() {
                Theme = KitAlertFrameTheme(),
                Icon = OnePixelIcon(),
                Headline = headline,
                InformativeText = informative,
                Buttons = buttons ?? TwoButtonDestructiveRow(),
            };

        private static ButtonRow TwoButtonDestructiveRow() {
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(Btn("Discard", ButtonRowRole.Destructive));
            row.Items.Add(Btn("Cancel", ButtonRowRole.Cancel));
            return row;
        }

        private static ButtonRow ThreeButtonDestructiveRow() {
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(Btn("Discard", ButtonRowRole.Destructive));
            row.Items.Add(Btn("Keep editing", ButtonRowRole.Other));
            row.Items.Add(Btn("Cancel", ButtonRowRole.Cancel));
            return row;
        }

        private static Window Shown(AlertFrame alert, double width = 600, double height = 400) {
            var window = new Window { Width = width, Height = height, Content = alert };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static Border SheetPanel(AlertFrame alert) =>
            alert.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

        [AvaloniaFact]
        public void KitAlertFrame_IsAControlThemeTargetingAlertFrame() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(AlertFrame), KitAlertFrameTheme().TargetType);
        }

        [AvaloniaFact]
        public void Width_Is300_TheAlertWidthClass() {
            using var fixture = new KitFixture();
            AlertFrame alert = Themed();
            Shown(alert);

            Assert.Equal(300, SheetPanel(alert).Width);
        }

        [AvaloniaFact]
        public void Icon_Is32PointBitmap() {
            using var fixture = new KitFixture();
            AlertFrame alert = Themed();
            Shown(alert);

            Image icon = alert.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Icon");
            Assert.Equal(32, icon.Width);
            Assert.Equal(32, icon.Height);
            //Proves the TemplateBinding actually carries the assigned Icon through, not just its type.
            Assert.Same(alert.Icon, icon.Source);
        }

        [AvaloniaFact]
        public void Headline_IsEmphasizedBody_Informative_IsCalloutSecondaryAt1_4LineHeight() {
            using var fixture = new KitFixture();
            AlertFrame alert = Themed();
            Shown(alert);

            TextBlock headline = alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Headline");
            TextBlock informative = alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Informative");

            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontSize", Application.Current.ActualThemeVariant, out object? headSize);
            Assert.Equal((double)headSize!, headline.FontSize);

            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? calloutSize);
            Assert.Equal((double)calloutSize!, informative.FontSize);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), informative.Foreground?.ToImmutable());
            Assert.Equal(16.8, informative.LineHeight);
        }

        [AvaloniaFact]
        public void NoInformativeText_StillLaysOutWithoutCollapsingTheIconColumn() {
            using var fixture = new KitFixture();
            AlertFrame alert = Themed(informative: null);
            Shown(alert);

            Image icon = alert.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Icon");
            TextBlock informative = alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Informative");

            Assert.Equal(32, icon.Bounds.Width);
            Assert.False(informative.IsVisible);
        }

        [AvaloniaFact]
        public void Escape_ResolvesToCancel_EvenWithAThirdButtonPresent() {
            using var fixture = new KitFixture();
            ButtonRow row = ThreeButtonDestructiveRow();
            AlertFrame alert = Themed(buttons: row);
            alert.IsOpen = true;
            Shown(alert);

            int cancelled = 0;
            alert.Cancelled += (_, _) => cancelled++;

            Button anyRowButton = row.Items.OfType<Button>().First();
            fixture.StateOf(anyRowButton).KeyDown(Key.Escape);

            Assert.Equal(1, cancelled);
        }

        [AvaloniaFact]
        public void DestructiveButton_IsNotDefault_ReturnResolvesToCancel() {
            using var fixture = new KitFixture();
            ButtonRow row = TwoButtonDestructiveRow();
            AlertFrame alert = Themed(buttons: row);
            alert.IsOpen = true;
            Shown(alert);

            Button discard = row.Items.OfType<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Destructive);
            Button cancel = row.Items.OfType<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Cancel);
            int discardHits = 0, cancelHits = 0;
            discard.Click += (_, _) => discardHits++;
            cancel.Click += (_, _) => cancelHits++;

            fixture.StateOf(cancel).KeyDown(Key.Enter);

            Assert.Equal(0, discardHits);
            Assert.Equal(1, cancelHits);
        }

        [AvaloniaFact]
        public void AutomationPeer_NamedFromHeadline() {
            using var fixture = new KitFixture();
            AlertFrame alert = Themed(headline: "Discard changes to this graph?");
            Shown(alert);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(alert);

            Assert.Equal("Discard changes to this graph?", peer.GetName());
        }

        [AvaloniaFact]
        public void Icon_AccessibilityViewIsRaw() {
            using var fixture = new KitFixture();
            AlertFrame alert = Themed();
            Shown(alert);

            Image icon = alert.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Icon");

            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(icon));
        }
    }
}
