using System.Diagnostics;
using System.Security.Cryptography;
using DiffPlex.DiffBuilder.Model;
using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer.Tests;

public sealed class GitReaderTests : IDisposable
{
    private readonly string repo = Path.Combine(Path.GetTempPath(), "DiffViewer тест Grüße " + Guid.NewGuid());
    private readonly GitReader reader = new();

    public GitReaderTests()
    {
        Directory.CreateDirectory(repo);
        Git("init", "-q");
        Git("config", "user.name", "DiffViewer Tests");
        Git("config", "user.email", "diffviewer@example.invalid");
        Git("config", "core.autocrlf", "false");
    }

    [Fact]
    public async Task PartiallyStagedFileReadsThreeDistinctVersionsWithoutWriting()
    {
        const string path = "Forms/Модуль [Grüße].frm";
        Put(path, "HEAD\r\n");
        Commit();
        Put(path, "INDEX\r\n");
        Git("add", "--", path);
        Put(path, "WORKING TREE\r\n");
        File.SetLastWriteTimeUtc(FullPath(path), DateTime.UtcNow.AddDays(-2));

        var indexTime = File.GetLastWriteTimeUtc(FullPath(".git/index"));
        var files = Snapshot();
        var staged = await Read(path, "staged");
        Assert.Equal(("HEAD", "INDEX"), (staged.Old.Label, staged.New.Label));
        Assert.Equal(("HEAD\r\n", "INDEX\r\n"), Texts(staged));

        var unstaged = await Read(path, "unstaged");
        Assert.Equal(("INDEX", "WORKING TREE"), (unstaged.Old.Label, unstaged.New.Label));
        Assert.Equal(("INDEX\r\n", "WORKING TREE\r\n"), Texts(unstaged));
        using var editor = new StringWriter();
        EditorHunks.Write(editor, unstaged, ComparisonText.Decode(unstaged, null), dark: false);
        Assert.Contains("\tWORKING TREE\n", editor.ToString());
        Assert.Contains("INDEX", editor.ToString());
        Assert.DoesNotContain("HEAD", editor.ToString());
        Assert.Equal(files, Snapshot());
        Assert.Equal(indexTime, File.GetLastWriteTimeUtc(FullPath(".git/index")));
        Assert.False(File.Exists(FullPath(".git/index.lock")));
    }

    [Fact]
    public async Task ReadsAnUntrackedFileAndAStagedAdditionInAnUnbornRepo()
    {
        const string path = "Sub Folder/Новый.bas";
        Put(path, "new\n");
        Assert.Equal((null, "new\n"), Texts(await Read(path, "unstaged")));
        Assert.False(File.Exists(FullPath(".git/index")));
        Git("add", "--", path);
        Assert.Equal((null, "new\n"), Texts(await Read(path, "staged")));
    }

    [Fact]
    public async Task DistinguishesDeletionFromAnEmptyFile()
    {
        const string path = "Delete me.cls";
        Put(path, "");
        Commit();
        File.Delete(FullPath(path));
        var unstaged = await Read(path, "unstaged");
        Assert.Empty(unstaged.Old.Bytes!);
        Assert.Null(unstaged.New.Bytes);

        Git("add", "-u");
        var staged = await Read(path, "staged");
        Assert.Empty(staged.Old.Bytes!);
        Assert.Null(staged.New.Bytes);
    }

    [Fact]
    public async Task ResolvesTheSourceOfAStagedRenameAndUnstagedEditsAtTheDestination()
    {
        const string oldPath = "Alt.bas";
        const string newPath = "Новое имя.bas";
        const string before = "same\nsame\nsame\nsame\nsame\nsame\nbefore\n";
        const string staged = "same\nsame\nsame\nsame\nsame\nsame\nafter\n";
        Put(oldPath, before);
        Commit();
        Git("mv", oldPath, newPath);
        Put(newPath, staged);
        Git("add", "--", newPath);
        Put(newPath, staged + "unstaged\n");

        var comparison = await Read(newPath, "staged");
        Assert.Equal(oldPath, comparison.Old.Path);
        Assert.Equal(newPath, comparison.New.Path);
        Assert.Equal((before, staged), Texts(comparison));
        Assert.Equal((staged, staged + "unstaged\n"), Texts(await Read(newPath, "unstaged")));
    }

    [Fact]
    public async Task IntentToAddHasNoStagedContent()
    {
        Put("intent.bas", "new\n");
        Git("add", "-N", "intent.bas");
        Assert.Equal((null, null), Texts(await Read("intent.bas", "staged")));
        Assert.Equal(("", "new\n"), Texts(await Read("intent.bas", "unstaged")));
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("unstaged")]
    public async Task UnchangedFileHasNoChangedRows(string mode)
    {
        Put("same.cls", "same\r\n");
        Commit();
        var versions = await Read("same.cls", mode);
        Assert.Equal(("same\r\n", "same\r\n"), Texts(versions));
        var diff = TextDiff.Build(ComparisonText.Decode(versions, null));
        Assert.Equal(ChangeType.Unchanged, Assert.Single(diff.OldText.Lines).Type);
        Assert.Equal(ChangeType.Unchanged, Assert.Single(diff.NewText.Lines).Type);
    }

    [Fact]
    public async Task ReturnsOriginalBlobBytesAndReadsBinaryAttributes()
    {
        var raw = new byte[] { 0xff, 0x18, 0x00, 0x0d, 0x0a };
        File.WriteAllBytes(FullPath("binary.dat"), raw);
        Put(".gitattributes", "*.dat -diff\n");
        Git("add", ".");
        var versions = await Read("binary.dat", "staged");
        Assert.Equal(raw, versions.New.Bytes);
        Assert.True(versions.BinaryAttribute);
        Assert.True(ComparisonText.Decode(versions, null).Binary);
    }

    [Fact]
    public async Task StagedAndWorkingTreeUseTheirOwnByteRepresentations()
    {
        Git("config", "core.autocrlf", "true");
        Put("normalized.frm", "eins\r\nzwei\r\n");
        Git("add", ".");
        var versions = await Read("normalized.frm", "unstaged");
        Assert.Equal("eins\nzwei\n"u8.ToArray(), versions.Old.Bytes);
        Assert.Equal("eins\r\nzwei\r\n"u8.ToArray(), versions.New.Bytes);
    }

    private Task<FileComparison> Read(string path, string mode) =>
        reader.ReadAsync(LaunchOptions.Parse(["--repo", repo, "--file", path, "--mode", mode]));

    private static (string?, string?) Texts(FileComparison comparison)
    {
        var text = ComparisonText.Decode(comparison, null);
        return (text.Old?.Text, text.New?.Text);
    }

    private KeyValuePair<string, string>[] Snapshot() =>
        Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => KeyValuePair.Create(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToArray();

    private string FullPath(string file) => Path.Combine(repo, file);

    private void Put(string file, string content)
    {
        var path = FullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Commit()
    {
        Git("add", ".");
        Git("commit", "-qm", "Initial version");
    }

    private void Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git.exe")
        {
            WorkingDirectory = repo,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var git = Process.Start(start)!;
        var errors = git.StandardError.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, errors);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(repo, recursive: true);
    }
}
