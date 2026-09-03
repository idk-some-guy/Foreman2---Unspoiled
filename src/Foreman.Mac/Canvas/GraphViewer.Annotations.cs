using Foreman.Mac.Canvas.Elements;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas {
    //Ports the annotation-selection/lifecycle half of ProductionGraphViewer.Annotations.cs (reference §6):
    //selectedAnnotations already lives on the main GraphViewer.cs partial (added in Task 2 for the node-drag
    //follower loop). This keeps a second, parallel selection system rather than folding it into
    //SetSelection/UpdateSelection/CommitLassoSelection - matching upstream's own two-collections split and
    //this task's explicit brief not to unify them. AnnotationSelectionModifiers' three static bools aren't
    //re-ported as their own type: this port already has the identical Alt=Remove/Ctrl=Add/neither=Replace
    //translation as SelectionModifier+GraphCanvasControl.ModifierFor, reused here for the modifier-reading
    //half only - the selection STATE stays on its own SelectedAnnotations set and its own commit methods.
    public sealed partial class GraphViewer {
        //Optional hook for opening a real annotation properties window (double-click, right-click Properties -
        //reference §6); GraphCanvasControl assigns the real Avalonia-window implementation, tests can leave it
        //null or stub it directly.
        public Action<AnnotationElement>? ShowAnnotationPropertiesDialog { get; set; }

        //Ports AddAnnotationElement/RemoveAnnotationElement (reference §6): wires up the Context seam every
        //annotation needs for its own right-click menu and handle sizing (Task 3 dropped the constructor-time
        //graphViewer reference, so this is assigned once the element joins the live tree instead).
        public void AddAnnotationElement(AnnotationElement element) {
            element.Context = new AnnotationElementContext {
                ViewScale = () => Viewport.ViewScale,
                SelectedAnnotations = SelectedAnnotations,
                SelectedNodeCount = () => SelectedNodes.Count,
                ShowPropertiesDialog = ann => ShowAnnotationPropertiesDialog?.Invoke(ann),
                TryDeleteSelection = TryDeleteSelection,
                RemoveAnnotationElement = RemoveAnnotationElement,
            };
            Annotations.Add(element);
        }

        public void RemoveAnnotationElement(AnnotationElement element) {
            Annotations.Remove(element);
            SelectedAnnotations.Remove(element);
            element.Dispose();
        }

        public void ClearAnnotationSelection() {
            foreach (AnnotationElement ann in SelectedAnnotations)
                ann.IsSelected = false;
            SelectedAnnotations.Clear();
        }

        //Ports SelectSingleAnnotation (reference §6): Replace clears the whole mixed selection first (mirrors
        //upstream's ClearNodeAndAnnotationSelection with no keepAnnotation), Add just adds, Remove is a no-op
        //(there's nothing to remove from a single click on an unselected annotation).
        public void SelectSingleAnnotation(AnnotationElement annotation, SelectionModifier modifier) {
            if (modifier == SelectionModifier.Add) {
                SelectedAnnotations.Add(annotation);
                annotation.IsSelected = true;
                return;
            }
            if (modifier == SelectionModifier.Remove)
                return;

            ClearSelection();
            SelectedAnnotations.Add(annotation);
            annotation.IsSelected = true;
        }

        //Ports HandleMouseUpOnTrackedAnnotation (reference §6): the click that follows MouseDown having
        //already claimed this annotation (it was selected when the click started). viewBeingDragged gates the
        //Replace branch the same way upstream's caller does, so a chorded pan-then-release doesn't reselect.
        public void HandleTrackedAnnotationMouseUp(AnnotationElement clicked, SelectionModifier modifier, bool viewBeingDragged) {
            if (modifier == SelectionModifier.Remove) {
                SelectedAnnotations.Remove(clicked);
                clicked.IsSelected = false;
            } else if (modifier == SelectionModifier.Add) {
                if (clicked.IsSelected)
                    SelectedAnnotations.Remove(clicked);
                else
                    SelectedAnnotations.Add(clicked);
                clicked.IsSelected = !clicked.IsSelected;
            } else if (!viewBeingDragged) {
                SelectSingleAnnotation(clicked, modifier);
            }
        }

        //Ports the item-drag mirror case (reference §2/§6's Annotation_OnItemDrag): a selected annotation
        //leading a drag moves every other selected annotation by the same raw delta and re-snaps every
        //selected node through SetLocation - the asymmetric counterpart to the node-leads case in
        //GraphCanvasControl's own DragOperation.Item handling.
        public void DragSelectedAnnotationsAndNodes(AnnotationElement leader, int dx, int dy) {
            foreach (AnnotationElement ann in SelectedAnnotations.Where(a => a != leader)) {
                ann.X += dx;
                ann.Y += dy;
            }
            foreach (BaseNodeElement node in SelectedNodes)
                node.SetLocation(new Point(node.X + dx, node.Y + dy));
        }

        public HashSet<AnnotationElement> GetAnnotationsIntersectingLasso(Rectangle lasso) =>
            [.. Annotations.Where(a => a.LassoIntersectsEdge(lasso))];

        public void UpdateAnnotationLassoPreview(Rectangle lasso, SelectionModifier modifier) =>
            ApplyAnnotationZoneSelection(GetAnnotationsIntersectingLasso(lasso), modifier, commit: false);

        public void CommitAnnotationLassoSelection(Rectangle lasso, SelectionModifier modifier) =>
            ApplyAnnotationZoneSelection(GetAnnotationsIntersectingLasso(lasso), modifier, commit: true);

        //Ports ApplyAnnotationZoneSelection (reference §1/§6): same preview/commit split as the node lasso,
        //kept as its own method rather than reusing UpdateSelection/CommitLassoSelection per this task's brief.
        private void ApplyAnnotationZoneSelection(HashSet<AnnotationElement> zoneAnnotations, SelectionModifier modifier, bool commit) {
            if (modifier == SelectionModifier.Remove) {
                if (commit) {
                    foreach (AnnotationElement ann in SelectedAnnotations.Where(zoneAnnotations.Contains).ToList()) {
                        ann.IsSelected = false;
                        SelectedAnnotations.Remove(ann);
                    }
                } else {
                    foreach (AnnotationElement ann in Annotations)
                        ann.IsSelected = SelectedAnnotations.Contains(ann) && !zoneAnnotations.Contains(ann);
                }
                return;
            }

            if (modifier == SelectionModifier.Add) {
                foreach (AnnotationElement ann in Annotations)
                    ann.IsSelected = SelectedAnnotations.Contains(ann) || zoneAnnotations.Contains(ann);
                if (commit)
                    foreach (AnnotationElement ann in zoneAnnotations)
                        SelectedAnnotations.Add(ann);
                return;
            }

            foreach (AnnotationElement ann in Annotations)
                ann.IsSelected = zoneAnnotations.Contains(ann);
            if (commit) {
                ClearAnnotationSelection();
                foreach (AnnotationElement ann in zoneAnnotations) {
                    ann.IsSelected = true;
                    SelectedAnnotations.Add(ann);
                }
            }
        }

        //Ports TryDeleteSelection (reference §4f/§6): the combined node+annotation delete used by the
        //annotation right-click menu's "Delete selection" item - distinct from TryDeleteSelectedNodes, which
        //stays node-only for the node right-click menu's own "Delete selected nodes" item.
        public void TryDeleteSelection() {
            int total = SelectedNodes.Count + SelectedAnnotations.Count;
            if (total == 0)
                return;
            if (total > 10 && ConfirmBulkDelete is { } confirm && !confirm(total))
                return;

            foreach (BaseNodeElement node in SelectedNodes.ToList())
                Session.Editor.DeleteNode(node.ViewModel.Id);
            SelectedNodes.Clear();

            foreach (AnnotationElement ann in SelectedAnnotations.ToList())
                RemoveAnnotationElement(ann);

            Graph.UpdateNodeValues();
        }

        //Ports ImportAnnotationsAtOrigin (reference §5/§9): centers the imported set's centroid on origin,
        //selecting them the same way NodeClipboard.Paste replaces the node selection with just-pasted nodes.
        public void ImportAnnotationsAtOrigin(IReadOnlyList<Foreman.Serialization.AnnotationSaveData> annotations, Point origin) {
            if (annotations.Count == 0)
                return;

            List<AnnotationElement> imported = [.. annotations
                .Select(data => AnnotationLoader.TryCreateAnnotationFromSave(data, dpiScale: 1f, out AnnotationElement? ann) ? ann : null)
                .OfType<AnnotationElement>()];
            if (imported.Count == 0)
                return;

            long xAve = imported.Sum(a => (long)a.X) / imported.Count;
            long yAve = imported.Sum(a => (long)a.Y) / imported.Count;
            var offset = new Point(origin.X - (int)xAve, origin.Y - (int)yAve);

            //Ports the upstream method verbatim: it does NOT clear the existing annotation selection first
            //(unlike NodeClipboard.Paste's SetSelection, which replaces the node selection outright) - imported
            //annotations are added on top of whatever was already selected.
            foreach (AnnotationElement ann in imported) {
                ann.X += offset.X;
                ann.Y += offset.Y;
                ann.IsSelected = true;
                AddAnnotationElement(ann);
                SelectedAnnotations.Add(ann);
            }
        }

        //Ports ImportAnnotationsWithOffset (reference §9): a fixed-offset counterpart to ImportAnnotationsAtOrigin
        //(upstream's Import Graph menu action uses this one, paste uses the centroid version above). Not wired
        //to anything in this port yet - MainWindow's Import command is still an unimplemented ShellCommand
        //stub (task 12/P5 scope), ported here so the clipboard/import pairing stays complete per the reference.
        public void ImportAnnotationsWithOffset(IReadOnlyList<Foreman.Serialization.AnnotationSaveData> annotations, Size offset) {
            foreach (Foreman.Serialization.AnnotationSaveData data in annotations) {
                if (!AnnotationLoader.TryCreateAnnotationFromSave(data, dpiScale: 1f, out AnnotationElement? ann))
                    continue;

                ann.X += offset.Width;
                ann.Y += offset.Height;
                ann.IsSelected = true;
                AddAnnotationElement(ann);
                SelectedAnnotations.Add(ann);
            }
        }
    }
}
