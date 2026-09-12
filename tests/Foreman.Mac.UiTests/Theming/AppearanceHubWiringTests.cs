using Avalonia;
using Avalonia.Headless.XUnit;
using Foreman.Mac;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using System;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    //WindowScaler carries no DynamicResource of its own (it wraps each window's Content in a
    //LayoutTransformControl directly), so a scale change needs to reach the hub the same way a
    //theme/accent/contrast flip does, and subscribers with baked, size-dependent visuals refresh off
    //the one Changed/Subscribe path instead of a second scale-specific hook.
    [Collection(ScaleTestGroup.Name)]
    public class AppearanceHubWiringTests : IDisposable {
        private sealed class RecordingSubscriber : IThemeAware {
            public int RefreshCount { get; private set; }
            public void RefreshTheme() => RefreshCount++;
        }

        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public void SetScale_NotifiesTheAppWideHubsSubscribers() {
            var hub = ((App)Application.Current!).Hub;
            var subscriber = new RecordingSubscriber();
            hub.Subscribe(subscriber);

            WindowScaler.SetScale(1.5);

            Assert.True(subscriber.RefreshCount > 0);
        }
    }
}
