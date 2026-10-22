using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static Foreman.Mac.UiTests.support.LinkFixture;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public class LinkRoutingTests : IDisposable {
        private static readonly Color Orange = Color.FromArgb(255, 210, 90, 30);

        public LinkRoutingTests() => WindowScaler.SetScale(1.0);

        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        //A vertical link: the cubic's control points collapse onto the centreline, so distances to it are exact.
        private static LinkElement Straight(LinkFixture fx, ItemPrototype item, LinkRouting? routing = null) =>
            fx.AddLink(item, new Point(0, 150), NodeDirection.Up, new Point(0, -150), NodeDirection.Up, routing);

        [AvaloniaFact]
        public void HitTest_3ptLink_5Point9Hits_6Point1Misses_Width20_11Point9Hits() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = Straight(fx, fx.NewItem("iron-plate", Orange));

            Assert.Equal(3f, link.LinkWidth);
            Assert.True(link.HitTest(new PointF(5.9f, 0)));
            Assert.False(link.HitTest(new PointF(6.1f, 0)));

            link.LinkWidth = 20f;
            Assert.True(link.HitTest(new PointF(11.9f, 0)));
            Assert.False(link.HitTest(new PointF(12.1f, 0)));
        }

        [AvaloniaFact]
        public void SelectedCasing_NodeSelected_StrokePlus4_Undersupplied_ErrorDash6And4() {
            using LinkFixture fx = LinkFixture.Create();
            ItemPrototype item = fx.NewItem("iron-plate", Orange);
            LinkElement link = Straight(fx, item);
            CanvasTokens tokens = TokenBridge.Current;

            link.IsSelected = true;
            using (SKPaint casing = link.CasingPaint) {
                Assert.Equal(tokens.NodeSelected, casing.Color);
                Assert.Equal(link.LinkWidth + 4f, casing.StrokeWidth);
            }
            using (SKSurface surface = LinkFixture.RenderLink(link)) {
                Assert.Equal(tokens.NodeSelected, Sample(surface, 2, 0));
                Assert.Equal(new SKColor(210, 90, 30), Sample(surface, 0, 0));
            }

            using (SKPaint normal = link.StrokePaint)
                Assert.Null(normal.PathEffect);

            var consumerController = fx.Viewer.Session.Editor.RequestNodeController(link.ConsumerElement!.ViewModel.Id)!;
            consumerController.SetRateType(RateType.Manual);
            consumerController.SetDesiredSetValue(50);
            link.IsSelected = false;
            using (SKPaint short_ = link.StrokePaint) {
                Assert.Equal(tokens.Error, short_.Color);
                Assert.NotNull(short_.PathEffect);
            }
            using SKSurface dashed = LinkFixture.RenderLink(link);
            var on = new List<bool>();
            for (int y = -60; y <= 60; y++)
                on.Add(Sample(dashed, 0, y) == tokens.Error);
            Assert.Contains(true, on);
            Assert.Contains(false, on);
            for (int i = 0; i + 10 < on.Count; i++)
                Assert.Equal(on[i], on[i + 10]);
        }

        [AvaloniaFact]
        public void Hover_HaloIsStrokePlus4_At30Percent() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = Straight(fx, fx.NewItem("iron-plate", Orange));

            link.IsHovered = true;
            using SKPaint halo = link.HaloPaint;
            Assert.Equal(link.LinkWidth + 4f, halo.StrokeWidth);
            Assert.Equal(new SKColor(210, 90, 30, 77), halo.Color);

            using SKSurface surface = LinkFixture.RenderLink(link);
            Assert.InRange(Sample(surface, 2, 0).Red, 240, 243);
            Assert.Equal(new SKColor(210, 90, 30), Sample(surface, 0, 0));
        }

        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void Orthogonal_EverySegmentAxisParallel_FirstIsPerpendicular12TimesStep_CornersRadius4(double step) {
            WindowScaler.SetScale(step);
            using LinkFixture fx = LinkFixture.Create(atDefaultScale: false);
            LinkElement link = fx.AddLink(fx.NewItem("iron-plate", Orange), new Point(0, 300), NodeDirection.Up, new Point(260, -300), NodeDirection.Up, LinkRouting.Orthogonal);

            IReadOnlyList<RouteSegment> segments = link.PathSegments();
            RouteSegment[] runs = [.. segments.Where(s => s.ArcRadius == 0)];
            RouteSegment[] arcs = [.. segments.Where(s => s.ArcRadius > 0)];

            Assert.All(runs, s => Assert.True(Math.Abs(s.To.X - s.From.X) < 0.01f || Math.Abs(s.To.Y - s.From.Y) < 0.01f));
            Assert.Equal(0f, segments[0].To.X - segments[0].From.X, 0.01f);
            Assert.Equal(-12f * (float)step, segments[0].To.Y - segments[0].From.Y, 0.01f);
            Assert.Equal(2, arcs.Length);
            Assert.All(arcs, s => Assert.Equal(Math.Max(4f, link.LinkWidth), s.ArcRadius, 0.01f));
        }

        [AvaloniaFact]
        public void Orthogonal_WithWaypoints_TurnsAtEachWaypoint() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = fx.AddLink(fx.NewItem("iron-plate", Orange), new Point(0, 300), NodeDirection.Up, new Point(260, -300), NodeDirection.Up, LinkRouting.Orthogonal);
            Point[] waypoints = [new Point(100, 100), new Point(-80, -60)];
            fx.Viewer.SetWaypoints(link, waypoints);

            IReadOnlyList<RouteSegment> segments = link.PathSegments();

            Assert.All(segments.Where(s => s.ArcRadius == 0), s => Assert.True(Math.Abs(s.To.X - s.From.X) < 0.01f || Math.Abs(s.To.Y - s.From.Y) < 0.01f));
            float roundedCornerReach = 4f * (MathF.Sqrt(2) - 1f) + 0.5f;
            foreach (Point waypoint in waypoints)
                Assert.True(LinkRouter.DistanceToPolyline(link.Polyline, new SKPoint(waypoint.X, waypoint.Y)) < roundedCornerReach, waypoint.ToString());
        }

        [AvaloniaFact]
        public void Rounded_WithWaypoints_PassesThroughEachInOrder_AndLeavesPerpendicular() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = fx.AddLink(fx.NewItem("iron-plate", Orange), new Point(0, 300), NodeDirection.Up, new Point(260, -300), NodeDirection.Up);
            Point[] waypoints = [new Point(-120, 100), new Point(200, -50)];
            fx.Viewer.SetWaypoints(link, waypoints);

            IReadOnlyList<SKPoint> line = link.Polyline;

            foreach (Point waypoint in waypoints)
                Assert.True(LinkRouter.DistanceToPolyline(line, new SKPoint(waypoint.X, waypoint.Y)) < 0.5f);
            Assert.True(LinkRouter.AlongLength(line, new SKPoint(waypoints[0].X, waypoints[0].Y)) < LinkRouter.AlongLength(line, new SKPoint(waypoints[1].X, waypoints[1].Y)));
            Assert.True(Math.Abs(line[1].X - line[0].X) < 0.15f * Math.Abs(line[1].Y - line[0].Y));
            Assert.True(Math.Abs(line[^1].X - line[^2].X) < 0.15f * Math.Abs(line[^1].Y - line[^2].Y));
        }

        [AvaloniaTheory]
        [InlineData(1.5)]
        public void DynamicLineWidthFalse_EveryLinkIsLinkWidthTimesStep(double step) {
            WindowScaler.SetScale(step);
            using LinkFixture fx = LinkFixture.Create(atDefaultScale: false);
            fx.Viewer.Context.DynamicLinkWidth = false;
            ItemPrototype item = fx.NewItem("iron-plate", Orange);
            fx.AddLink(item, new Point(0, 300), NodeDirection.Up, new Point(0, -300), NodeDirection.Up);
            fx.AddLink(item, new Point(400, 300), NodeDirection.Up, new Point(400, -300), NodeDirection.Up);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(2 * LinkFixture.Half, 2 * LinkFixture.Half));
            fx.Viewer.Paint(surface.Canvas);

            Assert.Equal(2, fx.Viewer.LinkElements.Count);
            Assert.All(fx.Viewer.LinkElements, link => Assert.Equal(3f * (float)step, link.LinkWidth, 0.001f));
        }

        [AvaloniaFact]
        public void LinkPeer_NameReadsItemSourceTargetRateRouting_AndWaypointCount() {
            using LinkFixture fx = LinkFixture.Create();
            ItemPrototype item = fx.NewItem("iron-plate", Orange, friendlyName: "Iron plate");
            BaseNodeElement supplier = fx.Supplier(item, new Point(0, 300));
            BaseNodeElement consumer = fx.Consumer(item, new Point(0, -300));
            LinkElement link = fx.Link(supplier, consumer, item, LinkRouting.Orthogonal);
            fx.Viewer.Session.TryGetDomainNode(supplier.ViewModel.Id, out BaseNode? domain);
            domain!.OutputLinks.Single().ThroughputPerSec = 90;

            Assert.Equal("Iron plate link, Iron plate to Iron plate, 90 per second, Orthogonal", link.AutomationName);

            fx.Viewer.SetWaypoints(link, [new Point(10, 0), new Point(20, 0)]);
            Assert.EndsWith(", Orthogonal, 2 waypoints", link.AutomationName);

            var controller = fx.Viewer.Session.Editor.RequestNodeController(consumer.ViewModel.Id)!;
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(500);
            Assert.EndsWith(", 2 waypoints, short of supply", link.AutomationName);
            Assert.Equal("Actions: Delete, Rounded, Orthogonal, Clear Waypoints", LinkElement.AutomationHelpText);
        }

        [AvaloniaFact]
        public void NewLink_TakesTheSettingsDefaultRouting_AndChangingTheSettingLeavesExistingLinksAlone() {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            var window = new MainWindow();
            try {
                window.Show();
                using LinkFixture fx = LinkFixture.Over(window.GraphCanvas);
                window.DataCache = fx.Cache;
                window.Settings = new AppSettings { DefaultLinkRouting = LinkRouting.Rounded };
                window.PresetResolver = new PresetResolver(directory);
                window.SettingsService = new SettingsService(directory);
                window.ApplyLoadedSettings();
                SettingsWindow opened = null!;
                window.SettingsDialogStub = w => opened = w;
                window.ShowSettings();
                ItemPrototype item = fx.NewItem("iron-plate", Orange);

                opened.ViewModel.LinkRoutingIndex = 1;
                LinkElement first = Straight(fx, item);
                opened.ViewModel.LinkRoutingIndex = 0;
                LinkElement second = fx.AddLink(item, new Point(400, 150), NodeDirection.Up, new Point(400, -150), NodeDirection.Up);

                Assert.Equal(LinkRouting.Orthogonal, first.ViewModel.Routing);
                Assert.Equal(LinkRouting.Rounded, second.ViewModel.Routing);
            } finally {
                window.Close();
                Directory.Delete(directory, recursive: true);
            }
        }

        [AvaloniaFact]
        public void PreRoutingFile_LinksTakeTheCurrentDefault_WithZeroWaypoints() {
            using LinkFixture fx = LinkFixture.Create();
            ItemPrototype item = fx.NewItem("iron-plate", Orange);
            LinkElement link = Straight(fx, item, LinkRouting.Rounded);
            fx.Viewer.SetWaypoints(link, [new Point(5, 5)]);
            JsonNode saved = JsonNode.Parse(GraphSaveCodec.WriteProductionGraphToString(fx.Viewer.Graph, writeIndented: false))!;
            foreach (JsonNode? wire in saved["NodeLinks"]!.AsArray()) {
                wire!.AsObject().Remove("Routing");
                wire.AsObject().Remove("Waypoints");
            }
            saved["Version"] = 8;
            ProductionGraphSaveDocument preRouting = GraphSaveCodec.ReadProductionGraph(saved.ToJsonString())!;
            fx.Viewer.DefaultLinkRouting = LinkRouting.Orthogonal;

            fx.Viewer.LoadDocument(fx.Cache, new GraphViewerSaveDocument { Version = 8, ProductionGraph = preRouting });

            Assert.Single(fx.Viewer.LinkElements);
            Assert.All(fx.Viewer.LinkElements, l => {
                Assert.Equal(LinkRouting.Orthogonal, l.ViewModel.Routing);
                Assert.Empty(l.ViewModel.Waypoints);
            });

            fx.Viewer.DefaultLinkRouting = LinkRouting.Rounded;
            Assert.True(fx.Viewer.ImportNodesFromDocument(fx.Cache, preRouting, new Point(2000, 0), applySolverSettings: false) > 0);
            Assert.All(fx.Viewer.LinkElements.Skip(1), l => Assert.Equal(LinkRouting.Rounded, l.ViewModel.Routing));

            LinkElement source = fx.AddLink(item, new Point(800, 150), NodeDirection.Up, new Point(800, -150), NodeDirection.Up);
            fx.Viewer.SetWaypoints(source, [new Point(10, 20)]);
            fx.Viewer.SetSelection([(BaseNodeElement)source.SupplierElement!, (BaseNodeElement)source.ConsumerElement!]);
            NodeClipboard.Paste(fx.Viewer, fx.Cache, NodeClipboard.Copy(fx.Viewer), new Point(-900, 600));

            LinkElement pasted = fx.Viewer.LinkElements.Single(l => !ReferenceEquals(l, source) && l.ViewModel.Waypoints.Count > 0);
            Size shift = (Size)Point.Subtract(pasted.SupplierElement!.ViewModel.Location, (Size)source.SupplierElement!.ViewModel.Location);
            Assert.NotEqual(Size.Empty, shift);
            Assert.Equal(Point.Add(new Point(10, 20), shift), Assert.Single(pasted.ViewModel.Waypoints));
            Assert.Equal(new Point(10, 20), Assert.Single(source.ViewModel.Waypoints));
        }
    }
}
