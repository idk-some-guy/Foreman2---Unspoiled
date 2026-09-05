using Foreman.Mac;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.Inspector;
using System;

namespace Foreman.Mac.UiTests.support {
    //Wraps InspectorTestSupport's MainWindow fixture, synchronously, with Settings assigned.
    internal sealed class ShellFixture : IDisposable {
        public required MainWindow Window { get; init; }

        public static ShellFixture Open() {
            MainWindow window = InspectorTestSupport.OpenEmptyWindowAsync().GetAwaiter().GetResult();
            window.Settings = new AppSettings();
            return new ShellFixture { Window = window };
        }

        public void Dispose() {
            Window.Close();
            WindowScaler.SetScale(1.0);
        }
    }
}
