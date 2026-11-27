using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--editor-merge")
            return Merge(args);

        // Inspect option names only. A file can itself be named "--format".
        for (var index = 0; index < args.Length; index += 2)
            if (args[index] == "--format")
                return Export(args);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static int Merge(string[] args)
    {
        if (args.Length != 4 || !Path.IsPathFullyQualified(args[1]) ||
            !long.TryParse(args[3], out var owner))
            return 1;

        try
        {
            var job = args[1];
            var encoding = new UnicodeEncoding(false, false, true);
            string Read(string suffix)
            {
                var bytes = File.ReadAllBytes(job + suffix);
                if (bytes.Length > 16 * 1024 * 1024)
                    throw new IOException("The module is too large to merge.");
                return encoding.GetString(bytes);
            }

            var merge = EditorMerge.Compare(Read(".base"), Read(".editor"), Read(".disk"));
            _ = new Application();
            var window = new MergeWindow(args[2], merge);
            new WindowInteropHelper(window).Owner = (nint)owner;
            if (window.ShowDialog() != true || window.Result is null)
                return 2;
            File.WriteAllBytes(job, encoding.GetBytes("MODERNIZR-MERGE-1\r\n" + window.Result));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or Win32Exception)
        {
            return 1;
        }
    }

    private static int Export(string[] args)
    {
        try
        {
            var options = LaunchOptions.Parse(args);
            var settings = ViewerSettings.Load();
            var versions = new GitReader().ReadAsync(options).GetAwaiter().GetResult();
            var text = ComparisonText.Decode(versions, options.Encoding ?? settings?.FallbackEncoding);
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            if (options.Format == "editor-hunks")
                EditorHunks.Write(output, versions, text, settings?.Dark == true);
            else
                InlineRtf.Write(output, versions, text, settings?.Dark == true);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            JsonException or Win32Exception)
        {
            using var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
            error.WriteLine(ex.Message);
            return 1;
        }
    }
}

internal sealed record ViewerSettings(string? FallbackEncoding, string? Theme)
{
    public bool Dark => string.Equals(Theme, "dark", StringComparison.OrdinalIgnoreCase);

    public static ViewerSettings? Load()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Modernizr", "DiffViewer", "settings.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;
    }
}
