using System.Reflection;
using System.Text.Json.Nodes;
using Json.Schema;

namespace PurlMaster;

public sealed class BomValidator
{
    private static readonly string[] SupportedVersions = ["1.2", "1.3", "1.4", "1.5", "1.6"];
    private readonly Dictionary<string, JsonSchema> schemas = new(StringComparer.Ordinal);
    private readonly EvaluationOptions options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = false,
        EvaluateAs = SpecVersion.Draft7
    };

    public BomValidator()
    {
        var assembly = Assembly.GetExecutingAssembly();
        options.SchemaRegistry.Fetch = _ => throw new InvalidOperationException("External schema fetching is disabled.");
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".schema.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var loaded = JsonSchema.FromText(reader.ReadToEnd());
            options.SchemaRegistry.Register(loaded);
            foreach (var version in SupportedVersions)
                if (name.EndsWith($"bom-{version}.schema.json", StringComparison.Ordinal)) schemas.Add(version, loaded);
        }
        if (schemas.Count != SupportedVersions.Length)
            throw new InvalidOperationException("A bundled CycloneDX 1.2–1.6 schema is missing.");
    }

    public void Validate(JsonObject document)
    {
        if (document["bomFormat"] is not JsonValue format || !format.TryGetValue<string>(out var formatText) ||
            formatText != "CycloneDX")
            throw new FormatException("/bomFormat: Only CycloneDX JSON is supported.");
        if (document["specVersion"] is not JsonValue version || !version.TryGetValue<string>(out var versionText) ||
            versionText is null || !schemas.TryGetValue(versionText, out var schema))
            throw new FormatException("/specVersion: Supported CycloneDX versions are 1.2, 1.3, 1.4, 1.5 and 1.6.");

        var result = schema.Evaluate(document, options);
        if (!result.IsValid)
        {
            var errors = EnumerateErrors(result).Distinct().Take(8).ToArray();
            throw new FormatException($"CycloneDX {versionText} schema validation failed: " + string.Join("; ", errors));
        }
    }

    private static IEnumerable<string> EnumerateErrors(EvaluationResults result)
    {
        if (result.HasErrors)
            foreach (var error in result.Errors!)
                yield return $"{result.InstanceLocation}: {error.Value}";
        if (result.HasDetails)
            foreach (var child in result.Details)
                foreach (var error in EnumerateErrors(child)) yield return error;
    }
}
