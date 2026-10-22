using Foreman.Mac.Canvas.Elements;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    internal static class CanvasTraversal {
        private const double OffAxisWeight = 2;

        internal static List<BaseNodeElement> ReadingOrder(IEnumerable<BaseNodeElement> nodes) =>
            [.. nodes.OrderBy(n => n.Location.Y).ThenBy(n => n.Location.X).ThenBy(n => n.ViewModel.Id.Value)];

        internal static BaseNodeElement? Spatial(BaseNodeElement from, Direction direction, IEnumerable<BaseNodeElement> candidates) {
            BaseNodeElement? best = null;
            double bestScore = double.MaxValue;
            foreach (BaseNodeElement candidate in candidates) {
                if (ReferenceEquals(candidate, from))
                    continue;
                double dx = candidate.Location.X - from.Location.X;
                double dy = candidate.Location.Y - from.Location.Y;
                (double along, double off) = direction switch {
                    Direction.Right => (dx, dy),
                    Direction.Left => (-dx, dy),
                    Direction.Down => (dy, dx),
                    Direction.Up => (-dy, dx),
                    _ => (0d, 0d),
                };
                if (along <= 0 || Math.Abs(off) > along)
                    continue;
                double score = Math.Sqrt((along * along) + (OffAxisWeight * off * (OffAxisWeight * off)));
                if (score < bestScore) {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        internal static List<LinkElement> LinksOf(BaseNodeElement node, IEnumerable<LinkElement> links) {
            var outputs = new List<LinkElement>();
            var inputs = new List<LinkElement>();
            foreach (LinkElement link in links) {
                if (ReferenceEquals(link.SupplierElement, node))
                    outputs.Add(link);
                else if (ReferenceEquals(link.ConsumerElement, node))
                    inputs.Add(link);
            }
            return [
                .. outputs.OrderBy(l => l.SupplierTab.GetConnectionPoint().X).ThenBy(l => l.ViewModel.Id.Value),
                .. inputs.OrderBy(l => l.ConsumerTab.GetConnectionPoint().X).ThenBy(l => l.ViewModel.Id.Value),
            ];
        }

        internal static List<LinkElement> LinkOrder(IEnumerable<BaseNodeElement> nodes, IReadOnlyCollection<LinkElement> links) {
            var ordered = new List<LinkElement>(links.Count);
            foreach (BaseNodeElement node in ReadingOrder(nodes))
                ordered.AddRange(LinksOf(node, links).Where(l => ReferenceEquals(l.SupplierElement, node)));
            return ordered;
        }

        internal static Rectangle WorldBounds(BaseNodeElement node) =>
            new(node.Location.X - node.Width / 2, node.Location.Y - node.Height / 2, node.Width, node.Height);
    }
}
