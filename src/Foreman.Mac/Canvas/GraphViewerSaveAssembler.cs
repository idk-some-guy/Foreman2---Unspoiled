using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Canvas {
    //Ports GraphSaveWriter.WriteViewer/WriteViewerUi (upstream Serialization/GraphSaveWriter.cs:33-58):
    //Foreman.Core.Serialization.GraphSaveWriter can't carry this half over, since ProductionGraphViewer is a
    //WinForms UI type with no Core-layer equivalent (docs/io-reference.md §2 "What's missing"). Every field
    //below already has a live source on GraphViewer/DataCache; this only assembles them.
    public static class GraphViewerSaveAssembler {
        public static GraphViewerSaveDocument BuildSaveDocument(GraphViewer viewer, DataCache cache) => new() {
            Version = GraphSaveFormat.SaveFormatVersion,
            SavedPresetName = cache.PresetName,
            IncludedMods = new Dictionary<string, string>(cache.IncludedMods),
            ProductionGraph = GraphSaveWriter.WriteProductionGraph(viewer.Graph),
            Ui = new GraphViewerUiSaveData {
                Unit = viewer.Graph.SelectedRateUnit,
                ViewOffset = viewer.Viewport.ViewOffset,
                ViewScale = viewer.Viewport.ViewScale,
                ExtraProdForNonMiners = viewer.Graph.EnableExtraProductivityForNonMiners,
                AssemblerSelectorStyle = viewer.Graph.AssemblerSelector.DefaultSelectionStyle,
                ModuleSelectorStyle = viewer.Graph.ModuleSelector.DefaultSelectionStyle,
                FuelPriorityList = [.. viewer.Graph.FuelSelector.FuelPriority.Select(i => i.Name)],
                EnabledRecipes = SortEnabled(cache.Recipes.Values),
                EnabledAssemblers = SortEnabled(cache.Assemblers.Values),
                EnabledModules = SortEnabled(cache.Modules.Values),
                EnabledBeacons = SortEnabled(cache.Beacons.Values),
                OldImport = false,
            },
            Annotations = [.. viewer.Annotations.Select(a => a.ToSaveData())],
            AnnotationDpi = GraphViewer.AnnotationDeviceDpi,
        };

        private static List<string> SortEnabled<T>(IEnumerable<T> entities) where T : IDataObjectBase =>
            [.. entities.Where(e => e.Enabled).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal)];
    }
}
