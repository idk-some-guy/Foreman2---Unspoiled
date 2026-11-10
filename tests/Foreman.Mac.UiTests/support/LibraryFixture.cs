using Foreman.DataCaching.Storage;
using Foreman.Mac.Services;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace Foreman.Mac.UiTests.support {
    internal sealed class LibraryFixture : IDisposable {
        private readonly string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public LibraryFixture() {
            Directory.CreateDirectory(root);
            Store = new GraphLibraryStore(StoragePaths.DbPath(root));
            Clock = new ManualTimeProvider();
            Worker = new ManualTaskScheduler();
            Service = new GraphLibraryService(Store, Clock) { Worker = Worker };
        }

        private readonly List<string> directories = [];
        private readonly List<MainWindow> windows = [];

        public string Root => root;
        public string DbPath => StoragePaths.DbPath(root);
        public GraphLibraryStore Store { get; }
        public ManualTimeProvider Clock { get; }
        public GraphLibraryService Service { get; }
        public ManualTaskScheduler Worker { get; }

        public string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            directories.Add(dir);
            return dir;
        }

        public MainWindow Track(MainWindow window) {
            windows.Add(window);
            return window;
        }

        public void Dispose() {
            foreach (MainWindow window in windows) {
                if (window.Settings is { } settings)
                    settings.ShowWelcomeAtLaunch = false;
                window.Close();
            }
            WelcomeLauncher.Current?.Close();
            if (ShellHost.Context is { } context && ReferenceEquals(context.Library, Service))
                ShellHost.Context = null;
            Service.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            foreach (string dir in directories)
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
        }
    }
}
