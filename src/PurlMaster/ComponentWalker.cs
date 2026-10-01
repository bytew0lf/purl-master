using System.Text.Json.Nodes;

namespace PurlMaster;

public static class ComponentWalker
{
    public static IEnumerable<(JsonObject Component, string Pointer)> Find(JsonObject bom)
    {
        var version = bom["specVersion"]?.GetValue<string>();
        foreach (var c in List(bom["components"], "/components")) yield return c;
        foreach (var c in Component(bom["metadata"]?["component"], "/metadata/component")) yield return c;
        if (version == "1.6")
            foreach (var c in List(bom["declarations"]?["targets"]?["components"], "/declarations/targets/components")) yield return c;
        // Older schemas permit extension properties; only visit locations defined for this BOM version.
        if (version is not ("1.5" or "1.6")) yield break;
        foreach (var c in List((bom["metadata"]?["tools"] as JsonObject)?["components"], "/metadata/tools/components")) yield return c;
        if (bom["vulnerabilities"] is JsonArray vulnerabilities)
            for (var i = 0; i < vulnerabilities.Count; i++)
                foreach (var c in List((vulnerabilities[i]?["tools"] as JsonObject)?["components"], $"/vulnerabilities/{i}/tools/components"))
                    yield return c;
        if (bom["annotations"] is JsonArray annotations)
            for (var i = 0; i < annotations.Count; i++)
                foreach (var c in Component(annotations[i]?["annotator"]?["component"], $"/annotations/{i}/annotator/component"))
                    yield return c;
        if (bom["formulation"] is JsonArray formulas)
            for (var i = 0; i < formulas.Count; i++)
                foreach (var c in List(formulas[i]?["components"], $"/formulation/{i}/components")) yield return c;
    }

    private static IEnumerable<(JsonObject Component, string Pointer)> Component(JsonNode? node, string path)
    {
        if (node is not JsonObject component) yield break;
        yield return (component, path);
        foreach (var child in List(component["components"], path + "/components")) yield return child;
        foreach (var name in new[] { "ancestors", "descendants", "variants" })
            foreach (var child in List(component["pedigree"]?[name], path + "/pedigree/" + name)) yield return child;
    }

    private static IEnumerable<(JsonObject Component, string Pointer)> List(JsonNode? node, string path)
    {
        if (node is not JsonArray array) yield break;
        for (var i = 0; i < array.Count; i++)
            foreach (var c in Component(array[i], path + "/" + i)) yield return c;
    }
}
