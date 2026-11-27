using DiffPlex;

namespace Modernizr.DiffViewer.Core;

public sealed class EditorMerge
{
    private sealed record Edit(int Start, int End, string[] Lines, bool FromDisk);
    private sealed record Section(string[] Common, Conflict? Conflict);
    public sealed record Conflict(int BaseLine, string[] Base, string[] Editor, string[] Disk);
    private readonly List<Section> sections = [];
    private readonly List<Conflict> conflicts = [];
    public IReadOnlyList<Conflict> Conflicts => conflicts;

    public static EditorMerge Compare(string original, string editor, string disk)
    {
        var baseline = Lines(original);
        var ours = Lines(editor);
        var theirs = Lines(disk);
        var edits = Edits(original, editor, ours, false).Concat(Edits(original, disk, theirs, true))
            .OrderBy(edit => edit.Start).ToArray();
        var merge = new EditorMerge();
        var position = 0;
        for (var index = 0; index < edits.Length;)
        {
            var start = edits[index].Start;
            var end = edits[index].End;
            var first = index++;
            // An insertion at the end of a replacement can follow it. An
            // insertion at its start or within it needs a common decision.
            while (index < edits.Length && (edits[index].Start == start || edits[index].Start < end))
                end = Math.Max(end, edits[index++].End);

            merge.sections.Add(new(baseline[position..start], null));
            var group = edits[first..index];
            var editorEdits = group.Where(edit => !edit.FromDisk).ToArray();
            var diskEdits = group.Where(edit => edit.FromDisk).ToArray();
            var editorSide = Apply(baseline, start, end, editorEdits);
            var diskSide = Apply(baseline, start, end, diskEdits);
            if (editorEdits.Length == 0 || diskEdits.Length == 0 || editorSide.SequenceEqual(diskSide))
                merge.sections.Add(new(editorEdits.Length == 0 ? diskSide : editorSide, null));
            else
            {
                var conflict = new Conflict(start + 1, baseline[start..end], editorSide, diskSide);
                merge.conflicts.Add(conflict);
                merge.sections.Add(new([], conflict));
            }
            position = end;
        }
        merge.sections.Add(new(baseline[position..], null));
        return merge;
    }

    public string Resolve(IReadOnlyList<bool> takeDisk)
    {
        if (takeDisk.Count != Conflicts.Count)
            throw new ArgumentException("Choose a version for every conflict.", nameof(takeDisk));
        var index = 0;
        return string.Join("\r\n", sections.SelectMany(section => section.Conflict is { } conflict
            ? (takeDisk[index++] ? conflict.Disk : conflict.Editor)
            : section.Common));
    }

    private static string[] Apply(string[] baseline, int start, int end, Edit[] edits)
    {
        var lines = new List<string>();
        foreach (var edit in edits)
        {
            lines.AddRange(baseline[start..edit.Start]);
            lines.AddRange(edit.Lines);
            start = edit.End;
        }
        lines.AddRange(baseline[start..end]);
        return lines.ToArray();
    }

    private static IEnumerable<Edit> Edits(string original, string version, string[] lines, bool fromDisk) =>
        Differ.Instance.CreateDiffs(original, version, false, false, EditorLines.Instance).DiffBlocks
            .Select(block => new Edit(block.DeleteStartA, block.DeleteStartA + block.DeleteCountA,
                lines[block.InsertStartB..(block.InsertStartB + block.InsertCountB)], fromDisk));

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.Split("\r\n", StringSplitOptions.None);

    private sealed class EditorLines : IChunker
    {
        public static readonly EditorLines Instance = new();
        public IReadOnlyList<string> Chunk(string text) => Lines(text);
    }
}
