using System.Windows;
using System.Windows.Controls;
using DiffPlex.DiffBuilder.Model;

namespace Modernizr.DiffViewer;

internal sealed record DisplayRow(DiffPiece? Left, DiffPiece? Right, string Marker = "", string? ContextLabel = null)
{
    public DiffPiece? InlinePiece => Marker == "−" ? Left : Right;
    public string LeftMarker => Left?.Type is ChangeType.Deleted or ChangeType.Modified ? "−" : "";
    public string RightMarker => Right?.Type is ChangeType.Inserted or ChangeType.Modified ? "+" : "";
}

internal sealed class DiffRowTemplateSelector(DataTemplate line, DataTemplate context) : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container) =>
        item is DisplayRow { ContextLabel: not null } ? context : line;
}
