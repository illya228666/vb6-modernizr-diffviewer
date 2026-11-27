using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Modernizr.DiffViewer.Core;
using Xunit.Abstractions;

namespace Modernizr.DiffViewer.Tests;

public sealed class InlineRtfTests(ITestOutputHelper output)
{
    [Fact]
    public void NumberedEditorHunkReturnsOriginalWorkingTreeContextAndWordOffset()
    {
        const string old = "95    Debug.Print \"BEFORE \" & retryLimit\n100    Debug.Print \"separator\"\n";
        const string current = "100    DEBUG.PRINT \"AFTER \" & RetryLimit\n105    Debug.Print \"separator\"\n";
        var parts = ExportEditor(old, current);
        Assert.Equal(["1", "0\t1\t1\t1", "19:6\t100    DEBUG.PRINT \"AFTER \" & RetryLimit",
            "105    Debug.Print \"separator\""], parts[2..6]);
        var inline = Export(old, current);
        Assert.Single(inline.Changes);
    }

    [Fact]
    public void EditorPreviewKeepsWordColorsAndExcludesTheDesignerAndHiddenAttributes()
    {
        const string old = "VERSION 5.00\nBegin VB.Form Form1\n Caption = \"A\"\nEnd\n"
            + "Attribute VB_Name = \"Form1\"\nOption Explicit\nPrivate Sub Save()\n"
            + "Attribute Save.VB_Description = \"hidden\"\n\tSaveOrder(id) ' Grüße\nEnd Sub\n";
        var current = old.Replace("Caption = \"A\"", "Caption = \"B\"")
            .Replace("\"hidden\"", "\"updated\"").Replace("(id)", "(orderId)");
        var parts = ExportEditor(old, current, ".frm");

        Assert.Equal([EditorHunks.Signature, "light", "1", "2\t1\t1\t1",
            "Option Explicit", "Private Sub Save()", "11:7\t\tSaveOrder(orderId) ' Grüße", "End Sub"], parts[..8]);
        using var rich = new HiddenRichEdit(parts[8]);
        var preview = rich.Read(0, 100);
        Assert.Contains("INDEX → WORKING TREE\r−  \tSaveOrder(id) ' Grüße", preview);
        Assert.Equal(0xC0BAF3, rich.Background(preview.IndexOf("(id)", StringComparison.Ordinal) + 1));
        Assert.DoesNotContain("Caption", preview);
        Assert.DoesNotContain("Attribute", preview);
    }

    [Theory]
    [InlineData("obsolete\nSave\nEnd Sub\n", "Save\nEnd Sub\n", "0\t0\t2\t1")]
    [InlineData("Save\nEnd Sub\nobsolete\n", "Save\nEnd Sub\n", "2\t0\t0\t1")]
    public void EditorExportIncludesBothKindsOfDeletionBoundary(string old, string current, string expected)
    {
        var parts = ExportEditor(old, current);
        Assert.Equal(["1", expected, "Save", "End Sub"], parts[2..6]);
        using var rich = new HiddenRichEdit(parts[6]);
        Assert.Contains("−  obsolete", rich.Read(0, 100));
    }

    [Fact]
    public void EditorExportHandlesAFileAdditionAndExcludesBinary()
    {
        var added = ExportEditor(null, "Option Explicit\n");
        Assert.Equal(["1", "0\t1\t0\t0", "\tOption Explicit"], added[2..5]);
        using var rich = new HiddenRichEdit(added[5]);
        Assert.Contains("Added lines", rich.Read(0, 100));
        Assert.Equal([EditorHunks.Signature, "light", "0", ""], ExportEditor("old", "new", ".frx"));
    }

    private static string[] ExportEditor(string? old, string? current, string extension = ".bas")
    {
        var versions = new FileComparison(new("INDEX", "Module" + extension, Bytes(old)),
            new("WORKING TREE", "Module" + extension, Bytes(current)), false);
        using var output = new StringWriter();
        EditorHunks.Write(output, versions, ComparisonText.Decode(versions, null), dark: false);
        return output.ToString().Split('\n');
    }

    [Theory]
    [InlineData(false, 0xEEEDFC, 0xC0BAF3, 0xEDF6E8, 0xC0DFA9)]
    [InlineData(true, 0x2F2942, 0x4B4177, 0x2E381A, 0x49632D)]
    public void NativeRichEditImportsColorsAndNavigatesToBothGroups(
        bool dark, int removedLine, int removedWord, int addedLine, int addedWord)
    {
        const string context = @"\path {Контекст}	Grüße 😀";
        var wire = Export($"first old\n{context}\nlast removed\n", $"first new\n{context}\n", dark: dark);
        using var rich = new HiddenRichEdit(wire.Document);
        Assert.Equal(dark ? "dark" : "light", wire.Theme);
        Assert.Equal(2, wire.Changes.Length);

        var first = wire.Changes[0];
        const string old = "           1                −  first old\r";
        const string current = "                        1   +  first new\r";
        Assert.Equal(old + current, rich.Read(first, (old + current).Length));
        Assert.Equal(removedLine, rich.Background(first));
        Assert.Equal(removedWord, rich.Background(first + old.IndexOf("old", StringComparison.Ordinal)));
        Assert.Equal(addedLine, rich.Background(first + old.Length));
        Assert.Equal(addedWord, rich.Background(first + old.Length + current.IndexOf("new", StringComparison.Ordinal)));
        Assert.Contains(context, rich.Read(first, wire.Changes[1] - first));
        Assert.Equal("           3                −  last removed", rich.Read(wire.Changes[1], 43));
    }

    [Fact]
    public void AdditionAtStartAndEndKeepsBothContextLineNumbers()
    {
        var wire = Export("middle\n", "first\nmiddle\nlast\n");
        using var rich = new HiddenRichEdit(wire.Document);
        Assert.Equal(2, wire.Changes.Length);
        Assert.Contains("+  first\r           1            2      middle\r",
            rich.Read(wire.Changes[0], wire.Changes[1] - wire.Changes[0]));
        Assert.Equal("+  last", rich.Read(wire.Changes[1] + 28, 7));
    }

    [Theory]
    [InlineData(null, "", "Empty file added.")]
    [InlineData("", null, "Empty file deleted.")]
    [InlineData("same", "same", "No content changes.")]
    [InlineData("same\r\n", "same\n", "Text lines match.")]
    public void ReportsComparisonsWithNoChangedRows(string? old, string? current, string notice)
    {
        var wire = Export(old, current);
        Assert.Empty(wire.Changes);
        Assert.Contains(notice, wire.Document);
    }

    [Fact]
    public void BinaryHasAnExplanationAndNoRows()
    {
        var wire = Export(null, "new", path: "Form.frx");
        Assert.Empty(wire.Changes);
        Assert.Contains(InlineRtf.BinaryNotice, wire.Document);
    }

    [Fact]
    public void LoadsAFullModuleInOneNativeStream()
    {
        var original = string.Join('\n', Enumerable.Range(1, 12_000).Select(line => $"' Module line {line}"));
        var edited = original.Replace("' Module line 1800", "' EDIT {} Привет 😀", StringComparison.Ordinal) +
            "\n" + new string('x', 20_000);
        var timer = Stopwatch.StartNew();
        var wire = Export(original, edited);
        var exportTime = timer.ElapsedMilliseconds;
        timer.Restart();
        using var rich = new HiddenRichEdit(wire.Document);
        output.WriteLine($"Export: {exportTime} ms, native import: {timer.ElapsedMilliseconds} ms.");
        Assert.Equal(2, wire.Changes.Length);
        Assert.Contains("' EDIT {} Привет 😀", rich.Read(wire.Changes[0], 130));
        Assert.Contains("+  xxx", rich.Read(wire.Changes[1], 44));
    }

    private static (string Theme, int[] Changes, string Document) Export(
        string? old, string? current, string path = "Forms/Тест {Grüße} 😀.frm", bool dark = false)
    {
        var versions = new FileComparison(new("HEAD", path, Bytes(old)), new("INDEX", path, Bytes(current)), false);
        using var output = new StringWriter();
        InlineRtf.Write(output, versions, ComparisonText.Decode(versions, null), dark);
        var parts = output.ToString().Split('\n', 4);
        Assert.Equal(InlineRtf.Signature, parts[0]);
        Assert.All(parts[3], character => Assert.True(character <= '\x7f'));
        return (parts[1], parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(), parts[3]);
    }

    private static byte[]? Bytes(string? text) => text is null ? null : Encoding.UTF8.GetBytes(text);

    // A hidden control exercises the Windows parser and selection offsets
    // without launching the IDE or displaying a window.
    private sealed class HiddenRichEdit : IDisposable
    {
        private readonly nint module = NativeLibrary.Load("Msftedit.dll");
        private readonly nint window;

        public HiddenRichEdit(string rtf)
        {
            try
            {
                window = CreateWindowExW(0, "RICHEDIT50W", "", 0x20000000 | 0x800 | 0x80 | 4,
                    0, 0, 1024, 768, 0, 0, 0, 0);
                Assert.NotEqual(0, window);
                byte[] bytes = Encoding.ASCII.GetBytes(rtf);
                var offset = 0;
                StreamCallback callback = (nint _, nint buffer, int size, out int written) =>
                {
                    written = Math.Min(size, bytes.Length - offset);
                    Marshal.Copy(bytes, offset, buffer, written);
                    offset += written;
                    return 0;
                };
                var stream = new EditStream { Callback = Marshal.GetFunctionPointerForDelegate(callback) };
                SendMessageW(window, 0x435, 0, InlineRtf.MaxDocumentBytes); // EM_EXLIMITTEXT
                Assert.True(SendMessageW(window, 0x449, 2, ref stream) > 0); // EM_STREAMIN, SF_RTF
                GC.KeepAlive(callback);
                Assert.Equal(0, stream.Error);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public string Read(int start, int count)
        {
            var range = new TextRange { Start = start, End = start + count, Buffer = Marshal.AllocHGlobal(2 * (count + 1)) };
            try
            {
                var length = SendMessageW(window, 0x44b, 0, ref range); // EM_GETTEXTRANGE
                return Marshal.PtrToStringUni(range.Buffer, (int)length)!;
            }
            finally { Marshal.FreeHGlobal(range.Buffer); }
        }

        public int Background(int position)
        {
            SendMessageW(window, 0xb1, position, position + 1); // EM_SETSEL
            var format = new CharacterFormat { Size = 116 };
            SendMessageW(window, 0x43a, 1, ref format); // EM_GETCHARFORMAT, SCF_SELECTION
            return format.Background;
        }

        public void Dispose()
        {
            if (window != 0)
                DestroyWindow(window);
            NativeLibrary.Free(module);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct EditStream { public nint Cookie; public int Error; public nint Callback; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextRange { public int Start; public int End; public nint Buffer; }

        [StructLayout(LayoutKind.Explicit, Size = 116)]
        private struct CharacterFormat
        {
            [FieldOffset(0)] public int Size;
            [FieldOffset(96)] public int Background;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint StreamCallback(nint cookie, nint buffer, int capacity, out int written);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern nint CreateWindowExW(int exStyle, string className, string caption, int style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint arg);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern nint SendMessageW(nint window, int message, nint wParam, ref EditStream lParam);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern nint SendMessageW(nint window, int message, nint wParam, ref TextRange lParam);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern nint SendMessageW(nint window, int message, nint wParam, ref CharacterFormat lParam);
    }
}
