using System.Globalization;
using DiffPlex.DiffBuilder.Model;

namespace Modernizr.DiffViewer.Core;

public static class EditorHunks
{
    public const string Signature = "MODERNIZR-EDITOR-HUNKS-1";
    private const int ContextLines = 2;

    public static void Write(TextWriter output, FileComparison comparison, ComparisonText text, bool dark)
    {
        var model = text.Binary ? null : TextDiff.Build(text with { Old = ProjectCode(text.Old), New = ProjectCode(text.New) });
        var runs = model is null ? [] : DiffRows.Runs(model).ToArray();

        output.Write($"{Signature}\n{(dark ? "dark" : "light")}\n");
        output.Write(runs.Count(run => run.Changed).ToString(CultureInfo.InvariantCulture));
        output.Write('\n');
        if (model is null)
            return;

        var right = model.NewText.Lines.Where(line => line.Position is not null).ToArray();
        var consumed = 0;
        foreach (var run in runs)
        {
            var rows = DiffRows.Inline(model, run);
            if (!run.Changed)
            {
                consumed += run.End - run.Start;
                continue;
            }

            var removed = new List<DiffPiece>();
            var added = new List<DiffPiece>();
            foreach (var row in rows)
            {
                if (row.Change == InlineChange.Removed)
                    removed.Add(row.Text);
                else
                    added.Add(row.Text);
            }

            var before = Math.Min(consumed, ContextLines);
            var after = Math.Min(right.Length - consumed - added.Count, ContextLines);
            output.Write(FormattableString.Invariant($"{before}\t{added.Count}\t{after}\t{removed.Count}\n"));
            for (var i = consumed - before; i < consumed; i++)
                WriteLine(output, right[i].Text);
            foreach (var line in added)
            {
                output.Write(WordRanges(line));
                output.Write('\t');
                WriteLine(output, line.Text);
            }
            consumed += added.Count;
            for (var i = consumed; i < consumed + after; i++)
                WriteLine(output, right[i].Text);
            WriteLine(output, InlineRtf.RemovedPreview(removed, comparison.Old.Label, comparison.New.Label, dark));
        }
    }

    private static void WriteLine(TextWriter output, string? value)
    {
        output.Write(value);
        output.Write('\n');
    }

    private static DecodedVersion? ProjectCode(DecodedVersion? version)
    {
        if (version is null)
            return null;

        var lines = version.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        // VB6 exports declare VB_Name between the designer/header and the code.
        // Procedure attributes can appear anywhere after that boundary.
        var code = lines.Skip(Vb6Code.StartOfCode(lines)).Where(line => !IsHiddenAttribute(line));
        return version with { Text = string.Join('\n', code) };
    }

    private static bool IsHiddenAttribute(string line)
    {
        var attribute = line.AsSpan().TrimStart();
        if (!attribute.StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase))
            return false;
        attribute = attribute["Attribute ".Length..].TrimStart();
        var equals = attribute.IndexOf('=');
        if (equals < 1)
            return false;
        var name = attribute[..equals].TrimEnd();
        return name.StartsWith("VB_", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".VB_", StringComparison.OrdinalIgnoreCase);
    }

    private static string WordRanges(DiffPiece line)
    {
        var ranges = new List<string>();
        var offset = 0;
        var start = -1;
        foreach (var word in line.SubPieces)
        {
            if (string.IsNullOrEmpty(word.Text))
                continue;
            if (word.Type == ChangeType.Unchanged)
                Flush();
            else if (start < 0)
                start = offset;
            offset += word.Text.Length;
        }
        Flush();
        return string.Join(',', ranges);

        void Flush()
        {
            if (start < 0)
                return;
            ranges.Add(FormattableString.Invariant($"{start}:{offset - start}"));
            start = -1;
        }
    }
}
