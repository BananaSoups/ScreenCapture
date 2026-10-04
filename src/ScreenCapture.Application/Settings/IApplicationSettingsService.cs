using ScreenCapture.Core.Models;

namespace ScreenCapture.Application.Settings;

public interface IApplicationSettingsService
{
    ApplicationSettings Settings { get; }

    Task LoadAsync();

    Task SaveAsync();
}