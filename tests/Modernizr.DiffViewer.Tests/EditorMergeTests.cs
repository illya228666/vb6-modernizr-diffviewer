using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer.Tests;

public sealed class EditorMergeTests
{
    [Theory]
    [InlineData("a|b|c", "A|b|c", "a|b|C", "A|b|C")]
    [InlineData("a|b", "a|b|new", "A|b", "A|b|new")]
    [InlineData("a|b|c", "a|c", "a|B|c", null)]
    [InlineData("a|b", "a|X|b", "a|X|b", "a|X|b")]
    [InlineData("a|b", "a|X|b", "a|Y|b", null)]
    [InlineData("a|b", "a|b|X", "a|b|Y", null)]
    [InlineData("", "X", "Y", null)]
    [InlineData("a|b", "a|", "a|b", "a|")]
    [InlineData("a|b", "", "a|b", "")]
    [InlineData("105    Debug.Print Name|Log", "110    debug.Print Name|Log", "105    Debug.Print Name|Log Äж", "110    debug.Print Name|Log Äж")]
    public void CombinesIndependentEditsAndFindsConflicts(string original, string editor, string disk, string? expected)
    {
        var merge = EditorMerge.Compare(Code(original), Code(editor), Code(disk));
        if (expected is null)
            Assert.Single(merge.Conflicts);
        else
        {
            Assert.Empty(merge.Conflicts);
            Assert.Equal(Code(expected), merge.Resolve([]));
        }
    }

    [Fact]
    public void KeepsBothAlternativesForOverlappingDeletions()
    {
        var merge = EditorMerge.Compare(Code("a|b|c|d|e"), Code("a|ED|d|e"), Code("a|b|DISK|e"));
        var conflict = Assert.Single(merge.Conflicts);
        Assert.Equal(["b", "c", "d"], conflict.Base);
        Assert.Equal(["ED", "d"], conflict.Editor);
        Assert.Equal(["b", "DISK"], conflict.Disk);
        Assert.Equal(Code("a|ED|d|e"), merge.Resolve([false]));
        Assert.Equal(Code("a|b|DISK|e"), merge.Resolve([true]));
        Assert.Throws<ArgumentException>(() => merge.Resolve([]));
    }

    [Fact]
    public void ResolvesSeparatedConflictsIndependently()
    {
        var merge = EditorMerge.Compare(Code("a|same|b"), Code("IDE A|same|IDE B"), Code("Disk A|same|Disk B"));
        Assert.Equal(2, merge.Conflicts.Count);
        Assert.Equal(Code("IDE A|same|Disk B"), merge.Resolve([false, true]));
    }

    private static string Code(string text) => text.Replace("|", "\r\n", StringComparison.Ordinal);
}
