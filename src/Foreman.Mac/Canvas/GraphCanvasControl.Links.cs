using Avalonia.Input;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    public sealed partial class GraphCanvasControl {
        private static LinkElement? LinkOf(GraphElement? element) => element switch {
            LinkElement link => link,
            WaypointElement waypoint => waypoint.Link,
            _ => null,
        };

        private bool TryPressLink(GraphElement? clicked, PointerPressedEventArgs e, PointerPointProperties properties, Point graphPoint) {
            if (!properties.IsLeftButtonPressed || LinkOf(clicked) is not LinkElement link)
                return false;

            linkBodyPressed = false;
            var waypoint = clicked as WaypointElement;
            if (e.ClickCount == 2) {
                if (waypoint is not null)
                    Viewer.RemoveWaypoint(link, waypoint.Index);
                else
                    Viewer.AddWaypoint(link, graphPoint);
                ResetPointerDragState();
                InvalidateVisual();
                e.Handled = true;
                return true;
            }

            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || ModifierFor(e.KeyModifiers) == SelectionModifier.Add;
            Viewer.SelectLink(link, additive);
            if (waypoint is not null) {
                Viewer.SelectWaypoint(link, waypoint.Index);
                MouseDownElement = waypoint;
            } else {
                link.SelectedWaypointIndex = -1;
                MouseDownElement = null;
                linkBodyPressed = true;
            }

            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return true;
        }

        private void UpdateLinkHover(Point graphPoint, BaseNodeElement? hoveredNode) {
            LinkElement? hovered = null;
            if (MouseDownElement is WaypointElement draggedWaypoint)
                hovered = draggedWaypoint.Link;
            else if (hoveredNode is null && DraggedLink is null && CurrentDragOperation == DragOperation.None)
                hovered = Viewer.LinkTargetAt(graphPoint)?.Link;

            if (Viewer.SetHoveredLink(hovered))
                InvalidateVisual();
        }

        internal List<MenuEntry> BuildLinkMenu(LinkElement link, Point graphPoint) {
            IReadOnlyCollection<LinkElement> targets = Viewer.SelectedLinks.Contains(link) ? Viewer.SelectedLinks : new[] { link };
            bool anyWaypoints = targets.Any(l => l.ViewModel.Waypoints.Count > 0);
            return [
                MenuEntry.Radio("Rounded", targets.All(l => l.ViewModel.Routing == LinkRouting.Rounded), () => Viewer.SetSelectedLinksRouting(LinkRouting.Rounded)),
                MenuEntry.Radio("Orthogonal", targets.All(l => l.ViewModel.Routing == LinkRouting.Orthogonal), () => Viewer.SetSelectedLinksRouting(LinkRouting.Orthogonal)),
                MenuEntry.Divider,
                MenuEntry.Item("Add Waypoint", () => Viewer.AddWaypoint(link, graphPoint)),
                MenuEntry.Item("Clear Waypoints", Viewer.ClearSelectedWaypoints, anyWaypoints),
                MenuEntry.Divider,
                MenuEntry.Item("Delete Link", Viewer.DeleteSelectedLinks),
            ];
        }
    }
}
