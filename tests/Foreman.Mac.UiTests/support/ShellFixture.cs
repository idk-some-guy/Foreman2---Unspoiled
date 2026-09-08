using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac;
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
    //Wraps InspectorTestSupport's MainWindow fixture, synchronously, with Settings assigned.
    internal sealed class ShellFixture : IDisposable {
        public required MainWindow Window { get; init; }

        internal AddBrowserViewModel BrowserVm => (AddBrowserViewModel)Window.BrowserWindow!.DataContext!;
        internal ProductionGraph Graph => Window.GraphCanvas.Viewer.Graph;

        public static ShellFixture Open() {
            //Reset first, not just in Dispose - WindowScaler is process-wide, so a scale left non-default
            //by a test that ran earlier (and never got as far as its own Dispose) would otherwise bake into
            //this window's LayoutTransformControl at Track time.
            WindowScaler.SetScale(1.0);
            //Pre-warms the shared vanilla cache off-thread first (see OpenWithSpaceAge's own comment) so
            //OpenEmptyWindowAsync's internal GetCacheAsync() always hits its already-loaded fast path -
            //a cold GetAwaiter().GetResult() here would otherwise risk the same deadlock.
            Task.Run(InspectorTestSupport.GetCacheAsync).GetAwaiter().GetResult();
            MainWindow window = InspectorTestSupport.OpenEmptyWindowAsync().GetAwaiter().GetResult();
            window.Settings = new AppSettings();
            return new ShellFixture { Window = window };
        }

        //Space Age ships spoilage/quality data vanilla doesn't (RecipeSegmentQualityTests uses the same
        //shared cache) - the browser placement suite's Spoil/Plant/quality coverage needs it. The cache
        //fetch runs via Task.Run rather than a plain GetAwaiter().GetResult(): on a cold cache, an await
        //inside the load chain resumes on this thread's own SynchronizationContext, which never gets
        //pumped because this same thread is blocked waiting on it - Task.Run gives the load a thread with
        //no ambient context to deadlock on.
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

        //A supplier node for "iron-plate", plus the AddLinkContext a Consumer-direction drag off its
        //output tab would produce - CatalogFixture.LinkContextFor's shape, against a full MainWindow shell
        //so the browser under test can actually open.
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
