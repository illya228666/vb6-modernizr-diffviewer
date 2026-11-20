using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using DiffPlex.DiffBuilder.Model;

namespace Modernizr.DiffViewer;

public sealed class DiffCell : TextBlock
{
    public static readonly DependencyProperty PieceProperty = DependencyProperty.Register(
        nameof(Piece), typeof(DiffPiece), typeof(DiffCell),
        new PropertyMetadata(null, (element, args) => ((DiffCell)element).ShowPiece((DiffPiece?)args.NewValue)));

    public DiffPiece? Piece
    {
        get => (DiffPiece?)GetValue(PieceProperty);
        set => SetValue(PieceProperty, value);
    }

    private void ShowPiece(DiffPiece? piece)
    {
        Inlines.Clear();
        if (piece?.Text is null)
            return;

        var column = 0;
        if (piece.SubPieces.Count == 0)
        {
            Text = ExpandTabs(piece.Text, ref column);
            return;
        }

        foreach (var part in piece.SubPieces)
        {
            if (part.Text is null)
                continue;
            var run = new Run(ExpandTabs(part.Text, ref column));
            if (part.Type != ChangeType.Unchanged)
                run.SetResourceReference(TextElement.BackgroundProperty,
                    part.Type == ChangeType.Deleted ? "RemovedWord" : "AddedWord");
            Inlines.Add(run);
        }
    }

    internal static string ExpandTabs(string text, ref int column)
    {
        if (!text.Contains('\t'))
        {
            column += text.Length;
            return text;
        }
        var expanded = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == '\t')
            {
                var spaces = 4 - column % 4;
                expanded.Append(' ', spaces);
                column += spaces;
            }
            else
            {
                expanded.Append(character);
                column++;
            }
        }
        return expanded.ToString();
    }
}
