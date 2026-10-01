namespace PurlMaster;

public sealed record CommandLineOptions(string? Input, string? Output, bool Recursive, bool InPlace, bool Overwrite, bool Help)
{
    public const string Usage = """
        Usage:
          purl-master <file-or-directory> [--output <path>] [--recursive] [--overwrite]
          purl-master <file-or-directory> --in-place [--recursive]
          purl-master --help

        Options:
          -o, --output <path>  Output file or directory (default: sibling .purl output).
          --recursive         Include subdirectories for directory input.
          --in-place          Replace input files atomically; leave unchanged files untouched.
          --overwrite         Allow replacement of existing separate output files.
          -h, --help          Show this help.

        Supports CycloneDX 1.2–1.6 JSON. Existing PURLs and document versions are preserved.
        Exit codes: 0 success, 1 file/discovery failure, 2 invalid arguments.
        """;

    public static CommandLineOptions Parse(string[] args)
    {
        string? input = null;
        string? output = null;
        bool recursive = false, inPlace = false, overwrite = false, help = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i] switch { "-o" => "--output", "-h" => "--help", var value => value };
            if (option.StartsWith('-'))
            {
                if (!seen.Add(option)) throw new ArgumentException($"Repeated option: {option}");
                switch (option)
                {
                    case "--output":
                        if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith('-'))
                            throw new ArgumentException("--output requires a path.");
                        output = args[i];
                        break;
                    case "--recursive": recursive = true; break;
                    case "--in-place": inPlace = true; break;
                    case "--overwrite": overwrite = true; break;
                    case "--help": help = true; break;
                    default: throw new ArgumentException($"Unknown option: {option}");
                }
            }
            else if (input is null && !string.IsNullOrWhiteSpace(option)) input = option;
            else throw new ArgumentException("Exactly one input path is required.");
        }
        if (help && (args.Length != 1)) throw new ArgumentException("--help must be used on its own.");
        if (!help && input is null) throw new ArgumentException("An input file or directory is required.");
        if (inPlace && (output is not null || overwrite))
            throw new ArgumentException("--in-place cannot be combined with --output or --overwrite.");
        return new(input, output, recursive, inPlace, overwrite, help);
    }
}
