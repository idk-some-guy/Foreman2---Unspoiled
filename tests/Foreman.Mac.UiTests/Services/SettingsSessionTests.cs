using Avalonia;
using Avalonia.Headless.XUnit;
using Foreman.Mac;
using Foreman.Mac.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Services {
    public class SettingsSessionTests {
        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static SettingsSession NewSession(
            AppSettings settings,
            Func<Task>? resolve = null,
            Func<ThemeMode, Task>? applyTheme = null,
            Action<double>? applyScale = null) {
            var store = new SettingsService(NewTempDir());
            var app = (Application.Current as App)!;
            return new SettingsSession(settings, store, app, resolve ?? (() => Task.CompletedTask),
                applyTheme ?? (_ => Task.CompletedTask), applyScale ?? (_ => { }));
        }

        [AvaloniaFact]
        public async Task Apply_NonSolverProperty_CoalescesOneSaveAfterFlush() {
            var settings = new AppSettings();
            var session = NewSession(settings);

            session.Apply("SmartNodeDirectionChecked");
            Assert.Equal(0, session.SaveCallCount);

            await session.FlushAsync();

            Assert.Equal(1, session.SaveCallCount);
            Assert.Equal(0, session.ResolveCallCount);
        }

        [AvaloniaFact]
        public async Task Apply_TwoRapidChanges_StillOnlyOneSaveAfterFlush() {
            var settings = new AppSettings();
            var session = NewSession(settings);

            session.Apply("SmartNodeDirectionChecked");
            session.Apply("RoundAssemblerCountChecked");
            await session.FlushAsync();

            Assert.Equal(1, session.SaveCallCount);
        }

        [AvaloniaFact]
        public async Task Apply_SolverAffectingProperty_RequestsExactlyOneResolveAfterFlush() {
            var settings = new AppSettings();
            var session = NewSession(settings);

            session.Apply("AssemblerSelectorStyleIndex");
            Assert.Equal(0, session.ResolveCallCount);

            await session.FlushAsync();

            Assert.Equal(1, session.ResolveCallCount);
            Assert.Equal(1, session.SaveCallCount);
        }

        [AvaloniaFact]
        public async Task Apply_EnabledObjectsAndPresetSwitch_AreSolverAffecting() {
            var settings = new AppSettings();
            var session = NewSession(settings);

            session.Apply("EnabledObjects");
            await session.FlushAsync();
            Assert.Equal(1, session.ResolveCallCount);

            session.Apply("PresetSwitch");
            await session.FlushAsync();
            Assert.Equal(2, session.ResolveCallCount);
        }

        [AvaloniaFact]
        public async Task Apply_FlagDarkMode_AppliesThemeImmediately_NotOnFlush() {
            var settings = new AppSettings { FlagDarkMode = ThemeMode.Dark };
            ThemeMode? applied = null;
            var session = NewSession(settings, applyTheme: mode => {
                applied = mode;
                return Task.CompletedTask;
            });

            session.Apply("FlagDarkMode");

            Assert.Equal(ThemeMode.Dark, applied);
            await session.FlushAsync();
        }

        [AvaloniaFact]
        public async Task Apply_UiScaleLabel_AppliesScaleImmediately_NotOnFlush() {
            var settings = new AppSettings { UiScale = 1.25 };
            double? applied = null;
            var session = NewSession(settings, applyScale: scale => applied = scale);

            session.Apply("UiScaleLabel");

            Assert.Equal(1.25, applied);
            await session.FlushAsync();
        }

        [AvaloniaFact]
        public async Task FlushAsync_WithNothingPending_IsANoOp() {
            var settings = new AppSettings();
            var session = NewSession(settings);

            await session.FlushAsync();

            Assert.Equal(0, session.SaveCallCount);
            Assert.Equal(0, session.ResolveCallCount);
        }
    }
}
