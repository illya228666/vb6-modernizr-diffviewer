using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DiffPlex.DiffBuilder.Model;
using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly LaunchOptions options;
    private readonly string? encoding;
    private readonly CancellationTokenSource closing = new();
    private readonly List<int> changes = [];
    private SideBySideDiffModel? model;
    private int currentChange = -1;
    private int longestLine;
    private bool inline;
    private bool collapsed;
    private bool dark;

    public double HorizontalOffset { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(LaunchOptions options, string? encoding, bool dark)
    {
        this.options = options;
        this.encoding = encoding;
        this.dark = dark;
        InitializeComponent();
        ApplyTheme();

        Title = Path.GetFileName(options.RelativePath) + "  ·  Modernizr DiffViewer";
        FileTitle.Text = options.RelativePath;
        Repository.Text = options.Repository;
        OldLabel.Text = options.Mode == ComparisonMode.Staged ? "HEAD" : "INDEX";
        NewLabel.Text = options.Mode == ComparisonMode.Staged ? "INDEX" : "WORKING TREE";
        Status.Text = options.Mode == ComparisonMode.Unstaged
            ? "Read-only  ·  WORKING TREE shows the saved file on disk"
            : "Read-only  ·  Staged contents";
        Loaded += LoadComparison;
        Closed += (_, _) => closing.Cancel();
    }

    private async void LoadComparison(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadComparison;
        try
        {
            var (versions, text, diff) = await Task.Run(async () =>
            {
                var versions = await new GitReader().ReadAsync(options, closing.Token);
                var text = ComparisonText.Decode(versions, encoding);
                return (versions, text, text.Binary ? null : TextDiff.Build(text));
            }, closing.Token);
            if (closing.IsCancellationRequested)
                return;

            if (versions.Old.Path != versions.New.Path)
                FileTitle.Text = versions.Old.Path + "  →  " + versions.New.Path;

            var labels = (Describe(versions.Old, text.Old), Describe(versions.New, text.New));
            OldLabel.Text = labels.Item1;
            NewLabel.Text = labels.Item2;
            if (text.Binary)
            {
                Notice.Text = InlineRtf.BinaryNotice;
                return;
            }

            model = diff!;
            var sameLines = model.OldText.Lines.All(line => line.Type == ChangeType.Unchanged);
            if (sameLines && model.NewText.Lines.All(line => line.Type == ChangeType.Unchanged))
            {
                Notice.Text = DiffRows.UnchangedNotice(versions);
                if (model.OldText.Lines.Count == 0)
                    return;
            }

            longestLine = model.OldText.Lines.Concat(model.NewText.Lines).Max(ExpandedLength);
            Notice.Visibility = Visibility.Collapsed;
            if (sameLines && model.NewText.Lines.All(line => line.Type == ChangeType.Unchanged))
                Status.Text += "  ·  " + Notice.Text;
            ModeButton.IsEnabled = true;
            RebuildRows();
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Win32Exception ex)
        {
            Notice.Text = $"Could not start Git. Make sure Git for Windows is on PATH.\n{ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Notice.Text = ex.Message;
        }
        finally
        {
            if (closing.IsCancellationRequested)
                closing.Dispose();
        }
    }

    private static string Describe(FileVersion version, DecodedVersion? text)
    {
        if (version.Bytes is null)
            return version.Label + "  ·  Absent";
        if (text is null)
            return version.Label;
        var end = text.Text.Length != 0 && !text.FinalNewline ? ", no final newline" : "";
        return $"{version.Label}  ·  {text.Encoding}, {text.LineEndings}{end}";
    }

    private static int ExpandedLength(DiffPiece piece)
    {
        var length = 0;
        if (piece.Text is not null)
        {
            foreach (var c in piece.Text)
                length += c == '\t' ? 4 - length % 4 : 1;
        }
        return length;
    }

    private void RebuildRows()
    {
        if (model is null)
            return;

        var previousChange = currentChange;
        var rows = new List<DisplayRow>();
        var left = model.OldText.Lines;
        var right = model.NewText.Lines;
        changes.Clear();
        foreach (var run in DiffRows.Runs(model))
        {
            if (run.Changed)
                changes.Add(rows.Count);

            if (!run.Changed && collapsed && run.End - run.Start > 7)
            {
                for (var i = run.Start; i < run.Start + 3; i++)
                    rows.Add(new(left[i], right[i]));
                rows.Add(new(null, null, ContextLabel: $"⋯  {run.End - run.Start - 6:N0} unchanged lines hidden"));
                for (var i = run.End - 3; i < run.End; i++)
                    rows.Add(new(left[i], right[i]));
            }
            else if (inline)
            {
                foreach (var row in DiffRows.Inline(model, run))
                {
                    var marker = row.Change switch
                    {
                        InlineChange.Removed => "−",
                        InlineChange.Added => "+",
                        _ => ""
                    };
                    rows.Add(new(row.Left, row.Right, marker));
                }
            }
            else
            {
                for (var i = run.Start; i < run.End; i++)
                    rows.Add(new(left[i], right[i]));
            }
        }

        Rows.ItemsSource = null;
        Rows.ItemTemplateSelector = new DiffRowTemplateSelector(
            (DataTemplate)Resources[inline ? "InlineRow" : "SideRow"], (DataTemplate)Resources["ContextRow"]);
        Rows.ItemsSource = rows;
        ContextButton.IsEnabled = changes.Count != 0;
        currentChange = -1;
        if (previousChange >= 0 && previousChange < changes.Count)
            Navigate(previousChange);
        else
            UpdateNavigation();
        UpdateHorizontal();
    }

    private void UpdateNavigation()
    {
        ChangeCount.Text = changes.Count == 0 ? "" :
            currentChange < 0 ? $"{changes.Count:N0} changes" : $"{currentChange + 1:N0} / {changes.Count:N0}";
        PreviousButton.IsEnabled = currentChange > 0;
        NextButton.IsEnabled = currentChange + 1 < changes.Count;
    }

    private void Navigate(int index)
    {
        if (index < 0 || index >= changes.Count)
            return;
        currentChange = index;
        var row = changes[index];
        Rows.SelectedIndex = row;
        Rows.UpdateLayout();
        if (VisualTreeHelper.GetChild(Rows, 0) is System.Windows.Controls.Border
            { Child: System.Windows.Controls.ScrollViewer scroll })
            scroll.ScrollToVerticalOffset(row);
        else
            Rows.ScrollIntoView(Rows.Items[row]);
        UpdateNavigation();
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        inline = !inline;
        ModeButton.Content = inline ? "Side-by-side view" : "Inline view";
        RebuildRows();
    }

    private void Context_Click(object sender, RoutedEventArgs e)
    {
        collapsed = !collapsed;
        ContextButton.Content = collapsed ? "Show unchanged" : "Hide unchanged";
        RebuildRows();
    }

    private void Next_Click(object sender, RoutedEventArgs e) => Navigate(currentChange + 1);
    private void Previous_Click(object sender, RoutedEventArgs e) => Navigate(currentChange - 1);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F7)
            return;
        Navigate(currentChange + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1));
        e.Handled = true;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        dark = !dark;
        ApplyTheme();
    }

    private void Rows_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHorizontal();

    private void Horizontal_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        HorizontalOffset = -e.NewValue;
        PropertyChanged?.Invoke(this, new(nameof(HorizontalOffset)));
    }

    private void UpdateHorizontal()
    {
        var font = new Typeface(Rows.FontFamily, Rows.FontStyle, Rows.FontWeight, Rows.FontStretch);
        var glyph = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            font, Rows.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var viewport = Math.Max(0, inline ? Rows.ActualWidth - 150 : (Rows.ActualWidth - 20) / 2 - 75);
        Horizontal.Maximum = Math.Max(0, longestLine * glyph.Width + 12 - viewport);
        Horizontal.ViewportSize = viewport;
        Horizontal.Visibility = longestLine > 0 && Horizontal.Maximum > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyTheme()
    {
        SetBrush("Surface", "#FFFFFF", "#151922");
        SetBrush("Panel", "#F3F6FA", "#1D2430");
        SetBrush("Ink", "#192538", "#E3EAF4");
        SetBrush("Muted", "#4F6177", "#A8B7CA");
        SetBrush("Rule", "#CDD7E4", "#334255");
        SetBrush("Hover", "#E3EDF9", "#303F52");
        SetBrush("Accent", "#2169C4", "#74AEFF");
        SetBrush("AddedLine", "#E8F6ED", "#1A382E");
        SetBrush("RemovedLine", "#FCEDEE", "#42292F");
        SetBrush("AddedWord", "#A9DFC0", "#2D6349");
        SetBrush("RemovedWord", "#F3BAC0", "#77414B");
        SetBrush("Blank", "#F3F5F8", "#1A202B");
        ThemeButton.Content = dark ? "Light theme" : "Dark theme";
    }

    private void SetBrush(string key, string light, string darkColor)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkColor : light));
        brush.Freeze();
        Resources[key] = brush;
    }
}
