using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Foreman.Mac.Kit.Appearance;
using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Appearance {
    public class AppearanceHubSubscriberTests {
        private sealed class FakeAppearance : IPlatformAppearance {
            private EventHandler? changed;

            public Color? AccentColor { get; set; }
            public ColorContrastPreference ContrastPreference { get; set; } = ColorContrastPreference.NoPreference;
            public bool ReduceTransparency { get; set; }
            public bool ReduceMotion { get; set; }
            public PlatformThemeVariant ThemeVariant { get; set; } = PlatformThemeVariant.Light;

            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        private sealed class RecordingSubscriber : IThemeAware {
            public int RefreshCount { get; private set; }
            public void RefreshTheme() => RefreshCount++;
        }

        [AvaloniaFact]
        public void Subscribe_Refresh_CallsRefreshThemeOnSubscriber() {
            var hub = new AppearanceHub((Application)Application.Current!, new FakeAppearance());
            var subscriber = new RecordingSubscriber();
            hub.Subscribe(subscriber);

            hub.Refresh();

            Assert.Equal(1, subscriber.RefreshCount);
            hub.Dispose();
        }

        [AvaloniaFact]
        public void Unsubscribe_Refresh_NoLongerCallsRefreshThemeOnSubscriber() {
            var hub = new AppearanceHub((Application)Application.Current!, new FakeAppearance());
            var subscriber = new RecordingSubscriber();
            hub.Subscribe(subscriber);
            hub.Refresh();

            hub.Unsubscribe(subscriber);
            hub.Refresh();

            Assert.Equal(1, subscriber.RefreshCount);
            hub.Dispose();
        }

        //A subscriber dropped by its owner (no other references) must not keep the hub's own weak-reference
        //list alive - proven by forcing a collection and confirming the next Refresh doesn't throw and no
        //longer reaches a subscriber built and discarded in the helper below.
        [AvaloniaFact]
        public void Subscribe_SubscriberCollected_PrunedWithoutError() {
            var hub = new AppearanceHub((Application)Application.Current!, new FakeAppearance());
            var probe = new RecordingSubscriber();
            SubscribeDisposableSubscriber(hub);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            hub.Refresh();

            hub.Subscribe(probe);
            hub.Refresh();
            Assert.Equal(1, probe.RefreshCount);
            hub.Dispose();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribeDisposableSubscriber(AppearanceHub hub) => hub.Subscribe(new RecordingSubscriber());
    }
}
