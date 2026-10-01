using System.Text;
using System.Text.Json.Nodes;

namespace PurlMaster.Tests;

public class VersionSupportTests
{
    private readonly BomEnricher enricher = new(new BomValidator());
    private const string ExpectedPurl = "pkg:generic/vendor/product@1.0?cpe_part=a";

    public static JsonObject Component(string name = "product")
    {
        var component = EnrichmentTests.Component(name);
        component["version"] = "7.9";
        return component;
    }

    public static JsonObject Bom(string version, params JsonObject[] components) => new()
    {
        ["bomFormat"] = "CycloneDX", ["specVersion"] = version, ["version"] = 9,
        ["components"] = new JsonArray(components.Cast<JsonNode>().ToArray())
    };

    private EnrichmentResult Enrich(JsonObject document) => enricher.Enrich(Encoding.UTF8.GetBytes(document.ToJsonString()));

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.3")]
    [InlineData("1.4")]
    [InlineData("1.5")]
    [InlineData("1.6")]
    public void EnrichesEveryVersionAndPreservesExistingValuesAndVersion(string version)
    {
        JsonObject Tree(string name)
        {
            var component = Component(name);
            component["components"] = new JsonArray(Component(name + "-nested"));
            component["pedigree"] = new JsonObject();
            foreach (var relationship in new[] { "ancestors", "descendants", "variants" })
                component["pedigree"]![relationship] = new JsonArray(Component(name + "-" + relationship));
            return component;
        }
        var existing = Component("existing");
        existing["purl"] = "pkg:maven/org.Example/widget@2.0?type=jar";
        existing["cpe"] = "not-a-valid-cpe";
        var withoutCpe = Component("without-cpe");
        withoutCpe.Remove("cpe");
        var bom = Bom(version, Tree("root"), existing, withoutCpe);
        bom["metadata"] = new JsonObject { ["component"] = Tree("metadata") };

        var result = Enrich(bom);
        Assert.Equal(new ComponentCounts(10, 1, 1, 0), result.Counts);
        Assert.Empty(result.Warnings);
        var output = (JsonObject)JsonNode.Parse(result.Output)!;
        Assert.Equal(version, output["specVersion"]!.GetValue<string>());
        Assert.Equal(9, output["version"]!.GetValue<int>());
        Assert.Equal(result.Output, enricher.Enrich(result.Output).Output);
        var added = ComponentWalker.Find(output).Where(x => x.Component["purl"]?.GetValue<string>() == ExpectedPurl).ToArray();
        Assert.Equal(10, added.Length);
        foreach (var (component, _) in added)
        {
            Assert.Equal("7.9", component["version"]!.GetValue<string>());
            component.Remove("purl");
        }
        Assert.True(JsonNode.DeepEquals(bom, output));
    }

    [Theory]
    [InlineData("1.2", false)]
    [InlineData("1.3", false)]
    [InlineData("1.4", false)]
    [InlineData("1.5", true)]
    [InlineData("1.6", true)]
    public void UsesEachVersionsBomVersionRequirement(string version, bool accepted)
    {
        var bom = Bom(version, Component());
        bom.Remove("version");
        if (accepted) Assert.Equal(1, Enrich(bom).Counts.Added);
        else Assert.Contains($"CycloneDX {version} schema validation failed", Assert.Throws<FormatException>(() => Enrich(bom)).Message);
    }

    [Theory]
    [InlineData("1.2", false)]
    [InlineData("1.3", false)]
    [InlineData("1.4", true)]
    [InlineData("1.5", true)]
    [InlineData("1.6", true)]
    public void UsesEachVersionsComponentVersionRequirement(string version, bool accepted)
    {
        var bom = Bom(version, EnrichmentTests.Component());
        if (accepted) Assert.Equal(1, Enrich(bom).Counts.Added);
        else Assert.Contains($"CycloneDX {version} schema validation failed", Assert.Throws<FormatException>(() => Enrich(bom)).Message);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.3")]
    public void PreservesPermittedExtensionsWithoutTreatingThemAsNewerComponentLocations(string version)
    {
        var bom = Bom(version, Component());
        bom["$schema"] = "https://invalid.example/schema.json";
        bom["declarations"] = "unrelated vendor declaration";
        bom["annotations"] = new JsonArray(new JsonObject
        {
            ["annotator"] = new JsonObject { ["component"] = Component("extension-annotation") }
        });
        bom["formulation"] = new JsonArray(new JsonObject { ["components"] = new JsonArray(Component("extension-formula")) });
        var result = Enrich(bom);
        Assert.Equal(1, result.Counts.Added);
        var output = (JsonObject)JsonNode.Parse(result.Output)!;
        ((JsonObject)output["components"]![0]!).Remove("purl");
        Assert.True(JsonNode.DeepEquals(bom, output));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.3")]
    [InlineData("1.4")]
    [InlineData("1.5")]
    [InlineData("1.6")]
    public void ResolvesSpdxReferenceLocallyForEveryVersion(string version)
    {
        var component = Component();
        component["licenses"] = new JsonArray(new JsonObject
        {
            ["license"] = new JsonObject { ["id"] = "MIT" }
        });
        var bom = Bom(version, component);
        Assert.Equal(1, Enrich(bom).Counts.Added);
        component["licenses"]![0]!["license"]!["id"] = "not-an-spdx-license";
        Assert.Contains($"CycloneDX {version} schema validation failed", Assert.Throws<FormatException>(() => Enrich(bom)).Message);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1")]
    [InlineData("1.7")]
    [InlineData("2.0")]
    [InlineData("1.2.0")]
    [InlineData("1.20")]
    [InlineData("v1.2")]
    [InlineData(" 1.2")]
    public void RejectsVersionsOutsideTheExactSupportedSet(string version) =>
        Assert.Contains("/specVersion:", Assert.Throws<FormatException>(() => Enrich(Bom(version, Component()))).Message);

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.3")]
    [InlineData("1.4")]
    [InlineData("1.5")]
    [InlineData("1.6")]
    public void ExistingPurlOnlyDocumentsRemainByteForByteUnchanged(string version)
    {
        var component = Component();
        component["purl"] = "";
        component["cpe"] = "bad-cpe";
        var bytes = Encoding.UTF8.GetBytes("  " + Bom(version, component).ToJsonString() + "\r\n");
        var result = enricher.Enrich(bytes);
        Assert.Equal(bytes, result.Output);
        Assert.Equal(new ComponentCounts(0, 1, 0, 0), result.Counts);
        Assert.Empty(result.Warnings);
    }
}
