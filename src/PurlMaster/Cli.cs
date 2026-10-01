using System.Text;
using System.Text.Json;

namespace PurlMaster;

public static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        CommandLineOptions options;
        string input;
        string destination;
        bool directory;
        try
        {
            options = CommandLineOptions.Parse(args);
            if (options.Help) { output.WriteLine(CommandLineOptions.Usage); return 0; }
            input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Input!));
            if (!Path.Exists(input)) { error.WriteLine($"ERROR {input}: Input does not exist."); return 1; }
            if (FileOperations.IsLink(input)) { error.WriteLine($"ERROR {input}: Linked input paths are not processed."); return 1; }
            directory = Directory.Exists(input);
            if (!directory && options.Recursive) throw new ArgumentException("--recursive requires directory input.");
            var defaultOutput = directory
                ? input + ".purl"
                : Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + ".purl.json");
            if (directory && options.Output is null && !options.InPlace && input == Path.GetPathRoot(input))
                throw new ArgumentException("Root-directory input requires an explicit output path.");
            destination = options.InPlace ? input : Path.GetFullPath(options.Output ?? defaultOutput);
            if (!options.InPlace)
            {
                var canonicalInput = FileOperations.CanonicalPath(input);
                var canonicalOutput = FileOperations.CanonicalPath(destination);
                if (directory ? FileOperations.IsSameOrInside(canonicalOutput, canonicalInput)
                    : canonicalOutput.Equals(canonicalInput, FileOperations.PathComparison))
                    throw new ArgumentException("Output must be separate from input; use --in-place to rewrite input files.");
                if (directory && File.Exists(destination)) throw new ArgumentException("Directory input requires an output directory.");
                if (!directory && Directory.Exists(destination)) throw new ArgumentException("File input requires an output file path.");
            }
        }
        catch (ArgumentException ex)
        {
            error.WriteLine("ERROR " + ex.Message);
            error.WriteLine(CommandLineOptions.Usage);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("ERROR " + ex.Message);
            return 1;
        }

        var discovery = directory ? FileOperations.Discover(input, options.Recursive)
            : (Files: new List<string> { input }, Errors: new List<string>());
        foreach (var issue in discovery.Errors) error.WriteLine("ERROR " + issue);
        if (discovery.Files.Count == 0)
        {
            error.WriteLine($"ERROR {input}: No JSON files found.");
            return 1;
        }

        var enricher = new BomEnricher(new BomValidator());
        var counts = new ComponentCounts(0, 0, 0, 0);
        var succeeded = 0;
        var failed = discovery.Errors.Count;
        foreach (var file in discovery.Files)
        {
            try
            {
                if (FileOperations.IsLink(file)) throw new IOException("Input became a symbolic link/reparse point.");
                var bytes = File.ReadAllBytes(file);
                var result = enricher.Enrich(bytes);
                foreach (var warning in result.Warnings) error.WriteLine($"WARNING {file} {warning}");
                var target = options.InPlace ? file : directory
                    ? Path.Combine(destination, Path.GetRelativePath(input, file)) : destination;
                if (!options.InPlace || result.Counts.Added > 0)
                {
                    // Check each mirrored path as well as the output root, including existing linked ancestors.
                    if (!options.InPlace && directory && FileOperations.IsSameOrInside(
                        FileOperations.CanonicalPath(target), FileOperations.CanonicalPath(input)))
                        throw new IOException("Mirrored output resolves inside the input directory.");
                    FileOperations.WriteAtomically(target, result.Output, options.InPlace || options.Overwrite,
                        options.InPlace ? file : null, options.InPlace ? bytes : null);
                }
                counts += result.Counts;
                succeeded++;
                output.WriteLine($"OK {file} -> {target}: added={result.Counts.Added}, existing={result.Counts.Existing}, without-cpe={result.Counts.WithoutCpe}, skipped={result.Counts.Skipped}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException or DecoderFallbackException)
            {
                failed++;
                error.WriteLine($"ERROR {file}: {ex.Message}");
            }
        }
        output.WriteLine($"Summary: succeeded={succeeded}, failed={failed}, added={counts.Added}, existing={counts.Existing}, without-cpe={counts.WithoutCpe}, skipped={counts.Skipped}");
        return failed > 0 ? 1 : 0;
    }
}
