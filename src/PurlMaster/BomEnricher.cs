using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PurlMaster;

public sealed record ComponentCounts(int Added, int Existing, int WithoutCpe, int Skipped)
{
    public static ComponentCounts operator +(ComponentCounts a, ComponentCounts b) =>
        new(a.Added + b.Added, a.Existing + b.Existing, a.WithoutCpe + b.WithoutCpe, a.Skipped + b.Skipped);
}

public sealed record EnrichmentResult(byte[] Output, ComponentCounts Counts, IReadOnlyList<string> Warnings);

public sealed class BomEnricher(BomValidator validator)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public EnrichmentResult Enrich(byte[] input)
    {
        var start = input.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        var json = StrictUtf8.GetString(input, start, input.Length - start);
        using var parsed = JsonDocument.Parse(json);
        RejectDuplicates(parsed.RootElement, "");
        if (JsonNode.Parse(json) is not JsonObject document)
            throw new FormatException("The SBOM root must be a JSON object.");
        validator.Validate(document);

        var added = 0;
        var existing = 0;
        var withoutCpe = 0;
        var skipped = 0;
        var warnings = new List<string>();
        foreach (var (component, pointer) in ComponentWalker.Find(document))
        {
            if (component.ContainsKey("purl")) { existing++; continue; }
            if (!component.ContainsKey("cpe")) { withoutCpe++; continue; }
            try
            {
                component["purl"] = CpePurlConverter.Convert(component["cpe"]!.GetValue<string>());
                added++;
            }
            catch (FormatException ex)
            {
                skipped++;
                warnings.Add($"{pointer}/cpe: {ex.Message}");
            }
        }

        if (added > 0) validator.Validate(document);
        var output = added == 0 ? input : StrictUtf8.GetBytes(document.ToJsonString(OutputOptions) + "\n");
        return new(output, new(added, existing, withoutCpe, skipped), warnings);
    }

    private static void RejectDuplicates(JsonElement node, string pointer)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                var child = pointer + "/" + property.Name.Replace("~", "~0").Replace("/", "~1");
                if (!names.Add(property.Name)) throw new FormatException($"{child}: Duplicate JSON property.");
                RejectDuplicates(property.Value, child);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in node.EnumerateArray()) RejectDuplicates(item, pointer + "/" + i++);
        }
    }
}
