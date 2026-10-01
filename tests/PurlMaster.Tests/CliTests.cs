using System.Text;
using System.Text.Json.Nodes;

namespace PurlMaster.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "purl-master-tests-" + Guid.NewGuid().ToString("N"));

    public CliTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    private (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private string Write(string relative, byte[]? bytes = null)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? EnrichmentTests.Bom(EnrichmentTests.Component()));
        return path;
    }

    [Fact]
    public void SingleFileUsesSeparateDefaultOutputAndDoesNotChangeSource()
    {
        var file = Write("input with spaces.json");
        var original = File.ReadAllBytes(file);
        var result = Run(file);
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Error);
        Assert.Equal(original, File.ReadAllBytes(file));
        var output = Path.Combine(root, "input with spaces.purl.json");
        Assert.NotNull(JsonNode.Parse(File.ReadAllBytes(output))!["components"]![0]!["purl"]);
        Assert.Contains("added=1", result.Output);
    }

    [Fact]
    public void CollisionPreservesSourceAndDestinationUntilExplicitOverwrite()
    {
        var file = Write("input.json");
        var target = Write("output.json", Encoding.UTF8.GetBytes("keep existing output"));
        var sourceBytes = File.ReadAllBytes(file);
        Assert.Equal(1, Run(file, "-o", target).Code);
        Assert.Equal("keep existing output", File.ReadAllText(target));
        Assert.Equal(sourceBytes, File.ReadAllBytes(file));
        Assert.Equal(0, Run(file, "-o", target, "--overwrite").Code);
        Assert.Equal("CycloneDX", JsonNode.Parse(File.ReadAllBytes(target))!["bomFormat"]!.GetValue<string>());
    }

    [Fact]
    public void RecursiveBatchMirrorsFilesAndContinuesAfterValidationFailure()
    {
        Write("input/a.json");
        Write("input/nested/deeper/b.JSON");
        Write("input/nested/deeper/ignored.txt", Encoding.UTF8.GetBytes("ignored"));
        Write("input/bad.json", Encoding.UTF8.GetBytes("{bad"));
        Write("input/other-version.json", Encoding.UTF8.GetBytes("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.1\"}"));
        var destination = Path.Combine(root, "result");
        var result = Run(Path.Combine(root, "input"), "--output", destination, "--recursive");
        Assert.Equal(1, result.Code);
        Assert.Contains("succeeded=2, failed=2, added=2", result.Output);
        Assert.Contains("bad.json", result.Error);
        Assert.Contains("other-version.json", result.Error);
        Assert.True(File.Exists(Path.Combine(destination, "a.json")));
        Assert.True(File.Exists(Path.Combine(destination, "nested/deeper/b.JSON")));
        Assert.Equal(2, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
        Assert.Equal("{bad", File.ReadAllText(Path.Combine(root, "input/bad.json")));
    }

    [Fact]
    public void RecursiveBatchSupportsMixedVersionsAndRetainsEachDocumentsVersion()
    {
        var inputs = new Dictionary<string, byte[]>();
        foreach (var version in new[] { "1.2", "1.3", "1.4", "1.5", "1.6" })
        {
            var bytes = Encoding.UTF8.GetBytes(VersionSupportTests.Bom(version, VersionSupportTests.Component()).ToJsonString());
            inputs.Add(Write($"input/nested/bom-{version}.json", bytes), bytes);
        }
        Write("input/unsupported.json", Encoding.UTF8.GetBytes("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.1\"}"));
        var invalid = VersionSupportTests.Bom("1.2", EnrichmentTests.Component());
        Write("input/invalid-1.2.json", Encoding.UTF8.GetBytes(invalid.ToJsonString()));
        var destination = Path.Combine(root, "output");
        var result = Run(Path.Combine(root, "input"), "--recursive", "--output", destination);
        Assert.Equal(1, result.Code);
        Assert.Contains("succeeded=5, failed=2, added=5", result.Output);
        Assert.Contains("CycloneDX 1.2 schema validation failed", result.Error);
        Assert.False(File.Exists(Path.Combine(destination, "invalid-1.2.json")));
        Assert.False(File.Exists(Path.Combine(destination, "unsupported.json")));
        Assert.Equal(5, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
        foreach (var (path, bytes) in inputs)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
            var output = (JsonObject)JsonNode.Parse(File.ReadAllBytes(Path.Combine(destination, "nested", Path.GetFileName(path))))!;
            Assert.Equal("pkg:generic/vendor/product@1.0?cpe_part=a", output["components"]![0]!["purl"]!.GetValue<string>());
            ((JsonObject)output["components"]![0]!).Remove("purl");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(bytes), output));
        }
    }

    [Fact]
    public void ShallowBatchAndDefaultDirectoryOutputDoNotIncludeNestedFiles()
    {
        Write("input/a.json");
        Write("input/nested/b.json");
        var result = Run(Path.Combine(root, "input"));
        Assert.Equal(0, result.Code);
        Assert.True(File.Exists(Path.Combine(root, "input.purl/a.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "input.purl/nested")));
        Assert.Contains("succeeded=1", result.Output);
    }

    [Fact]
    public void InPlaceEnrichmentIsIdempotentAndNoOpDoesNotRewriteFile()
    {
        var file = Write("input.json");
        Assert.Equal(0, Run(file, "--in-place").Code);
        var enriched = File.ReadAllBytes(file);
        var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, timestamp);
        var result = Run(file, "--in-place");
        Assert.Equal(0, result.Code);
        Assert.Equal(enriched, File.ReadAllBytes(file));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(file));
        Assert.Contains("added=0, existing=1", result.Output);
    }

    [Fact]
    public void NoOpSeparateOutputPreservesOriginalFormattingAndBom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("  {\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\"}\r\n")).ToArray();
        var file = Write("empty.json", bytes);
        Assert.Equal(0, Run(file).Code);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "empty.purl.json")));
    }

    [Fact]
    public void InvalidInPlaceInputRemainsUntouchedAndProducesNoTemporaryFiles()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.1\"}");
        var file = Write("invalid.json", bytes);
        var result = Run(file, "--in-place");
        Assert.Equal(1, result.Code);
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public void SkippedCpeIsReportedAsWarningWithoutFailingTheFile()
    {
        var component = EnrichmentTests.Component();
        component["cpe"] = "malformed";
        var file = Write("input.json", EnrichmentTests.Bom(component));
        var result = Run(file);
        Assert.Equal(0, result.Code);
        Assert.Contains("WARNING", result.Error);
        Assert.Contains("/components/0/cpe", result.Error);
        Assert.Contains("skipped=1", result.Output);
    }

    [Fact]
    public void RejectsSameAndNestedOutputPathsEvenWithOverwrite()
    {
        var file = Write("input/a.json");
        var directory = Path.GetDirectoryName(file)!;
        var bytes = File.ReadAllBytes(file);
        Assert.Equal(2, Run(file, "--output", file, "--overwrite").Code);
        Assert.Equal(2, Run(directory, "--output", directory, "--overwrite").Code);
        Assert.Equal(2, Run(directory, "--output", Path.Combine(directory, "nested/output"), "--recursive").Code);
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.False(Directory.Exists(Path.Combine(directory, "nested")));
    }

    [Fact]
    public void CanonicalPathsDetectLinkedOutputAliases()
    {
        var file = Write("input/a.json");
        var alias = Path.Combine(root, "alias");
        Directory.CreateSymbolicLink(alias, Path.GetDirectoryName(file)!);
        Assert.Equal(2, Run(file, "-o", Path.Combine(alias, "a.json"), "--overwrite").Code);
        Assert.Equal(2, Run(Path.GetDirectoryName(file)!, "-o", Path.Combine(alias, "out")).Code);
    }

    [Fact]
    public void DiscoverySkipsFileLinksDirectoryLinksAndCycles()
    {
        var file = Write("input/a.json");
        var directory = Path.GetDirectoryName(file)!;
        File.CreateSymbolicLink(Path.Combine(directory, "linked.json"), file);
        Directory.CreateSymbolicLink(Path.Combine(directory, "loop"), directory);
        var result = Run(directory, "--recursive");
        Assert.Equal(0, result.Code);
        Assert.Contains("succeeded=1", result.Output);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "input.purl"), "*", SearchOption.AllDirectories));
        Assert.Equal(1, Run(Path.Combine(directory, "linked.json")).Code);
    }

    [Fact]
    public void MirroredLinkedOutputCannotWriteBackIntoSourceTree()
    {
        var file = Write("input/nested/a.json");
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        Directory.CreateSymbolicLink(Path.Combine(output, "nested"), Path.GetDirectoryName(file)!);
        var bytes = File.ReadAllBytes(file);
        var result = Run(Path.Combine(root, "input"), "-o", output, "--recursive", "--overwrite");
        Assert.Equal(1, result.Code);
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Contains("resolves inside", result.Error);
    }

    [Fact]
    public void AtomicWriteDoesNotReplaceConcurrentlyChangedSource()
    {
        var file = Write("source.json", Encoding.UTF8.GetBytes("concurrent edit"));
        Assert.Throws<IOException>(() => FileOperations.WriteAtomically(file, Encoding.UTF8.GetBytes("new"), true,
            file, Encoding.UTF8.GetBytes("old")));
        Assert.Equal("concurrent edit", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(root, ".purl-master-*.tmp"));
    }

    [Fact]
    public void AtomicWriteCleansUpTemporaryFileAfterRenameFailure()
    {
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(destination);
        Assert.Throws<IOException>(() => FileOperations.WriteAtomically(destination, [1, 2, 3], true));
        Assert.True(Directory.Exists(destination));
        Assert.Empty(Directory.GetFiles(root, ".purl-master-*.tmp"));
    }

    [Fact]
    public void EmptyDirectoriesAndMissingInputFail()
    {
        Assert.Equal(1, Run(root).Code);
        Assert.Equal(1, Run(Path.Combine(root, "does-not-exist.json")).Code);
    }

    [Fact]
    public void HelpSucceedsWithoutReadingAnyFiles()
    {
        var result = Run("--help");
        Assert.Equal(0, result.Code);
        Assert.Contains("Usage:", result.Output);
        Assert.Empty(result.Error);
        Assert.Empty(Directory.GetFiles(root));
    }

    public static IEnumerable<object[]> InvalidArguments => new string[][]
    {
        [], ["--unknown"], ["file.json", "another.json"], ["file.json", "--output"],
        ["file.json", "--output", "--recursive"], ["file.json", "--recursive", "--recursive"],
        ["file.json", "--output", "one", "-o", "two"],
        ["file.json", "--in-place", "--output", "other"],
        ["file.json", "--in-place", "--overwrite"], ["--help", "file.json"]
    }.Select(args => new object[] { args });

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void InvalidArgumentsReturnUsageAndExitTwo(string[] args)
    {
        var result = Run(args);
        Assert.Equal(2, result.Code);
        Assert.Contains("Usage:", result.Error);
    }

    [Fact]
    public void RecursiveFlagOnSingleFileIsRejected()
    {
        var file = Write("input.json");
        Assert.Equal(2, Run(file, "--recursive").Code);
    }
}
