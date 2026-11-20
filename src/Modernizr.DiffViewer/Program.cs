using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Inspect option names only. A file can itself be named "--format".
        for (var index = 0; index < args.Length; index += 2)
            if (args[index] == "--format")
                return ExportInline(args);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static int ExportInline(string[] args)
    {
        try
        {
            var options = LaunchOptions.Parse(args);
            var settings = ViewerSettings.Load();
            var versions = new GitReader().ReadAsync(options).GetAwaiter().GetResult();
            var text = ComparisonText.Decode(versions, options.Encoding ?? settings?.FallbackEncoding);
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
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
