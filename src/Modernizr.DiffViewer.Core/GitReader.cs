using System.Diagnostics;
using System.Text;

namespace Modernizr.DiffViewer.Core;

public sealed record FileVersion(string Label, string Path, byte[]? Bytes);
public sealed record FileComparison(FileVersion Old, FileVersion New, bool BinaryAttribute);

public sealed class GitReader
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxMetadataBytes = 16 * 1024 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public async Task<FileComparison> ReadAsync(LaunchOptions options, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            return await ReadComparison(options, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("Reading the Git comparison timed out.");
        }
    }

    private static async Task<FileComparison> ReadComparison(LaunchOptions options, CancellationToken token)
    {
        var repo = options.Repository;
        var path = options.RelativePath;
        var top = await Query(repo, token, "rev-parse", "--show-prefix");
        if (top.TrimEnd('\r', '\n').Length != 0)
            throw new IOException("--repo must point to the Git working tree root.");

        var indexEntries = Records(await Query(repo, token, "ls-files", "--stage", "-z", "--", path))
            .Select(ParseIndexEntry).Where(entry => entry.Path == path).ToArray();
        if (indexEntries.Any(entry => entry.Stage != "0"))
            throw new IOException("This file has unresolved merge stages in INDEX.");

        var indexEntry = indexEntries.SingleOrDefault();
        FileVersion oldVersion;
        FileVersion newVersion;
        if (options.Mode == ComparisonMode.Staged)
        {
            var headResult = await Run(repo, MaxMetadataBytes, token, "rev-parse", "--verify", "--quiet", "HEAD^{commit}");
            var head = headResult.ExitCode == 1 ? null : headResult.Text().TrimEnd('\r', '\n');
            var oldPath = path;
            var headEntry = head is null ? null : await TreeEntry(repo, head, path, token);
            if (headEntry is null && indexEntry is not null)
            {
                // A path filter would hide the rename source.
                var staged = await Query(repo, token, "diff", "--cached", "--name-status", "-z",
                    "--no-ext-diff", "--no-textconv", "--ita-invisible-in-index", "-M", "--");
                var change = FindStagedChange(staged, path);
                if (change.Kind is null)
                    indexEntry = null; // Git add -N registers intent without staged content.
                if (change.Source is not null && head is not null)
                {
                    oldPath = change.Source;
                    headEntry = await TreeEntry(repo, head, oldPath, token);
                }
            }
            oldVersion = await ReadBlob(repo, "HEAD", oldPath, headEntry, token);
            newVersion = await ReadBlob(repo, "INDEX", path, indexEntry, token);
        }
        else
        {
            oldVersion = await ReadBlob(repo, "INDEX", path, indexEntry, token);
            newVersion = new("WORKING TREE", path, await ReadDisk(repo, path, token));
        }

        string[] paths = oldVersion.Path == path ? [path] : [oldVersion.Path, path];
        var attrs = Records(await Query(repo, token, ["check-attr", "-z", "diff", "--", ..paths]));
        return new(oldVersion, newVersion, attrs.Where((_, i) => i % 3 == 2).Contains("unset"));
    }

    private static string[] Records(string text) => text.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private sealed record Entry(string Path, string Mode, string Id, string Stage);

    private static Entry ParseIndexEntry(string line)
    {
        var tab = line.IndexOf('\t');
        var fields = line[..tab].Split(' ');
        return new(line[(tab + 1)..], fields[0], fields[1], fields[2]);
    }

    private static async Task<Entry?> TreeEntry(string repo, string tree, string path, CancellationToken token)
    {
        var rows = Records(await Query(repo, token, "ls-tree", "-z", tree, "--", path));
        foreach (var row in rows)
        {
            var tab = row.IndexOf('\t');
            if (row[(tab + 1)..] != path)
                continue;
            var fields = row[..tab].Split(' ');
            return new(path, fields[0], fields[2], "0");
        }
        return null;
    }

    private static (string? Kind, string? Source) FindStagedChange(string output, string path)
    {
        var parts = Records(output);
        for (var i = 0; i < parts.Length;)
        {
            var kind = parts[i++];
            var source = parts[i++];
            if (kind[0] is 'R' or 'C')
            {
                if (parts[i++] == path)
                    return (kind, kind[0] == 'R' ? source : null);
            }
            else if (source == path)
                return (kind, null);
        }
        return (null, null);
    }

    private static async Task<FileVersion> ReadBlob(string repo, string label, string path, Entry? entry,
        CancellationToken token)
    {
        if (entry is null)
            return new(label, path, null);
        if (entry.Mode is not ("100644" or "100755"))
            throw new IOException($"{label} contains a link, a directory or a submodule at this path.");
        var blob = await Run(repo, MaxFileBytes, token, "cat-file", "blob", entry.Id);
        blob.EnsureSuccess();
        return new(label, path, blob.Output);
    }

    private static async Task<byte[]?> ReadDisk(string repo, string path, CancellationToken token)
    {
        try
        {
            var current = repo;
            FileAttributes attributes = default;
            foreach (var component in path.Split('/'))
            {
                current = Path.Combine(current, component);
                attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("The working tree path contains a symbolic link or reparse point.");
            }
            if (attributes.HasFlag(FileAttributes.Directory))
                throw new IOException("Select a file to compare. The working tree path is a directory.");

            await using var file = File.Open(current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await ReadLimited(file, MaxFileBytes, token);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static async Task<string> Query(string repo, CancellationToken token, params string[] arguments) =>
        (await Run(repo, MaxMetadataBytes, token, arguments)).Text();

    private static async Task<GitResult> Run(string repo, int maximum, CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo("git.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        string[] prefix = ["--no-pager", "--no-optional-locks", "--no-lazy-fetch", "--literal-pathspecs",
            "-c", "core.fsmonitor=false", "-C", repo];
        foreach (var arg in prefix.Concat(arguments))
            start.ArgumentList.Add(arg);
        string[] environmentOverrides = ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR",
            "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_PREFIX", "GIT_EXTERNAL_DIFF",
            "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_ICASE_PATHSPECS"];
        foreach (var key in environmentOverrides)
            start.Environment.Remove(key);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(start) ?? throw new IOException("Could not start git.exe.");
        process.StandardInput.Close();
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(token);
            var bytes = await ReadLimited(process.StandardOutput.BaseStream, maximum, token);
            await process.WaitForExitAsync(token);
            return new(process.ExitCode, bytes, await stderr);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<byte[]> ReadLimited(Stream stream, int maximum, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (bytes.Length + count > maximum)
                throw new IOException($"Comparison exceeds the {maximum / (1024 * 1024)} MiB read limit.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    private sealed record GitResult(int ExitCode, byte[] Output, string Error)
    {
        public void EnsureSuccess()
        {
            if (ExitCode != 0)
                throw new IOException($"Git exited with code {ExitCode}. {Error.Trim()}");
        }

        public string Text()
        {
            EnsureSuccess();
            return Utf8.GetString(Output);
        }
    }
}
