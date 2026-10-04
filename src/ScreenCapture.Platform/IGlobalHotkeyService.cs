namespace ScreenCapture.Platform;

public interface IGlobalHotkeyService
{
    Task InitializeAsync();

    Task RegisterAsync(
        string accelerator,
        Func<Task> callback);

    Task UnregisterAllAsync();

    Task DisposeAsync();
}