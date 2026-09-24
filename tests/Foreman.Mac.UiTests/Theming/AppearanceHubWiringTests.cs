using Avalonia;
using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.UiTests.Views;
using Foreman.Mac.Views;
using System;
using System.Threading.Tasks;
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

        private static int SubscriberCount => ((App)Application.Current!).Hub.SubscriberCount;

        [AvaloniaFact]
        public void SetScale_NotifiesTheAppWideHubsSubscribers() {
            var hub = ((App)Application.Current!).Hub;
            var subscriber = new RecordingSubscriber();
            hub.Subscribe(subscriber);

            WindowScaler.SetScale(1.5);

            Assert.True(subscriber.RefreshCount > 0);
        }

        //AddBrowserWindow's real open/shut signal is IsVisible, not Closed - MainWindow hides rather than closes it.
        [AvaloniaFact]
        public void AddBrowserWindow_JoinsTheHubOnShow_AndLeavesOnHide() {
            int before = SubscriberCount;
            var window = new AddBrowserWindow();
            Assert.Equal(before, SubscriberCount);

            window.Show();
            int shown = SubscriberCount;
            Assert.True(shown > before);

            //The kit grid inside keeps its own subscription while the hidden window stays in its tree.
            window.Hide();
            Assert.Equal(shown - 1, SubscriberCount);
        }

        [AvaloniaFact]
        public void SettingsWindow_JoinsTheHubOnConstruction_AndLeavesOnClose() {
            int before = SubscriberCount;
            var window = new SettingsWindow();
            Assert.Equal(before + 1, SubscriberCount);

            window.Close();
            Assert.Equal(before, SubscriberCount);
        }

        [AvaloniaFact]
        public void AboutWindow_JoinsTheHubOnConstruction_AndLeavesOnClose() {
            int before = SubscriberCount;
            var window = new AboutWindow();
            Assert.Equal(before + 1, SubscriberCount);

            window.Close();
            Assert.Equal(before, SubscriberCount);
        }

        [AvaloniaFact]
        public void PresetComparatorWindow_JoinsTheHubOnConstruction_AndLeavesOnClose() {
            int before = SubscriberCount;
            var window = new PresetComparatorWindow();
            Assert.Equal(before + 1, SubscriberCount);

            window.Close();
            Assert.Equal(before, SubscriberCount);
        }

        [AvaloniaFact]
        public async Task DataLoadWindow_JoinsTheHubOnConstruction_AndLeavesOnceLoadFinishesAndCloses() {
            int before = SubscriberCount;
            var window = new DataLoadWindow(new Preset("Nonexistent Preset", true, true));
            Assert.Equal(before + 1, SubscriberCount);

            await window.LoadTask;

            Assert.Equal(before, SubscriberCount);
        }

        [AvaloniaFact]
        public void GalleryWindow_JoinsTheHubOnConstruction_AndLeavesOnClose() {
            int before = SubscriberCount;
            DataCache cache = GalleryWindowTests.NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            Assert.Equal(before + 1, SubscriberCount);

            window.Close();
            Assert.Equal(before, SubscriberCount);
        }
    }
}
