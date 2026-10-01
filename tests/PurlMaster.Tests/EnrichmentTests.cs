using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PurlMaster.Tests;

public class EnrichmentTests
{
    private readonly BomEnricher enricher = new(new BomValidator());
    public const string Cpe = "cpe:2.3:a:vendor:product:1.0:*:*:*:*:*:*:*";

    public static JsonObject Component(string name = "product") => new()
    {
        ["type"] = "library", ["name"] = name, ["cpe"] = Cpe
    };

    public static byte[] Bom(params JsonObject[] components) => Encoding.UTF8.GetBytes(new JsonObject
    {
        ["bomFormat"] = "CycloneDX", ["specVersion"] = "1.6", ["components"] = new JsonArray(components.Cast<JsonNode>().ToArray())
    }.ToJsonString());

    [Fact]
    public void SuppliedFixturePreservesEveryValueExceptTheAddedPurl()
    {
        var input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/test-bom.json"));
        var result = enricher.Enrich(input);
        Assert.Equal(new ComponentCounts(1, 2, 1, 0), result.Counts);
        Assert.Empty(result.Warnings);
        var original = JsonNode.Parse(input)!;
        var output = JsonNode.Parse(result.Output)!;
        Assert.Equal("pkg:generic/busybox/busybox@1.36.1?cpe_part=a", output["components"]![1]!["purl"]!.GetValue<string>());
        ((JsonObject)output["components"]![1]!).Remove("purl");
        Assert.True(JsonNode.DeepEquals(original, output));
        Assert.Equal(result.Output, enricher.Enrich(result.Output).Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pkg:maven/org.Example/widget@1.0?type=jar")]
    [InlineData("Keep THIS literal identifier")]
    public void ExistingPurlIsPreservedAndItsMalformedCpeIsNotParsed(string purl)
    {
        var component = Component();
        component["purl"] = purl;
        component["cpe"] = "invalid";
        var bytes = Bom(component);
        var result = enricher.Enrich(bytes);
        Assert.Equal(new ComponentCounts(0, 1, 0, 0), result.Counts);
        Assert.Equal(bytes, result.Output);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SkipsOnlyAffectedComponentsAndReportsPointers()
    {
        var missing = Component("missing");
        missing.Remove("cpe");
        var invalid = Component("invalid");
        invalid["cpe"] = "";
        var pattern = Component("pattern");
        pattern["cpe"] = "cpe:/a:vendor:product:1.%02";
        var result = enricher.Enrich(Bom(Component(), missing, invalid, pattern));
        Assert.Equal(new ComponentCounts(1, 0, 1, 2), result.Counts);
        Assert.Equal(2, result.Warnings.Count);
        Assert.StartsWith("/components/2/cpe:", result.Warnings[0]);
        Assert.StartsWith("/components/3/cpe:", result.Warnings[1]);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1.6")]
    public void DiscoversAllSchemaComponentLocationsAndTheirNestedComponents(string version)
    {
        JsonObject Tree(string name)
        {
            var root = Component(name);
            root["components"] = new JsonArray(Component(name + "-nested"));
            root["pedigree"] = new JsonObject();
            foreach (var relationship in new[] { "ancestors", "descendants", "variants" })
            {
                var relative = Component(name + "-" + relationship);
                relative["components"] = new JsonArray(Component(name + "-" + relationship + "-nested"));
                root["pedigree"]![relationship] = new JsonArray(relative);
            }
            return root;
        }
        var bom = new JsonObject
        {
            ["bomFormat"] = "CycloneDX", ["specVersion"] = "1.6",
            ["components"] = new JsonArray(Tree("top")),
            ["metadata"] = new JsonObject
            {
                ["component"] = Tree("metadata"),
                ["tools"] = new JsonObject { ["components"] = new JsonArray(Tree("metadata-tool")) }
            },
            ["vulnerabilities"] = new JsonArray(new JsonObject
            {
                ["tools"] = new JsonObject { ["components"] = new JsonArray(Tree("vulnerability-tool")) }
            }),
            ["declarations"] = new JsonObject
            {
                ["targets"] = new JsonObject { ["components"] = new JsonArray(Tree("target")) }
            },
            ["formulation"] = new JsonArray(new JsonObject { ["components"] = new JsonArray(Tree("formula")) }),
            ["annotations"] = new JsonArray(new JsonObject
            {
                ["subjects"] = new JsonArray("subject"), ["timestamp"] = "2026-10-01T00:00:00Z", ["text"] = "note",
                ["annotator"] = new JsonObject { ["component"] = Tree("annotator") }
            })
        };
        bom["specVersion"] = version;
        if (version == "1.5") bom.Remove("declarations");
        var expected = version == "1.6" ? 56 : 48;
        var result = enricher.Enrich(Encoding.UTF8.GetBytes(bom.ToJsonString()));
        Assert.Equal(expected, result.Counts.Added);
        var discovered = ComponentWalker.Find((JsonObject)JsonNode.Parse(result.Output)!).ToArray();
        Assert.Equal(expected, discovered.Select(x => x.Pointer).Distinct().Count());
        Assert.All(discovered, x => Assert.Equal("pkg:generic/vendor/product@1.0?cpe_part=a", x.Component["purl"]!.GetValue<string>()));
    }

    [Fact]
    public void WalkerIgnoresObjectsOutsideComponentPositions()
    {
        var arbitrary = Component("unrelated");
        var bom = new JsonObject { ["unrelated"] = arbitrary, ["components"] = new JsonArray(Component()) };
        Assert.Single(ComponentWalker.Find(bom));
        Assert.False(arbitrary.ContainsKey("purl"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"bomFormat\":\"SPDX\",\"specVersion\":\"1.6\"}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.1\"}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.7\"}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":1.6}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":[{\"type\":\"library\"}]}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"extra\":true}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":[{\"type\":\"library\",\"name\":\"x\",\"purl\":null}]}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":[{\"type\":\"library\",\"name\":\"x\",\"cpe\":5}]}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":[{\"type\":\"library\",\"name\":\"x\",\"licenses\":[{\"license\":{\"id\":\"not-an-spdx-license\"}}]}]}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"signature\":{\"algorithm\":5}}")]
    public void RejectsUnsupportedOrSchemaInvalidDocuments(string json) =>
        Assert.Throws<FormatException>(() => enricher.Enrich(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("{")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",}")]
    public void RejectsMalformedJson(string json) =>
        Assert.ThrowsAny<JsonException>(() => enricher.Enrich(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void RejectsDuplicatePropertiesBeforeTheyCanBeLost()
    {
        var json = "{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":[{\"type\":\"library\",\"name\":\"x\",\"purl\":\"keep\",\"purl\":\"replace\"}]}";
        Assert.Contains("/components/0/purl", Assert.Throws<FormatException>(() => enricher.Enrich(Encoding.UTF8.GetBytes(json))).Message);
    }

    [Fact]
    public void AcceptsUtf8BomAndPreservesNoOpBytes()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("  {\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\"}\r\n")).ToArray();
        Assert.Equal(bytes, enricher.Enrich(bytes).Output);
    }

    [Fact]
    public void RejectsInvalidUtf8() =>
        Assert.Throws<DecoderFallbackException>(() => enricher.Enrich([0xFF]));

    [Fact]
    public void InputSchemaUrlDoesNotOverrideTheBundledSchema()
    {
        var doc = (JsonObject)JsonNode.Parse(Bom(Component()))!;
        doc["$schema"] = "https://invalid.example/untrusted.schema.json";
        var result = enricher.Enrich(Encoding.UTF8.GetBytes(doc.ToJsonString()));
        Assert.Equal(1, result.Counts.Added);
        Assert.Equal(doc["$schema"]!.GetValue<string>(), JsonNode.Parse(result.Output)!["$schema"]!.GetValue<string>());
    }
}
