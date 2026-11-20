using System.Globalization;
using System.Text;
using DiffPlex.DiffBuilder.Model;

namespace Modernizr.DiffViewer.Core;

public static class InlineRtf
{
    public const string Signature = "MODERNIZR-INLINE-RTF-1";
    public const int MaxDocumentBytes = 64 * 1024 * 1024;
    public const string BinaryNotice = "Binary file. Text comparison is unavailable.";

    public static void Write(TextWriter output, FileComparison comparison, ComparisonText text, bool dark)
    {
        var document = new Document(dark);
        var path = comparison.Old.Path == comparison.New.Path
            ? comparison.New.Path
            : $"{comparison.Old.Path} → {comparison.New.Path}";
        document.Header(path, comparison.Old.Label, comparison.New.Label);

        if (text.Binary)
            document.Paragraph(BinaryNotice, @"\cf2");
        else
        {
            var model = TextDiff.Build(text);
            var runs = DiffRows.Runs(model).ToArray();
            if (!runs.Any(run => run.Changed))
                document.Paragraph(DiffRows.UnchangedNotice(comparison), @"\cf2");
            foreach (var run in runs)
            {
                if (run.Changed)
                    document.Changes.Add(document.Position);
                foreach (var row in DiffRows.Inline(model, run))
                    document.Row(row);
            }
        }

        document.Write(output);
    }

    internal static string RemovedPreview(IEnumerable<DiffPiece> lines, string oldLabel, string newLabel, bool dark)
    {
        var document = new Document(dark);
        document.Paragraph($"{oldLabel} → {newLabel}", @"\cf2\b");
        var any = false;
        foreach (var line in lines)
        {
            document.Row(new(line, null, InlineChange.Removed), compact: true);
            any = true;
        }
        if (!any)
            document.Paragraph("Added lines", @"\cf2");
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        document.WriteRtf(output);
        return output.ToString().Replace("\n", "", StringComparison.Ordinal);
    }

    private sealed class Document
    {
        private readonly StringBuilder rtf;
        private readonly bool dark;
        private const int NumberWidth = 12;
        private static readonly string[] Light = ["192538", "4F6177", "FCEDEE", "E8F6ED", "F3BAC0", "A9DFC0"];
        private static readonly string[] Dark = ["E3EAF4", "A8B7CA", "42292F", "1A382E", "77414B", "2D6349"];

        public Document(bool dark)
        {
            this.dark = dark;
            rtf = new(@"{\rtf1\ansi\uc1\deff0{\fonttbl{\f0\fmodern Consolas;}}{\colortbl;");
            foreach (var color in dark ? Dark : Light)
            {
                rtf.Append(@"\red").Append(Convert.ToInt32(color[..2], 16))
                   .Append(@"\green").Append(Convert.ToInt32(color[2..4], 16))
                   .Append(@"\blue").Append(Convert.ToInt32(color[4..], 16)).Append(';');
            }
            rtf.Append(@"}\f0\fs21\cf1 ");
        }

        public List<int> Changes { get; } = [];
        // Unicode Rich Edit represents each \par as one CR and counts UTF-16 code units.
        public int Position { get; private set; }

        public void Header(string path, string oldLabel, string newLabel)
        {
            Paragraph(path, @"\b");
            Paragraph($"{oldLabel} → {newLabel}   ·   Read-only   ·   F7 / Shift+F7 changes   ·   Esc close", @"\cf2");
            Paragraph($"{oldLabel,NumberWidth} {newLabel,NumberWidth}", @"\cf2\b");
        }

        public void Paragraph(string text, string format)
        {
            rtf.Append('{').Append(format).Append(' ');
            AppendText(text);
            rtf.Append('}');
            EndParagraph();
        }

        public void Row(InlineRow row, bool compact = false)
        {
            var (lineColor, wordColor, marker) = row.Change switch
            {
                InlineChange.Removed => (3, 5, "−"),
                InlineChange.Added => (4, 6, "+"),
                _ => (0, 0, " ")
            };
            rtf.Append(@"\highlight").Append(lineColor).Append(@" {\cf2 ");
            var prefix = compact ? $"{marker}  " :
                FormattableString.Invariant($"{row.Left?.Position,NumberWidth} {row.Right?.Position,NumberWidth}   {marker}  ");
            AppendText(prefix);
            rtf.Append('}');

            var piece = row.Text;
            if (piece.SubPieces.Count == 0)
                AppendText(piece.Text ?? "");
            else
            {
                foreach (var word in piece.SubPieces)
                {
                    if (word.Text is null)
                        continue;
                    var color = word.Type == ChangeType.Unchanged ? lineColor : wordColor;
                    rtf.Append(@"\highlight").Append(color).Append(' ');
                    AppendText(word.Text);
                }
            }
            rtf.Append(@"\highlight0");
            EndParagraph();
        }

        private void AppendText(string text)
        {
            foreach (var character in text)
            {
                switch (character)
                {
                    case '\\' or '{' or '}':
                        rtf.Append('\\').Append(character);
                        break;
                    case '\t':
                        rtf.Append(@"\tab ");
                        break;
                    case '\r' or '\n' or '\u2028' or '\u2029':
                        rtf.Append(' ');
                        break;
                    case >= ' ' and <= '~':
                        rtf.Append(character);
                        break;
                    case < ' ' or '\u007f':
                        rtf.Append('?');
                        break;
                    default:
                        rtf.Append(@"\u").Append(unchecked((short)character).ToString(CultureInfo.InvariantCulture)).Append('?');
                        break;
                }
            }
            Position += text.Length;
        }

        private void EndParagraph()
        {
            rtf.Append(@"\par").Append('\n');
            Position++;
            if (rtf.Length > MaxDocumentBytes)
                throw new IOException("Inline document exceeds the 64 MiB display limit.");
        }

        public void Write(TextWriter output)
        {
            output.Write(Signature);
            output.Write('\n');
            output.Write(dark ? "dark\n" : "light\n");
            output.Write(string.Join(",", Changes.Select(position => position.ToString(CultureInfo.InvariantCulture))));
            output.Write('\n');
            WriteRtf(output);
        }

        public void WriteRtf(TextWriter output)
        {
            foreach (var chunk in rtf.GetChunks())
                output.Write(chunk.Span);
            output.Write('}');
        }
    }
}
