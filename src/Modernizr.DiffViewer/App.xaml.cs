using System.IO;
using System.Text.Json;
using System.Windows;
using Modernizr.DiffViewer.Core;

namespace Modernizr.DiffViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var options = LaunchOptions.Parse(e.Args);
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modernizr", "DiffViewer", "settings.json");
            var settings = File.Exists(settingsPath)
                ? JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(settingsPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
            MainWindow = new MainWindow(options, options.Encoding ?? settings?.FallbackEncoding,
                string.Equals(settings?.Theme, "dark", StringComparison.OrdinalIgnoreCase));
            MainWindow.Show();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message + "\n\n" + LaunchOptions.Usage, "Modernizr DiffViewer",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(2);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(ex.Message, "Modernizr DiffViewer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

internal sealed record ViewerSettings(string? FallbackEncoding, string? Theme);
