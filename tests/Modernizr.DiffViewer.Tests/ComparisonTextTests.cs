using System.Text;
using DiffPlex.DiffBuilder.Model;
using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer.Tests;

public sealed class ComparisonTextTests
{
    [Theory]
    [InlineData("windows-1251", "Привет")]
    [InlineData("windows-1252", "Grüße aus Köln")]
    public void DetectsUtf8AndUsesTheSelectedAnsiFallback(string fallback, string sample)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansi = Encoding.GetEncoding(fallback).GetBytes(sample + "\r\n");
        var utf8 = Encoding.UTF8.GetBytes(sample + "\n");
        var text = ComparisonText.Decode(Compare(ansi, utf8), fallback);
        Assert.False(text.Binary);
        Assert.Equal(new(sample + "\r\n", fallback, "CRLF", true), text.Old);
        Assert.Equal(new(sample + "\n", "utf-8", "LF", true), text.New);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public void DetectsBomBeforeBinaryHeuristics(string name)
    {
        var codec = Encoding.GetEncoding(name);
        const string sample = "Строка\r\nGrüße";
        var bytes = codec.GetPreamble().Concat(codec.GetBytes(sample)).ToArray();
        var text = ComparisonText.Decode(Compare(null, bytes), "windows-1251");
        Assert.False(text.Binary);
        Assert.Equal(sample, text.New!.Text);
        Assert.EndsWith(" BOM", text.New.Encoding, StringComparison.Ordinal);
        Assert.False(text.New.FinalNewline);
    }

    [Theory]
    [InlineData("M.frx")]
    [InlineData("M.res")]
    public void RecognizesVb6BinaryResourcesEvenIfBytesLookLikeText(string path)
    {
        var text = ComparisonText.Decode(Compare(null, "ABCD"u8.ToArray(), path), null);
        Assert.True(text.Binary);
    }

    [Fact]
    public void RecognizesBinaryContentByControlBytes()
    {
        Assert.True(ComparisonText.Decode(Compare(null, [0x89, 0x50, 0x00, 0x03]), null).Binary);
    }

    [Fact]
    public void KeepsLinePositionsAndHighlightsModifiedWords()
    {
        var model = TextDiff.Build(ComparisonText.Decode(Compare(
            "Option Explicit\r\n\r\nPublic Sub First()\r\n"u8.ToArray(),
            "Option Explicit\n\nPublic Sub Zweite()\n"u8.ToArray()), null));
        Assert.Equal([1, 2, 3], model.OldText.Lines.Select(line => line.Position));
        Assert.Equal([1, 2, 3], model.NewText.Lines.Select(line => line.Position));
        Assert.Contains(model.OldText.Lines[2].SubPieces, piece => piece.Type == ChangeType.Deleted);
        Assert.Contains(model.NewText.Lines[2].SubPieces, piece => piece.Type == ChangeType.Inserted);
    }

    [Fact]
    public void BuildsALargeModuleAndKeepsLongLinesIntact()
    {
        var module = string.Join('\n', Enumerable.Range(0, 12_000).Select(index => "' VB6 line " + index)) + "\n";
        var longLine = new string('x', 20_000);
        var model = TextDiff.Build(ComparisonText.Decode(Compare(
            Encoding.UTF8.GetBytes(module), Encoding.UTF8.GetBytes(module + longLine + "\n")), null));
        Assert.Equal(12_001, model.NewText.Lines.Count);
        Assert.Equal(longLine, model.NewText.Lines[^1].Text);
    }

    [Fact]
    public void NormalizesUncArgumentsWithoutTouchingTheShare()
    {
        var options = LaunchOptions.Parse(
            ["--repo", @"\\server\VB Projekte\Репозиторий\", "--file", @"Forms\Grüße.bas", "--mode", "staged"]);
        Assert.Equal(@"\\server\VB Projekte\Репозиторий\", options.Repository);
        Assert.Equal("Forms/Grüße.bas", options.RelativePath);
    }

    [Theory]
    [InlineData("../other.bas")]
    [InlineData("C:/other.bas")]
    [InlineData("forms/.git/config")]
    [InlineData(@"\\server\share\script.bas")]
    [InlineData("forms/.. /other.bas")]
    public void RejectsPathsOutsideTheWorktree(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            LaunchOptions.Parse(["--repo", Path.GetTempPath(), "--file", path, "--mode", "staged"]));
    }

    private static FileComparison Compare(byte[]? old, byte[]? current, string path = "Module.bas") =>
        new(new("INDEX", path, old), new("WORKING TREE", path, current), false);
}
