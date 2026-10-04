using System.Text.Json;
using ScreenCapture.Core.Models;

namespace ScreenCapture.Application.Settings;

public sealed class ApplicationSettingsService
    : IApplicationSettingsService
{
    private readonly string _settingsPath;

    public ApplicationSettings Settings { get; private set; } = new();

    public ApplicationSettingsService()
    {
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "ScreenCapture");

        _settingsPath = Path.Combine(
            configDirectory,
            "settings.json");
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            Settings = new ApplicationSettings();

            await SaveAsync();

            return;
        }

        try
        {
            await using var stream =
                File.OpenRead(_settingsPath);

            Settings =
                await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                    stream)
                ?? new ApplicationSettings();

            await SaveAsync();
        }
        catch
        {
            Settings = new ApplicationSettings();

            await SaveAsync();
        }
    }

    public async Task SaveAsync()
    {
        var directory =
            Path.GetDirectoryName(_settingsPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream =
            File.Create(_settingsPath);

        await JsonSerializer.SerializeAsync(
            stream,
            Settings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
    }
}
