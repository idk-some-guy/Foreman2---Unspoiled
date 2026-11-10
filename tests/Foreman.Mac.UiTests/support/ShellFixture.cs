using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.Inspector;
using Foreman.Mac.ViewModels;
using Foreman.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.support {
    internal sealed class ShellFixture : IDisposable {
        public required MainWindow Window { get; init; }

        internal AddBrowserViewModel BrowserVm => (AddBrowserViewModel)Window.BrowserWindow!.DataContext!;
        internal ProductionGraph Graph => Window.GraphCanvas.Viewer.Graph;
        internal ManualTaskScheduler SolveWorker => (ManualTaskScheduler)Graph.Solver.Worker;

        public static ShellFixture Open() {
            WindowScaler.SetScale(1.0);
            Task.Run(InspectorTestSupport.GetCacheAsync).GetAwaiter().GetResult();
            MainWindow window = InspectorTestSupport.OpenEmptyWindowAsync().GetAwaiter().GetResult();
            window.Settings = new AppSettings();
            return new ShellFixture { Window = window };
        }

        //A cold cache load awaited on this thread deadlocks on its SynchronizationContext, so it runs through Task.Run.
        public static ShellFixture OpenWithSpaceAge() {
            WindowScaler.SetScale(1.0);
            DataCache cache = Task.Run(InspectorTestSupport.GetSpaceAgeCacheAsync).GetAwaiter().GetResult();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            window.GraphCanvas.Viewer.Context.DCache = cache;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            Dispatcher.UIThread.RunJobs();
            window.Settings = new AppSettings();
            return new ShellFixture { Window = window };
        }

        public static ShellFixture OpenWithVanillaAndOneSupplier(out AddLinkContext link) {
            ShellFixture shell = Open();
            DataCache cache = shell.Window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId nodeId = shell.Window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement element = shell.Window.GraphCanvas.NodeElements.Single(n => n.ViewModel.Id == nodeId);
            element.RequestStateUpdate();
            element.PrePaint();
            link = new AddLinkContext(pair, NewNodeType.Consumer, new FRange(0, 0, true), element, NodeDirection.Up);
            return shell;
        }

        public void Dispose() {
            Window.Close();
            WindowScaler.SetScale(1.0);
        }
    }
}
