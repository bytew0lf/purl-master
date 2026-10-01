namespace PurlMaster;

public static class FileOperations
{
    // Conservatively treat casing aliases as identical on macOS and Windows.
    public static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public static string CanonicalPath(string path) => CanonicalPath(path, 0);

    private static string CanonicalPath(string path, int depth)
    {
        if (depth > 40) throw new IOException("Too many symbolic links while resolving a path.");
        var absolute = Path.GetFullPath(path);
        var root = Path.GetPathRoot(absolute)!;
        var current = root;
        foreach (var segment in absolute[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Path.Exists(current) && IsLink(current))
            {
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                var resolved = info.ResolveLinkTarget(true)?.FullName
                    ?? throw new IOException($"Cannot resolve linked path: {current}");
                // A link's absolute target may itself contain linked parent directories (e.g. /var on macOS).
                current = CanonicalPath(resolved, depth + 1);
            }
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    public static bool IsSameOrInside(string candidate, string directory) =>
        candidate.Equals(directory, PathComparison) ||
        candidate.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, PathComparison) ||
        (directory == Path.GetPathRoot(directory) && candidate.StartsWith(directory, PathComparison));

    public static (List<string> Files, List<string> Errors) Discover(string directory, bool recursive)
    {
        var files = new List<string>();
        var errors = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(current); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{current}: {ex.Message}");
                continue;
            }
            foreach (var entry in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (recursive) pending.Push(entry);
                    }
                    else if (Path.GetExtension(entry).Equals(".json", StringComparison.OrdinalIgnoreCase)) files.Add(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{entry}: {ex.Message}");
                }
            }
        }
        files.Sort(StringComparer.Ordinal);
        return (files, errors);
    }

    public static void WriteAtomically(string destination, byte[] content, bool overwrite,
        string? inputPath = null, byte[]? expectedInput = null)
    {
        if (Path.Exists(destination) && IsLink(destination))
            throw new IOException("Output destination is a symbolic link/reparse point.");
        if (!overwrite && Path.Exists(destination))
            throw new IOException("Output already exists; use --overwrite to replace a separate output file.");

        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".purl-master-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            if (inputPath is not null)
            {
                if (IsLink(inputPath) || !File.ReadAllBytes(inputPath).AsSpan().SequenceEqual(expectedInput))
                    throw new IOException("Input changed while processing; in-place replacement was cancelled.");
                // Preserve executable/read permissions when replacing an existing Unix file.
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(inputPath));
            }
            if (Path.Exists(destination) && IsLink(destination))
                throw new IOException("Output destination became a symbolic link/reparse point.");
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
