namespace ScreenCapture.Platform;

public interface ITrayService
{
    Task InitializeAsync();

    void ShowMainWindow();

    void HideMainWindow();

    void ShowNotification(string title, string message);

    Task DisposeAsync();
}