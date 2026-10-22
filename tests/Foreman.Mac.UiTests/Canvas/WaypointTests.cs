using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using static Foreman.Mac.UiTests.support.LinkFixture;

namespace Foreman.Mac.UiTests.Canvas {
    public class WaypointTests {
        private static readonly Color Orange = Color.FromArgb(255, 210, 90, 30);

        private static LinkElement Straight(LinkFixture fx, LinkRouting? routing = null) =>
            fx.AddLink(fx.NewItem("iron-plate", Orange), new Point(0, 300), NodeDirection.Up, new Point(0, -300), NodeDirection.Up, routing);

        [AvaloniaFact]
        public void Waypoint_Renders10_Hits20_HiddenUnlessHoveredSelectedOrFocused() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = Straight(fx);
            fx.Viewer.SetWaypoints(link, [new Point(0, 0)]);
            WaypointElement waypoint = link.WaypointElements.Single();
            CanvasTokens tokens = TokenBridge.Current;

            Assert.Equal(new Size(10, 10), waypoint.RenderBounds.Size);
            Assert.Equal(new Size(20, 20), waypoint.HitBounds.Size);

            using (SKSurface hidden = LinkFixture.RenderLink(link))
                Assert.Equal(new SKColor(210, 90, 30), Sample(hidden, 0, 0));
            Assert.False(waypoint.ContainsPoint(new Point(9, 0)));

            foreach (Action<LinkElement> show in (Action<LinkElement>[])[l => l.IsHovered = true, l => l.IsSelected = true, l => l.IsFocused = true]) {
                link.IsHovered = link.IsSelected = link.IsFocused = false;
                show(link);
                using SKSurface shown = LinkFixture.RenderLink(link);
                Assert.Equal(tokens.WindowBackground, Sample(shown, 0, 0));
                Assert.Equal(tokens.Accent, Sample(shown, 3, -3));
                Assert.True(waypoint.ContainsPoint(new Point(9, 0)));
                Assert.False(waypoint.ContainsPoint(new Point(10, 0)));
            }

            link.SelectedWaypointIndex = 0;
            using SKSurface selected = LinkFixture.RenderLink(link);
            Assert.Equal(tokens.Accent, Sample(selected, 0, 0));
        }

        [AvaloniaFact]
        public void DoubleClickOnLink_AddsOneWaypointWithinHalfAPointOfThePath() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = fx.AddLink(fx.NewItem("iron-plate", Orange), new Point(0, 300), NodeDirection.Up, new Point(260, -300), NodeDirection.Up);
            Assert.Empty(link.ViewModel.Waypoints);
            IReadOnlyList<SKPoint> line = link.Polyline;
            SKPoint onPath = line[line.Count / 3];

            fx.DoubleClick(fx.ToScreen(new Point((int)Math.Round(onPath.X), (int)Math.Round(onPath.Y))));

            Point added = Assert.Single(link.ViewModel.Waypoints);
            Assert.True(LinkRouter.DistanceToPolyline(link.Polyline, new SKPoint(added.X, added.Y)) <= 0.5f);
        }

        [AvaloniaFact]
        public void DoubleClickOnWaypoint_RemovesIt_AndDeleteRemovesTheSelectedOne() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = Straight(fx);
            fx.Viewer.SetWaypoints(link, [new Point(0, 100), new Point(0, -100)]);
            fx.ClickAt(fx.ToScreen(new Point(0, 0)));

            fx.DoubleClick(fx.ToScreen(new Point(0, 100)));
            Assert.Equal<Point>([new Point(0, -100)], link.ViewModel.Waypoints);

            fx.ClickAt(fx.ToScreen(new Point(0, -100)));
            Assert.Equal(0, link.SelectedWaypointIndex);
            fx.Control.DeleteSelection();
            Assert.Empty(link.ViewModel.Waypoints);
            Assert.Contains(link, fx.Viewer.LinkElements);

            fx.Control.DeleteSelection();
            Assert.Empty(fx.Viewer.LinkElements);
            Assert.Empty(fx.Viewer.SelectedLinks);
            Assert.Equal(2, fx.Viewer.NodeElements.Count);
        }

        [AvaloniaFact]
        public void WaypointDrag_SnapsToMinorGrid_ShiftLocksTheAxis() {
            using LinkFixture fx = LinkFixture.Create();
            fx.Control.Grid.ShowGrid = true;
            fx.Control.Grid.CurrentGridUnit = 20;
            LinkElement link = Straight(fx);
            fx.Viewer.SetWaypoints(link, [new Point(0, 0)]);
            fx.ClickAt(fx.ToScreen(new Point(0, 40)));

            int mutations = 0;
            fx.Viewer.Session.GraphMutated += (_, _) => mutations++;

            AvaloniaPoint start = fx.ToScreen(new Point(0, 0));
            fx.Window!.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            fx.Window!.MouseMove(start + new AvaloniaPoint(20, 0), RawInputModifiers.LeftMouseButton);
            fx.Window!.MouseMove(start + new AvaloniaPoint(30, 40), RawInputModifiers.LeftMouseButton);
            fx.Window!.MouseMove(start + new AvaloniaPoint(37, 61), RawInputModifiers.LeftMouseButton);
            Assert.Equal(0, mutations);
            Assert.Equal(new Point(40, 60), link.EffectiveWaypoints[0]);
            fx.Window!.MouseUp(start + new AvaloniaPoint(37, 61), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, mutations);
            Assert.Equal(new Point(40, 60), link.ViewModel.Waypoints[0]);

            AvaloniaPoint again = fx.ToScreen(new Point(40, 60));
            fx.Window!.MouseDown(again, MouseButton.Left, RawInputModifiers.None);
            fx.Window!.MouseMove(again + new AvaloniaPoint(20, 0), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            fx.Window!.MouseMove(again + new AvaloniaPoint(83, 29), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            fx.Window!.MouseUp(again + new AvaloniaPoint(83, 29), MouseButton.Left, RawInputModifiers.Shift);
            Assert.Equal(new Point(120, 60), link.ViewModel.Waypoints[0]);
        }

        [AvaloniaFact]
        public void SelectingALink_DeselectsNodes_UnlessShiftIsHeld() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = Straight(fx);
            BaseNodeElement node = (BaseNodeElement)link.SupplierElement!;
            AvaloniaPoint onLink = fx.ToScreen(new Point(0, 0));

            fx.Viewer.SetSelection([node]);
            fx.ClickAt(onLink);
            Assert.Empty(fx.Viewer.SelectedNodes);
            Assert.Same(link, Assert.Single(fx.Viewer.SelectedLinks));
            Assert.True(link.IsSelected);

            fx.Viewer.SetSelection([node]);
            Assert.Empty(fx.Viewer.SelectedLinks);
            Assert.False(link.IsSelected);

            fx.ClickAt(fx.ToScreen(new Point(0, 60)), RawInputModifiers.Shift);
            Assert.Same(node, Assert.Single(fx.Viewer.SelectedNodes));
            Assert.Same(link, Assert.Single(fx.Viewer.SelectedLinks));

            fx.ClickAt(fx.ToScreen(new Point(300, -200)));
            fx.Window!.MouseDown(onLink, MouseButton.Left, RawInputModifiers.None);
            fx.Window!.MouseMove(onLink + new AvaloniaPoint(0, 20), RawInputModifiers.LeftMouseButton);
            fx.Window!.MouseMove(onLink + new AvaloniaPoint(0, 40), RawInputModifiers.LeftMouseButton);
            Assert.Null(fx.Viewer.SelectionZone);
            fx.Window!.MouseUp(onLink + new AvaloniaPoint(0, 40), MouseButton.Left, RawInputModifiers.None);
            Assert.Same(link, Assert.Single(fx.Viewer.SelectedLinks));
            Assert.Empty(fx.Viewer.SelectedNodes);

            AvaloniaPoint background = fx.ToScreen(new Point(300, -200));
            fx.Window!.MouseDown(background, MouseButton.Left, RawInputModifiers.None);
            fx.Window!.MouseMove(background + new AvaloniaPoint(20, 20), RawInputModifiers.LeftMouseButton);
            fx.Window!.MouseMove(background + new AvaloniaPoint(40, 40), RawInputModifiers.LeftMouseButton);
            Assert.NotNull(fx.Viewer.SelectionZone);
            Assert.Equal(300, fx.Viewer.SelectionZone!.Value.X);
            Assert.Equal(-200, fx.Viewer.SelectionZone!.Value.Y);
            fx.Window!.MouseUp(background + new AvaloniaPoint(40, 40), MouseButton.Left, RawInputModifiers.None);
        }

        [AvaloniaFact]
        public void LinkContextMenu_RoundedOrthogonal_AddClearWaypoints_DeleteLink() {
            using LinkFixture fx = LinkFixture.Create();
            LinkElement link = Straight(fx);
            fx.Viewer.SetWaypoints(link, [new Point(0, 100)]);
            LinkElement second = fx.AddLink(fx.NewItem("copper-plate", Orange), new Point(400, 300), NodeDirection.Up, new Point(400, -300), NodeDirection.Up, LinkRouting.Orthogonal);
            LinkElement spare = fx.AddLink(fx.NewItem("iron-gear-wheel", Orange), new Point(-400, 300), NodeDirection.Up, new Point(-400, -300), NodeDirection.Up, LinkRouting.Orthogonal);

            fx.Viewer.SelectLink(link, additive: false);
            fx.Viewer.SelectLink(second, additive: true);
            fx.Window!.MouseDown(fx.ToScreen(new Point(0, 0)), MouseButton.Right, RawInputModifiers.None);
            fx.Window!.MouseUp(fx.ToScreen(new Point(0, 0)), MouseButton.Right, RawInputModifiers.None);
            IReadOnlyList<MenuEntry> mixed = fx.Control.LastContextMenuEntries!;
            Assert.False(mixed[0].Selected);
            Assert.False(mixed[1].Selected);
            Assert.Equal(2, fx.Viewer.SelectedLinks.Count);
            mixed[0].Invoke!();
            Assert.Equal(LinkRouting.Rounded, second.ViewModel.Routing);

            fx.Viewer.SelectLink(second, additive: false);
            fx.Window!.MouseDown(fx.ToScreen(new Point(0, 0)), MouseButton.Right, RawInputModifiers.None);
            fx.Window!.MouseUp(fx.ToScreen(new Point(0, 0)), MouseButton.Right, RawInputModifiers.None);

            IReadOnlyList<MenuEntry> menu = fx.Control.LastContextMenuEntries!;
            Assert.Equal<string?>(["Rounded", "Orthogonal", null, "Add Waypoint", "Clear Waypoints", null, "Delete Link"], menu.Select(e => e.Caption));
            Assert.True(menu[0].Selected);
            Assert.False(menu[1].Selected);
            Assert.Same(link, Assert.Single(fx.Viewer.SelectedLinks));

            menu[1].Invoke!();
            Assert.Equal(LinkRouting.Orthogonal, link.ViewModel.Routing);
            menu[4].Invoke!();
            Assert.Empty(link.ViewModel.Waypoints);
            menu[3].Invoke!();
            Assert.Single(link.ViewModel.Waypoints);

            fx.Viewer.SelectLink(link, additive: false);
            fx.Viewer.SelectLink(second, additive: true);
            fx.Window!.MouseDown(fx.ToScreen(new Point(0, 0)), MouseButton.Right, RawInputModifiers.None);
            fx.Window!.MouseUp(fx.ToScreen(new Point(0, 0)), MouseButton.Right, RawInputModifiers.None);
            fx.Control.LastContextMenuEntries![6].Invoke!();
            Assert.Same(spare, Assert.Single(fx.Viewer.LinkElements));
            Assert.Empty(fx.Viewer.SelectedLinks);

            fx.Window!.MouseDown(fx.ToScreen(new Point(-400, 0)), MouseButton.Right, RawInputModifiers.None);
            fx.Window!.MouseUp(fx.ToScreen(new Point(-400, 0)), MouseButton.Right, RawInputModifiers.None);
            Assert.Same(spare, Assert.Single(fx.Viewer.SelectedLinks));
            fx.Control.LastContextMenuEntries![6].Invoke!();
            Assert.Empty(fx.Viewer.LinkElements);
            Assert.Empty(fx.Viewer.SelectedLinks);
            Assert.Equal(6, fx.Viewer.NodeElements.Count);
        }
    }
}
