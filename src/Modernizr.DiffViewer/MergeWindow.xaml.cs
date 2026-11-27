using System.Windows;
using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer;

internal partial class MergeWindow : Window
{
    private readonly EditorMerge merge;
    private readonly bool?[] choices;
    private int index;
    private bool updating;
    internal string? Result { get; private set; }

    internal MergeWindow(string path, EditorMerge merge)
    {
        InitializeComponent();
        this.merge = merge;
        choices = new bool?[merge.Conflicts.Count];
        FileLabel.Text = path;
        if (choices.Length == 0)
        {
            Status.Text = "Both versions can be combined. The result will open as unsaved code in VB6.";
            Previous.Visibility = Next.Visibility = Versions.Visibility = Visibility.Collapsed;
            Combined.Text = merge.Resolve([]);
            Combined.Visibility = Visibility.Visible;
        }
        else
            ShowConflict();
    }

    private void ShowConflict()
    {
        updating = true;
        var conflict = merge.Conflicts[index];
        Status.Text = $"Conflict {index + 1} of {choices.Length} · original line {conflict.BaseLine} · " +
            $"{choices.Count(choice => choice.HasValue)} resolved";
        Original.Text = Display(conflict.Base);
        Editor.Text = Display(conflict.Editor);
        Disk.Text = Display(conflict.Disk);
        EditorChoice.IsChecked = choices[index] == false;
        DiskChoice.IsChecked = choices[index] == true;
        Previous.IsEnabled = index > 0;
        Next.IsEnabled = index + 1 < choices.Length;
        Apply.IsEnabled = choices.All(choice => choice.HasValue);
        updating = false;
    }

    private static string Display(string[] lines) => lines.Length == 0 ? "⟨No lines⟩" : string.Join("\r\n", lines);

    private void Version_Checked(object sender, RoutedEventArgs args)
    {
        if (updating)
            return;
        choices[index] = sender == DiskChoice;
        ShowConflict();
    }

    private void Previous_Click(object sender, RoutedEventArgs args) { index--; ShowConflict(); }
    private void Next_Click(object sender, RoutedEventArgs args) { index++; ShowConflict(); }

    private void Apply_Click(object sender, RoutedEventArgs args)
    {
        Result = merge.Resolve(choices.Select(choice => choice!.Value).ToArray());
        DialogResult = true;
    }
}
