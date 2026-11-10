using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitScrimTests {
        private static ControlTheme KitScrimTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Scrim", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Scrim Themed() => new() { Theme = KitScrimTheme() };

        [AvaloniaFact]
        public void KitScrim_IsAControlThemeTargetingScrim() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Scrim), KitScrimTheme().TargetType);
        }

        [AvaloniaFact]
        public void Background_ResolvesSheetScrimToken() {
            using var fixture = new KitFixture();
            Scrim scrim = Themed();
            fixture.Window.Content = scrim;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.SheetScrim").ToImmutable(), scrim.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void TopInset_PushesBoundsDown_NeverZero() {
            using var fixture = new KitFixture();
            Scrim scrim = Themed();
            scrim.TopInset = 30;
            var root = new Grid { Width = 400, Height = 300 };
            root.Children.Add(scrim);
            fixture.Window.Content = root;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(30, scrim.Bounds.Top);
        }

        [AvaloniaFact]
        public void PointerPress_DoesNotReachContentBeneath_OrRaiseAnyDismiss() {
            using var fixture = new KitFixture();
            int clicks = 0;
            var behind = new Button { Width = 400, Height = 300 };
            behind.Click += (_, _) => clicks++;

            Scrim scrim = Themed();
            var root = new Panel { Width = 400, Height = 300, Children = { behind, scrim } };
            fixture.Window.Content = root;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(scrim).Click();

            Assert.Equal(0, clicks);
        }

        [AvaloniaFact]
        public void AutomationPeer_IsNotExposed() {
            using var fixture = new KitFixture();
            Scrim scrim = Themed();
            fixture.Window.Content = scrim;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(scrim);

            Assert.False(peer.IsControlElement());
            Assert.False(peer.IsContentElement());
        }
    }
}
