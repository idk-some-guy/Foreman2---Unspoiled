using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class ValueTextTests {
        [AvaloniaFact]
        public void AutomationPeer_IsNamedByItsLabel_AndExposesItsTextAsTheValue() {
            using var fixture = new KitFixture();
            var text = new ValueText { Text = "Not chosen" };
            AutomationProperties.SetName(text, "Save to");
            fixture.Window.Content = text;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(text);
            Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
            Assert.Equal("Save to", peer.GetName());
            IValueProvider value = peer.GetProvider<IValueProvider>()!;
            Assert.True(value.IsReadOnly);
            Assert.Equal("Not chosen", value.Value);

            text.Text = "~/a";
            Assert.Equal("~/a", value.Value);
        }

        [AvaloniaFact]
        public void TakesTheKitTextTheme() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Text", null, out object? theme));
            var text = new ValueText { Theme = (Avalonia.Styling.ControlTheme)theme!, Text = "2140 × 1360 px" };
            text.Classes.Add("callout");
            fixture.Window.Content = text;
            Dispatcher.UIThread.RunJobs();

            Assert.True(text.Bounds.Width > 0);
        }
    }
}
