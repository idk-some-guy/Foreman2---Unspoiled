using Foreman.Graph;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    public sealed partial class GraphViewer {
        public LinkElement? WaypointSelectionLink => SelectedLinks.FirstOrDefault(link => link.SelectedWaypointIndex >= 0);

        public (LinkElement Link, WaypointElement? Waypoint)? LinkTargetAt(Point graphPoint) {
            for (int i = LinkElements.Count - 1; i >= 0; i--) {
                LinkElement link = LinkElements[i];
                if (!link.Visible && !link.IsSelected)
                    continue;
                if (link.WaypointAt(graphPoint) is WaypointElement waypoint)
                    return (link, waypoint);
            }
            for (int i = LinkElements.Count - 1; i >= 0; i--) {
                LinkElement link = LinkElements[i];
                if (link.Visible && link.HitTest(graphPoint))
                    return (link, null);
            }
            return null;
        }

        public bool SetHoveredLink(LinkElement? hovered) {
            bool changed = false;
            foreach (LinkElement link in LinkElements) {
                bool hover = ReferenceEquals(link, hovered);
                if (link.IsHovered == hover)
                    continue;
                link.IsHovered = hover;
                changed = true;
            }
            return changed;
        }

        public void SelectLink(LinkElement link, bool additive) {
            if (additive && SelectedLinks.Contains(link)) {
                SelectedLinks.Remove(link);
                link.IsSelected = false;
                link.SelectedWaypointIndex = -1;
                RaiseSelectionChanged();
                return;
            }

            if (!additive) {
                foreach (BaseNodeElement element in SelectedNodes)
                    element.Highlighted = false;
                SelectedNodes.Clear();
                CurrentSelectionNodes.Clear();
                ClearAnnotationSelection();
                ClearLinkSelection(except: link);
            }
            SelectedLinks.Add(link);
            link.IsSelected = true;
            RaiseSelectionChanged();
        }

        public void SelectWaypoint(LinkElement link, int index) {
            foreach (LinkElement other in SelectedLinks.Where(l => !ReferenceEquals(l, link)))
                other.SelectedWaypointIndex = -1;
            link.SelectedWaypointIndex = index;
        }

        private void ClearLinkSelection(LinkElement? except = null) {
            foreach (LinkElement link in SelectedLinks.ToList()) {
                if (ReferenceEquals(link, except))
                    continue;
                link.IsSelected = false;
                link.SelectedWaypointIndex = -1;
                SelectedLinks.Remove(link);
            }
        }

        public void SetSelectedLinksRouting(LinkRouting routing) {
            foreach (LinkElement link in SelectedLinks)
                Session.Editor.SetLinkRouting(link.ViewModel.Id, routing);
        }

        public void ClearSelectedWaypoints() {
            foreach (LinkElement link in SelectedLinks.Where(l => l.ViewModel.Waypoints.Count > 0))
                SetWaypoints(link, []);
        }

        public void CarryWaypoints(IReadOnlyCollection<BaseNodeElement> nodes, Action move) {
            if (nodes.Count < 2) {
                move();
                return;
            }

            Dictionary<BaseNodeElement, Point> before = nodes.ToDictionary(node => node, node => node.Location);
            move();
            foreach (LinkElement link in LinkElements.ToList()) {
                if (link.SupplierElement is not BaseNodeElement supplier || link.ConsumerElement is not BaseNodeElement consumer
                    || !before.TryGetValue(supplier, out Point supplierBefore) || !before.TryGetValue(consumer, out Point consumerBefore)
                    || link.ViewModel.Waypoints.Count == 0)
                    continue;

                Size supplierDelta = new(supplier.Location.X - supplierBefore.X, supplier.Location.Y - supplierBefore.Y);
                Size consumerDelta = new(consumer.Location.X - consumerBefore.X, consumer.Location.Y - consumerBefore.Y);
                if (supplierDelta != consumerDelta || supplierDelta == Size.Empty)
                    continue;
                SetWaypoints(link, [.. link.ViewModel.Waypoints.Select(waypoint => Point.Add(waypoint, supplierDelta))]);
            }
        }

        public void SetWaypoints(LinkElement link, IReadOnlyList<Point> waypoints) {
            Session.Editor.SetLinkWaypoints(link.ViewModel.Id, waypoints);
            link.SelectedWaypointIndex = link.SelectedWaypointIndex >= waypoints.Count ? -1 : link.SelectedWaypointIndex;
        }

        public void AddWaypoint(LinkElement link, Point graphPoint) {
            Point at = link.NearestPointOnPath(graphPoint, out int index);
            List<Point> waypoints = [.. link.ViewModel.Waypoints];
            waypoints.Insert(index, at);
            SetWaypoints(link, waypoints);
            if (!SelectedLinks.Contains(link))
                SelectLink(link, additive: false);
            SelectWaypoint(link, index);
        }

        public void RemoveWaypoint(LinkElement link, int index) {
            if (index < 0 || index >= link.ViewModel.Waypoints.Count)
                return;
            List<Point> waypoints = [.. link.ViewModel.Waypoints];
            waypoints.RemoveAt(index);
            link.SelectedWaypointIndex = -1;
            SetWaypoints(link, waypoints);
        }

        public bool RemoveSelectedWaypoint() {
            if (WaypointSelectionLink is not LinkElement link)
                return false;
            RemoveWaypoint(link, link.SelectedWaypointIndex);
            return true;
        }

        public void CommitWaypointPreview(LinkElement link) {
            if (link.WaypointPreview is not IReadOnlyList<Point> preview)
                return;
            link.WaypointPreview = null;
            SetWaypoints(link, preview);
        }

        public void DiscardWaypointPreviews() {
            foreach (LinkElement link in LinkElements)
                link.WaypointPreview = null;
        }

        public void DeleteSelectedLinks() {
            foreach (LinkElement link in SelectedLinks.ToList())
                Session.Editor.DeleteLink(link.ViewModel.Id);
            SelectedLinks.Clear();
            Graph.UpdateNodeValues();
            RaiseSelectionChanged();
        }
    }
}
