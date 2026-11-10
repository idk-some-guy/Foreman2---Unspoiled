using Avalonia;
using Avalonia.Headless.XUnit;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.UiTests.Views;
using Foreman.Mac.Views;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
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

        [AvaloniaFact]
        public void AddBrowserWindow_JoinsTheHubOnShow_AndLeavesOnHide() {
            int before = SubscriberCount;
            var window = new AddBrowserWindow();
            Assert.Equal(before, SubscriberCount);

            window.Show();
            int shown = SubscriberCount;
            Assert.True(shown > before);

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
            window.Show();

            await window.AttemptTask;
            window.Close();
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
