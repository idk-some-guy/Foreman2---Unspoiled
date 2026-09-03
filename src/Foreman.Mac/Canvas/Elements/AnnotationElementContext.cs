using System;
using System.Collections.Generic;

namespace Foreman.Mac.Canvas.Elements {
    //P4 seam for AnnotationElement's right-click menu and handle sizing (mirrors NodeElementContext, reference
    //§4f/§6): annotations are constructed by static FromSaveData/blank-add factories with no viewer in scope
    //(Task 3's decoupled-construction choice), so this rides along as a settable Context assigned once
    //GraphViewer.AddAnnotationElement takes ownership, the same way NodeElementContext is threaded through
    //BaseNodeElement's constructor instead.
    public sealed class AnnotationElementContext {
        public required Func<float> ViewScale { get; init; }
        public required HashSet<AnnotationElement> SelectedAnnotations { get; init; }
        public required Func<int> SelectedNodeCount { get; init; }
        public Action<AnnotationElement>? ShowPropertiesDialog { get; init; }
        public Action? TryDeleteSelection { get; init; }
        public Action<AnnotationElement>? RemoveAnnotationElement { get; init; }
    }
}
