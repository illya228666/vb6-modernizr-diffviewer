namespace Modernizr.DiffViewer.Core;

internal readonly record struct Vb6Code(string Original, string Key, int BodyOffset, bool Numbered)
{
    private static readonly string[] ModuleExtensions = [".bas", ".cls", ".frm", ".ctl", ".pag", ".dsr", ".dob"];

    public static bool IsModulePath(string path) =>
        ModuleExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static int StartOfCode(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].StartsWith("Attribute VB_Name = ", StringComparison.OrdinalIgnoreCase))
                return i + 1;
        return 0;
    }

    public static Vb6Code[] Read(IReadOnlyList<string> lines)
    {
        var code = new Vb6Code[lines.Count];
        var start = StartOfCode(lines);
        var continued = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var original = lines[i];
            if (i < start)
            {
                code[i] = new(original, original, 0, false);
                continue;
            }

            var indent = SkipSpace(original, 0);
            var endOfLabel = continued ? indent : SkipNumber(original, indent);
            var offset = SkipSpace(original, endOfLabel);
            var key = FoldCode(original[offset..], out continued);
            code[i] = new(original, key, offset, endOfLabel > indent);
        }
        return code;
    }

    private static int SkipNumber(string line, int start)
    {
        var i = start;
        while (i < line.Length && char.IsAsciiDigit(line[i]))
            i++;
        if (i == start || i - start > 5 ||
            (i < line.Length && line[i] is not (' ' or '\t' or ':')))
            return start;
        i = SkipSpace(line, i);
        return i < line.Length && line[i] == ':' ? i + 1 : i;
    }

    private static string FoldCode(string text, out bool continued)
    {
        // Keep the key the same length as the code so word spans map back to
        // the exact saved line, including its original spelling and label.
        var key = text.ToCharArray();
        var quoted = false;
        var statementStart = true;
        var continuation = text.AsSpan().TrimEnd(" \t").Length - 1;
        continued = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
                quoted = !quoted; // Doubled quotes toggle twice.
            if (quoted)
                continue;
            if (c == '\'')
                break;
            if (statementStart && text.AsSpan(i).StartsWith("Rem", StringComparison.OrdinalIgnoreCase) &&
                (i + 3 == text.Length || text[i + 3] is ' ' or '\t'))
            {
                "REM".CopyTo(0, key, i, 3);
                break;
            }
            key[i] = char.ToUpperInvariant(c);
            if (c is not (' ' or '\t'))
                statementStart = c == ':';
            continued |= i == continuation && c == '_' && i > 0 && (text[i - 1] is ' ' or '\t');
        }
        return new(key);
    }

    private static int SkipSpace(string line, int i)
    {
        while (i < line.Length && line[i] is ' ' or '\t')
            i++;
        return i;
    }
}
