using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Models;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Canvas.Elements {
    //Mirrors upstream's LOD enum (nested in ProductionGraphViewer). Low = names/icons only, Medium = adds
    //assembler/beacon icons, High = adds the percentage stat block. Only Spoil/Plant read this directly;
    //Assembler/Beacon LOD gating lands with RecipeNodeElement.
    public enum LevelOfDetail { Low, Medium, High }

    //Stands in for upstream's ProductionGraphViewer, which this port hasn't built yet (a later
    //orchestration layer). Carries only what BaseNodeElement/ItemTabElement/ErrorNoticeElement need from the
    //viewer for their read path: the session's view model for tab-order/tooltip lookups, the viewport for
    //screen-space tooltip anchors, and the display-setting flags that affect node chrome. LinkWidthLookup is
    //a forward seam for PassthroughNodeElement's simple-draw line width, which upstream reads from the
    //not-yet-ported LinkElement; it falls back to StaticLinkWidth until link elements exist.
    public sealed class NodeElementContext(IGraphViewModel view, Viewport viewport) {
        public IGraphViewModel View { get; } = view;
        public Viewport Viewport { get; } = viewport;

        public bool FlagOUSuppliedNodes { get; set; }
        public bool DynamicLinkWidth { get; set; }
        public bool ArrowsOnLinks { get; set; }
        public int IconsDrawSize { get; set; } = 32;
        public bool IconsOnly { get; set; }
        public LevelOfDetail LevelOfDetail { get; set; } = LevelOfDetail.Medium;
        public bool RoundAssemblerCount { get; set; }
        public bool EnableExtraProductivityForNonMiners { get; set; }
        public bool AbbreviateSciPacks { get; set; } = true;

        public float StaticLinkWidth { get; set; } = 3f;
        public Func<LinkId, float>? LinkWidthLookup { get; set; }

        //Set while an image export paints, so legibility reads the export scale and not the window zoom.
        public float? ExportViewScale { get; set; }

        public bool IsTextLegible(float fontSize) => CanvasText.IsLegible(fontSize, ExportViewScale ?? Viewport.ViewScale);

        public float GetLinkWidth(LinkId id) => LinkWidthLookup?.Invoke(id) ?? StaticLinkWidth;

        //The tab under the pointer and the tab under a dragged link's free end; the canvas and the drag set them on the UI thread.
        public ItemTabElement? HoveredTab { get; set; }
        public ItemTabElement? ArmedTab { get; set; }

        public Func<ProductionGraph.RateUnit>? RateUnitLookup { get; set; }
        public ProductionGraph.RateUnit RateUnit => RateUnitLookup?.Invoke() ?? ProductionGraph.RateUnit.Per1Sec;

        //The canvas's shared count/rate cross-fade clock: a node raises LabelValuesChanged on the UI thread, the canvas
        //bumps the generation and drives the progress, and the painters read both on whichever thread paints.
        public Action? LabelValuesChanged { get; set; }
        public int LabelFadeGeneration { get; set; }
        public double LabelFadeProgress { get; set; } = 1;

        //Seam for BaseNodeElement.SetLocation (upstream reaches these off `graphViewer` directly - this
        //port's node elements only ever see NodeElementContext, so the editor and the id->element lookup
        //ride along here instead).
        public IProductionGraphEditor? Editor { get; set; }
        public Func<NodeId, BaseNodeElement?>? GetNodeElement { get; set; }

        //Seam for BaseNodeElement's right-click menu (reference §4b): SelectedNodes is the live
        //GraphViewer set (not a snapshot), ClearSelection/TryDeleteSelectedNodes/FlipSelectedNodes ride along
        //the same way Editor does, since those three live on GraphViewer alongside SelectedNodes itself.
        //Clipboard access is Avalonia's async IClipboard underneath (upstream's WinForms Clipboard is
        //synchronous) - GraphCanvasControl adapts it to these plain synchronous delegates, see
        //docs/upstream-divergences.md.
        public HashSet<BaseNodeElement>? SelectedNodes { get; set; }
        public Action? ClearSelection { get; set; }
        public Action? TryDeleteSelectedNodes { get; set; }
        public Action? FlipSelectedNodes { get; set; }
        public Action? AutoconnectSelectionInputs { get; set; }
        public Action? AutoconnectSelectionOutputs { get; set; }
        public Action<string>? SetClipboardText { get; set; }
        public Func<string?>? GetClipboardText { get; set; }

        //Seam for RecipeNodeElement's paste-options block (reference §4c): the clipboard's NodeCopyOptions
        //payload can only be resolved against a live DataCache, which GraphViewer doesn't otherwise carry -
        //LoadDocument sets this to whatever cache it was just handed.
        public DataCache? DCache { get; set; }

        //Seam for BaseNodeElement.Dragged's tab-hit branch (reference §2/§3): starting a link drag is a
        //GraphCanvasControl concern (it owns MouseDownElement, which the ghost redirects to), so this rides
        //along the same way Editor/SelectedNodes already do.
        public Action<BaseNodeElement, LinkType, ItemQualityPair>? StartLinkDrag { get; set; }

        //Seam for BaseNodeElement.MouseUpLeft's fallback action (reference §8, upstream
        //MouseUpAction's left-click branch): opening the real edit panel needs FloatingPanelHost/RequestRedraw,
        //both GraphCanvasControl-only, so this rides along the same way StartLinkDrag does.
        public Action<BaseNodeElement>? EditNode { get; set; }
    }
}
