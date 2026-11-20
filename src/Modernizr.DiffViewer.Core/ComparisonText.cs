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
        return new(old, current, binary);
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
        Validate(lines.Chunk(oldText));
        Validate(lines.Chunk(newText));
        return new SideBySideDiffBuilder(Differ.Instance, lines, new BoundedWordChunker())
            .BuildDiffModel(oldText, newText, ignoreWhitespace: false, ignoreCase: false);
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
