using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Graph {
    /// <summary>Connects unlinked node inputs to the nearest compatible output on another node.</summary>
    public static class GraphAutoconnect {
        //scope null is the whole-graph pass (the Autoconnect toolbar button - reference §8, byte-identical to
        //upstream's ConnectDisconnectedInputs when called this way). A non-null scope restricts both the
        //consumers being satisfied and the eligible suppliers to that node set, and additionally excludes any
        //scoped node that itself has an open input from acting as a supplier - this is the selection-scoped
        //algorithm the node right-click menu's "Auto-connect disconnected inputs" item used to hand-roll
        //separately (reference §4b/§11 step 11's consolidation note).
        public static int ConnectDisconnectedInputs(ProductionGraphSession session, IReadOnlyCollection<NodeId>? scope = null) {
            ArgumentNullException.ThrowIfNull(session);

            IReadOnlyList<INodeViewModel> nodes = ScopedNodes(session, scope);
            HashSet<NodeId> ineligibleSuppliers = scope is null ? [] : NodesWithOpenInputs(nodes);

            var suppliersByItem = new Dictionary<ItemQualityPair, List<NodeId>>();
            foreach (INodeViewModel node in nodes) {
                if (ineligibleSuppliers.Contains(node.Id))
                    continue;
                foreach (ItemQualityPair output in node.Outputs) {
                    if (!suppliersByItem.TryGetValue(output, out List<NodeId>? supplierIds))
                        suppliersByItem[output] = supplierIds = [];
                    supplierIds.Add(node.Id);
                }
            }

            int linksCreated = 0;
            foreach (INodeViewModel consumer in nodes) {
                foreach (ItemQualityPair input in consumer.Inputs
            .Where(input => !consumer.InputLinks.Any(link => link.Item == input))) {
                    if (!suppliersByItem.TryGetValue(input, out List<NodeId>? suppliers))
                        continue;

                    NodeId supplierId = suppliers
                        .Where(id => id != consumer.Id)
                        .OrderBy(id => ManhattanDistance(session, id, consumer.Id))
                        .FirstOrDefault();

                    if (!supplierId.IsValid)
                        continue;

                    session.Editor.CreateLink(supplierId, consumer.Id, input);
                    linksCreated++;
                }
            }

            if (linksCreated > 0)
                session.Graph.UpdateNodeValues();

            return linksCreated;
        }

        //Mirror of ConnectDisconnectedInputs for the opposite direction - the node right-click menu's
        //"Auto-connect disconnected outputs" item (reference §4b). Upstream has no whole-graph equivalent of
        //this one (only the inputs direction gets a toolbar button), so this only takes a scope.
        public static int ConnectDisconnectedOutputs(ProductionGraphSession session, IReadOnlyCollection<NodeId> scope) {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(scope);

            IReadOnlyList<INodeViewModel> nodes = ScopedNodes(session, scope);
            HashSet<NodeId> ineligibleConsumers = NodesWithOpenOutputs(nodes);

            var consumersByItem = new Dictionary<ItemQualityPair, List<NodeId>>();
            foreach (INodeViewModel node in nodes) {
                if (ineligibleConsumers.Contains(node.Id))
                    continue;
                foreach (ItemQualityPair input in node.Inputs) {
                    if (!consumersByItem.TryGetValue(input, out List<NodeId>? consumerIds))
                        consumersByItem[input] = consumerIds = [];
                    consumerIds.Add(node.Id);
                }
            }

            int linksCreated = 0;
            foreach (INodeViewModel supplier in nodes) {
                foreach (ItemQualityPair output in supplier.Outputs
            .Where(output => !supplier.OutputLinks.Any(link => link.Item == output))) {
                    if (!consumersByItem.TryGetValue(output, out List<NodeId>? consumers))
                        continue;

                    NodeId consumerId = consumers
                        .Where(id => id != supplier.Id)
                        .OrderBy(id => ManhattanDistance(session, id, supplier.Id))
                        .FirstOrDefault();

                    if (!consumerId.IsValid)
                        continue;

                    session.Editor.CreateLink(supplier.Id, consumerId, output);
                    linksCreated++;
                }
            }

            if (linksCreated > 0)
                session.Graph.UpdateNodeValues();

            return linksCreated;
        }

        private static IReadOnlyList<INodeViewModel> ScopedNodes(ProductionGraphSession session, IReadOnlyCollection<NodeId>? scope) =>
            scope is null ? session.View.Nodes : [.. session.View.Nodes.Where(n => scope.Contains(n.Id))];

        private static HashSet<NodeId> NodesWithOpenInputs(IEnumerable<INodeViewModel> nodes) =>
            [.. nodes.Where(n => n.Inputs.Any(input => !n.InputLinks.Any(link => link.Item == input))).Select(n => n.Id)];

        private static HashSet<NodeId> NodesWithOpenOutputs(IEnumerable<INodeViewModel> nodes) =>
            [.. nodes.Where(n => n.Outputs.Any(output => !n.OutputLinks.Any(link => link.Item == output))).Select(n => n.Id)];

        private static int ManhattanDistance(ProductionGraphSession session, NodeId a, NodeId b) {
            return !session.View.TryGetNode(a, out INodeViewModel? nodeA) || nodeA is null
                ? int.MaxValue
                : !session.View.TryGetNode(b, out INodeViewModel? nodeB) || nodeB is null
                ? int.MaxValue
                : Math.Abs(nodeA.Location.X - nodeB.Location.X) + Math.Abs(nodeA.Location.Y - nodeB.Location.Y);
        }
    }
}
