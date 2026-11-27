using System.Text;
using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Modernizr.DiffViewer.Core;

public sealed record DecodedVersion(string Text, string Encoding, string LineEndings, bool FinalNewline);
public sealed record ComparisonText(DecodedVersion? Old, DecodedVersion? New, bool Binary)
{
    public bool VisualBasic { get; init; }
    private static readonly string[] BinaryExtensions =
        [".frx", ".res", ".ctx", ".dsx", ".dox", ".pgx", ".dll", ".exe", ".ocx", ".ico"];
    private static readonly (byte[] Mark, Encoding Codec)[] Bom =
    [
        ([0xff, 0xfe, 0x00, 0x00], new UTF32Encoding(false, true, true)),
        ([0x00, 0x00, 0xfe, 0xff], new UTF32Encoding(true, true, true)),
        ([0xef, 0xbb, 0xbf], new UTF8Encoding(true, true)),
        ([0xff, 0xfe], new UnicodeEncoding(false, true, true)),
        ([0xfe, 0xff], new UnicodeEncoding(true, true, true))
    ];
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Regex Breaks = new(@"\r\n|\r|\n", RegexOptions.CultureInvariant);

    public static ComparisonText Decode(FileComparison versions, string? fallback)
    {
        if (versions.BinaryAttribute || IsBinaryExtension(versions.Old) || IsBinaryExtension(versions.New))
            return new(null, null, true);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codec = int.TryParse(fallback, out var page)
            ? Encoding.GetEncoding(page)
            : fallback is not null
                ? Encoding.GetEncoding(fallback)
                : Encoding.GetEncoding(0);
        var old = DecodeVersion(versions.Old.Bytes, codec);
        var current = DecodeVersion(versions.New.Bytes, codec);
        var binary = (versions.Old.Bytes is not null && old is null) ||
                     (versions.New.Bytes is not null && current is null);
        return new(old, current, binary)
        {
            VisualBasic = Vb6Code.IsModulePath(versions.Old.Path) && Vb6Code.IsModulePath(versions.New.Path)
        };
    }

    private static bool IsBinaryExtension(FileVersion version) =>
        BinaryExtensions.Contains(Path.GetExtension(version.Path), StringComparer.OrdinalIgnoreCase);

    private static DecodedVersion? DecodeVersion(byte[]? bytes, Encoding fallback)
    {
        if (bytes is null)
            return null;

        foreach (var (mark, codec) in Bom)
        {
            if (bytes.AsSpan().StartsWith(mark))
                return Describe(codec.GetString(bytes, mark.Length, bytes.Length - mark.Length), codec.WebName + " BOM");
        }

        // Detect NUL and common binary control bytes across the whole file.
        if (bytes.Any(b => b < 32 && b is not (9 or 10 or 12 or 13 or 26)))
            return null;
        try
        {
            return Describe(Utf8.GetString(bytes), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return Describe(fallback.GetString(bytes), fallback.WebName);
        }
    }

    private static DecodedVersion Describe(string text, string codec)
    {
        var endings = Breaks.Matches(text).Select(match => match.Value).Distinct().ToArray();
        var format = endings.Length switch
        {
            0 => "no line breaks",
            > 1 => "mixed line endings",
            _ => endings[0] switch { "\r\n" => "CRLF", "\n" => "LF", _ => "CR" }
        };
        return new(text, codec, format, text.EndsWith('\n') || text.EndsWith('\r'));
    }
}

public static class TextDiff
{
    public const int MaxLines = 40_000;
    public const int MaxLineLength = 65_536;

    public static SideBySideDiffModel Build(ComparisonText comparison)
    {
        var oldText = comparison.Old?.Text ?? "";
        var newText = comparison.New?.Text ?? "";
        var lines = new FileLineChunker();
        var oldLines = lines.Chunk(oldText);
        var newLines = lines.Chunk(newText);
        Validate(oldLines);
        Validate(newLines);
        if (!comparison.VisualBasic)
            return BuildModel(oldText, newText);

        var left = Vb6Code.Read(oldLines);
        var right = Vb6Code.Read(newLines);
        var model = BuildModel(JoinKeys(left), JoinKeys(right));
        for (var index = 0; index < model.OldText.Lines.Count; index++)
        {
            var before = model.OldText.Lines[index];
            var after = model.NewText.Lines[index];
            if (before.Position is int oldPosition && after.Position is int newPosition)
            {
                var oldLine = left[oldPosition - 1];
                var newLine = right[newPosition - 1];
                var changedIndent = !oldLine.Numbered && !newLine.Numbered &&
                    !oldLine.Original.AsSpan(0, oldLine.BodyOffset)
                        .SequenceEqual(newLine.Original.AsSpan(0, newLine.BodyOffset));
                Restore(before, oldLine, changedIndent ? ChangeType.Deleted : ChangeType.Unchanged);
                Restore(after, newLine, changedIndent ? ChangeType.Inserted : ChangeType.Unchanged);
            }
            else if (before.Position is int deleted)
                before.Text = left[deleted - 1].Original;
            else if (after.Position is int added)
                after.Text = right[added - 1].Original;
        }
        return model;
    }

    private static SideBySideDiffModel BuildModel(string oldText, string newText) =>
        new SideBySideDiffBuilder(Differ.Instance, new FileLineChunker(), new BoundedWordChunker())
            .BuildDiffModel(oldText, newText, ignoreWhitespace: false, ignoreCase: false);

    private static string JoinKeys(Vb6Code[] lines) =>
        lines.Length == 0 ? "" : string.Join('\n', lines.Select(line => line.Key)) + '\n';

    private static void Restore(DiffPiece piece, Vb6Code line, ChangeType prefixType)
    {
        piece.Text = line.Original;
        var offset = line.BodyOffset;
        foreach (var word in piece.SubPieces)
        {
            if (word.Text is null)
                continue;
            word.Text = line.Original.Substring(offset, word.Text.Length);
            offset += word.Text.Length;
        }

        if (prefixType != ChangeType.Unchanged && piece.Type == ChangeType.Unchanged)
        {
            piece.Type = ChangeType.Modified;
            piece.SubPieces.Add(new(line.Original[line.BodyOffset..], ChangeType.Unchanged));
        }
        if (piece.SubPieces.Count > 0 && line.BodyOffset > 0)
            piece.SubPieces.Insert(0, new(line.Original[..line.BodyOffset], prefixType));
    }

    private static void Validate(IReadOnlyList<string> lines)
    {
        if (lines.Count > MaxLines || lines.Any(line => line.Length > MaxLineLength))
            throw new IOException($"Text exceeds {MaxLines:N0} lines or {MaxLineLength:N0} characters per line.");
    }

    private sealed class FileLineChunker : IChunker
    {
        public IReadOnlyList<string> Chunk(string text)
        {
            if (text.Length == 0)
                return [];
            var pieces = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            // A final terminator belongs to the last line.
            return pieces[^1].Length == 0 ? pieces[..^1] : pieces;
        }
    }

    private sealed class BoundedWordChunker : IChunker
    {
        private readonly WordChunker words = new();

        public IReadOnlyList<string> Chunk(string text) => text.Length > 4096 ? [text] : words.Chunk(text);
    }
}
