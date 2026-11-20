namespace Modernizr.DiffViewer.Core;

public enum ComparisonMode { Staged, Unstaged }

public sealed record LaunchOptions
{
    private LaunchOptions(string repo, string path, ComparisonMode mode, string? encoding) =>
        (Repository, RelativePath, Mode, Encoding) = (repo, path, mode, encoding);

    public string Repository { get; }
    public string RelativePath { get; }
    public ComparisonMode Mode { get; }
    public string? Encoding { get; }

    public const string Usage =
        """Modernizr.DiffViewer.exe --repo "<worktree root>" --file "<relative path>" --mode staged|unstaged [--encoding <name>]""";

    public static LaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i += 2)
        {
            var key = arguments[i];
            if (key is not ("--repo" or "--file" or "--mode" or "--encoding") || i + 1 == arguments.Count ||
                string.IsNullOrWhiteSpace(arguments[i + 1]) || !values.TryAdd(key, arguments[i + 1]))
                throw new ArgumentException($"Invalid argument: {key}");
        }

        var repo = values.GetValueOrDefault("--repo");
        if (repo is null || !Path.IsPathFullyQualified(repo))
            throw new ArgumentException("--repo must be an absolute working tree path.");

        var path = values.GetValueOrDefault("--file")?.Replace('\\', '/');
        if (path is null || path.Split('/').Any(part =>
            part is "" or "." or ".." || part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => c < ' ' || ":\"<>|*?".Contains(c))))
            throw new ArgumentException("--file must be a valid file path relative to the repository.");

        var mode = values.GetValueOrDefault("--mode") switch
        {
            "staged" => ComparisonMode.Staged,
            "unstaged" => ComparisonMode.Unstaged,
            _ => throw new ArgumentException("--mode must be staged or unstaged.")
        };
        return new(Path.GetFullPath(repo), path, mode, values.GetValueOrDefault("--encoding"));
    }
}
