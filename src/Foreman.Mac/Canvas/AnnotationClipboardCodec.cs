using Foreman.Serialization;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foreman.Mac.Canvas {
    //Ports ProductionGraphView/Annotations/AnnotationClipboardCodec.cs. Both methods operate on already-
    //parsed/serialized JsonNode trees (AnnotationJson.DeserializeListFromRoot/SerializeToArray, both public in
    //Foreman.Core), so this doesn't need upstream's internal GraphSaveJsonOptions - re-emitting a JsonNode tree
    //doesn't go through the typed-serialization naming/ignore-condition settings that options object controls.
    public static class AnnotationClipboardCodec {
        public static IReadOnlyList<AnnotationSaveData>? ReadAnnotations(string json) {
            try {
                JsonNode? root = JsonNode.Parse(json);
                return AnnotationJson.DeserializeListFromRoot(root);
            } catch (JsonException) {
                return null;
            }
        }

        public static string MergeAnnotationsIntoFragment(string productionGraphFragmentJson, IEnumerable<AnnotationSaveData> annotations) {
            JsonNode? parsed;
            try {
                parsed = JsonNode.Parse(productionGraphFragmentJson);
            } catch (JsonException) {
                return productionGraphFragmentJson;
            }

            if (parsed is not JsonObject root)
                return productionGraphFragmentJson;

            root["Annotations"] = AnnotationJson.SerializeToArray(annotations);
            return root.ToJsonString();
        }
    }
}
