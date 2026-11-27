using DiffPlex.DiffBuilder.Model;

namespace Modernizr.DiffViewer.Core;

public enum InlineChange { Context, Removed, Added }

public readonly record struct DiffRun(int Start, int End, bool Changed);
public sealed record InlineRow(DiffPiece? Left, DiffPiece? Right, InlineChange Change)
{
    public DiffPiece Text => (Right ?? Left)!;
}

public static class DiffRows
{
    public static IEnumerable<DiffRun> Runs(SideBySideDiffModel model)
    {
        var left = model.OldText.Lines;
        var right = model.NewText.Lines;
        for (var start = 0; start < left.Count;)
        {
            var changed = IsChanged(left[start], right[start]);
            var end = start + 1;
            while (end < left.Count && changed == IsChanged(left[end], right[end]))
                end++;
            yield return new(start, end, changed);
            start = end;
        }
    }

    public static IEnumerable<InlineRow> Inline(SideBySideDiffModel model, DiffRun run)
    {
        var left = model.OldText.Lines;
        var right = model.NewText.Lines;
        if (!run.Changed)
        {
            for (var i = run.Start; i < run.End; i++)
                yield return new(left[i], right[i], InlineChange.Context);
            yield break;
        }

        for (var i = run.Start; i < run.End; i++)
            if (left[i].Position is not null)
                yield return new(left[i], null, InlineChange.Removed);
        for (var i = run.Start; i < run.End; i++)
            if (right[i].Position is not null)
                yield return new(null, right[i], InlineChange.Added);
    }

    public static string UnchangedNotice(FileComparison versions, SideBySideDiffModel model) => (versions.Old.Bytes, versions.New.Bytes) switch
    {
        (null, null) => "The file is absent from both versions.",
        (null, _) => "Empty file added.",
        (_, null) => "Empty file deleted.",
        _ when model.OldText.Lines.Zip(model.NewText.Lines).Any(pair => pair.First.Text != pair.Second.Text) =>
            "VB6 code matches after ignoring capitalization and line numbers.",
        var (left, right) when !left.AsSpan().SequenceEqual(right) =>
            "Text lines match. Encoding, byte order mark or line endings differ.",
        _ when versions.Old.Path != versions.New.Path => "File renamed. Content is unchanged.",
        _ => "No content changes."
    };

    private static bool IsChanged(DiffPiece left, DiffPiece right) =>
        left.Type != ChangeType.Unchanged || right.Type != ChangeType.Unchanged;
}
