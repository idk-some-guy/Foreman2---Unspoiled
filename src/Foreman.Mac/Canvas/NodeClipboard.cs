using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    //Ports ProductionGraphViewer_KeyDown's Ctrl+C/X/V block and ImportNodesFromDocument's paste-centering
    //math (reference §5), plus the annotation splicing added in this task: Copy/Cut merge any selected
    //annotations' save data into the node fragment via AnnotationClipboardCodec.MergeAnnotationsIntoFragment,
    //and Paste independently imports any "Annotations" array via ImportAnnotationsAtOrigin - the two importers
    //don't depend on each other, so a fragment with only nodes, only annotations, or both all paste correctly.
    public static class NodeClipboard {
        public static string Copy(GraphViewer viewer) {
            viewer.Graph.SerializeNodeIdSet = [.. viewer.SelectedNodes.Select(n => n.ViewModel.Id.Value)];
            string fragmentJson = GraphSaveCodec.WriteProductionGraphToString(viewer.Graph, writeIndented: false);
            viewer.Graph.SerializeNodeIdSet = null;

            return viewer.SelectedAnnotations.Count == 0
                ? fragmentJson
                : AnnotationClipboardCodec.MergeAnnotationsIntoFragment(fragmentJson, viewer.SelectedAnnotations.Select(a => a.ToSaveData()));
        }

        public static string Cut(GraphViewer viewer) {
            string fragmentJson = Copy(viewer);
            foreach (BaseNodeElement node in viewer.SelectedNodes.ToList())
                viewer.Session.Editor.DeleteNode(node.ViewModel.Id);
            foreach (AnnotationElement annotation in viewer.SelectedAnnotations.ToList())
                viewer.RemoveAnnotationElement(annotation);
            return fragmentJson;
        }

        public static void Paste(GraphViewer viewer, DataCache cache, string json, Point origin) {
            bool pastedAnything = TryPasteNodes(viewer, cache, json, origin);

            IReadOnlyList<AnnotationSaveData>? annotations = AnnotationClipboardCodec.ReadAnnotations(json);
            if (annotations is { Count: > 0 }) {
                viewer.ImportAnnotationsAtOrigin(annotations, origin);
                pastedAnything = true;
            }

            if (pastedAnything) {
                viewer.Viewport.UpdateGraphBounds(viewer.Graph.Bounds);
                viewer.Graph.UpdateNodeValues();
            }
        }

        //Ports ImportNodesFromFragment's paste half (reference §5) via GraphViewer's shared merge/offset/
        //selection algorithm (reference §2) - the same one Import Graph's file merge uses.
        private static bool TryPasteNodes(GraphViewer viewer, DataCache cache, string json, Point origin) {
            try {
                return viewer.ImportNodesFromFragment(cache, json, origin, applySolverSettings: false) > 0;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Non-Foreman paste or invalid clipboard JSON");
                return false;
            }
        }
    }

    //Ports ProductionGraphView/NodeCopyOptions.cs (reference §5/§4c): a recipe node's build-config snapshot
    //for the "Copy this assembler's options"/paste-options clipboard flow. The `RecipeNode`-taking
    //constructor upstream declares is never actually called anywhere in that codebase, so it's dropped here
    //(see docs/upstream-divergences.md) - only the `IRecipeNodeViewModel` path RecipeNodeElement uses.
    public sealed class NodeCopyOptions {
        public AssemblerQualityPair Assembler { get; }
        public IReadOnlyList<ModuleQualityPair> AssemblerModules { get; }
        public IItem? Fuel { get; }
        public double NeighbourCount { get; }
        public double ExtraProductivityBonus { get; }
        public BeaconQualityPair Beacon { get; }
        public IReadOnlyList<ModuleQualityPair> BeaconModules { get; }
        public double BeaconCount { get; }
        public double BeaconsPerAssembler { get; }
        public double BeaconsConst { get; }

        public NodeCopyOptions(IRecipeNodeViewModel node) : this(
            node.SelectedAssembler,
            node.AssemblerModules,
            node.NeighbourCount,
            node.ExtraProductivity,
            node.Fuel,
            node.SelectedBeacon,
            node.BeaconModules,
            node.BeaconCount,
            node.BeaconsPerAssembler,
            node.BeaconsConst) {
        }

        private NodeCopyOptions(
            AssemblerQualityPair assembler,
            IReadOnlyList<ModuleQualityPair> assemblerModules,
            double neighbourCount,
            double extraProductivityBonus,
            IItem? fuel,
            BeaconQualityPair beacon,
            IReadOnlyList<ModuleQualityPair> beaconModules,
            double beaconCount,
            double beaconsPerAssembler,
            double beaconsConst) {
            Assembler = assembler;
            AssemblerModules = [.. assemblerModules];
            NeighbourCount = neighbourCount;
            ExtraProductivityBonus = extraProductivityBonus;
            Fuel = fuel;
            Beacon = beacon;
            BeaconModules = [.. beaconModules];
            BeaconCount = beaconCount;
            BeaconsPerAssembler = beaconsPerAssembler;
            BeaconsConst = beaconsConst;
        }

        public NodeCopyOptionsSaveDocument ToSaveDocument() => new() {
            Version = GraphSaveFormat.SaveFormatVersion,
            AssemblerName = Assembler.Assembler.Name,
            AssemblerQualityName = Assembler.Quality.Name,
            NeighbourCount = NeighbourCount,
            ExtraProductivityBonus = ExtraProductivityBonus,
            AssemblerModules = [.. AssemblerModules.Select(m => new ModuleQualitySaveData(m.Module.Name, m.Quality.Name))],
            BeaconModules = [.. BeaconModules.Select(m => new ModuleQualitySaveData(m.Module.Name, m.Quality.Name))],
            FuelName = Fuel?.Name,
            BeaconName = Beacon.Beacon?.Name,
            BeaconQualityName = Beacon.Quality?.Name,
            BeaconCount = BeaconCount,
            BeaconsPerAssembler = BeaconsPerAssembler,
            BeaconsConst = BeaconsConst
        };

        internal static NodeCopyOptions? FromSaveDocument(NodeCopyOptionsSaveDocument document, DataCache cache) {
            IQuality? defaultQuality = cache.DefaultQuality;

            if (!cache.Assemblers.TryGetValue(document.AssemblerName, out IAssembler? assembler) || assembler is null)
                return null;

            IQuality? assemblerQuality = ResolveQuality(cache, document.AssemblerQualityName, defaultQuality);
            if (assemblerQuality is null)
                return null;

            BeaconQualityPair beaconPair;
            if (document.BeaconName is not null) {
                if (!cache.Beacons.TryGetValue(document.BeaconName, out IBeacon? beacon) || beacon is null)
                    return null;
                IQuality? beaconQuality = ResolveQuality(cache, document.BeaconQualityName ?? "", defaultQuality);
                if (beaconQuality is null)
                    return null;
                beaconPair = new BeaconQualityPair(beacon, beaconQuality);
            } else
                beaconPair = new BeaconQualityPair();

            IItem? fuel = null;
            if (document.FuelName is not null && cache.Items.TryGetValue(document.FuelName, out IItem? fuelItem))
                fuel = fuelItem;

            return new NodeCopyOptions(
                new AssemblerQualityPair(assembler, assemblerQuality),
                ResolveModules(cache, document.AssemblerModules, defaultQuality),
                document.NeighbourCount,
                document.ExtraProductivityBonus,
                fuel,
                beaconPair,
                ResolveModules(cache, document.BeaconModules, defaultQuality),
                document.BeaconName is not null ? document.BeaconCount : 0,
                document.BeaconName is not null ? document.BeaconsPerAssembler : 0,
                document.BeaconName is not null ? document.BeaconsConst : 0);
        }

        private static IQuality? ResolveQuality(DataCache cache, string qualityName, IQuality? defaultQuality) =>
            cache.Qualities.TryGetValue(qualityName, out IQuality? quality)
                ? quality
                : cache.MissingQualities.TryGetValue(qualityName, out quality) ? quality : defaultQuality;

        private static List<ModuleQualityPair> ResolveModules(DataCache cache, IReadOnlyList<ModuleQualitySaveData> modules, IQuality? defaultQuality) {
            List<ModuleQualityPair> result = [];
            foreach (ModuleQualitySaveData moduleData in modules) {
                if (!cache.Modules.TryGetValue(moduleData.ModuleName, out IModule? module) || module is null)
                    continue;
                IQuality? quality = ResolveQuality(cache, moduleData.QualityName, defaultQuality);
                if (quality is null)
                    continue;
                result.Add(new ModuleQualityPair(module, quality));
            }
            return result;
        }

        public static NodeCopyOptions? GetNodeCopyOptions(string serialized, DataCache cache) {
            try {
                NodeCopyOptionsSaveDocument? document = GraphSaveCodec.ReadNodeCopyOptions(serialized);
                return document is null ? null : FromSaveDocument(document, cache);
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Failed to parse node copy options from clipboard");
                return null;
            }
        }
    }
}
