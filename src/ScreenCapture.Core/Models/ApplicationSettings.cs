namespace ScreenCapture.Core.Models;

public sealed class ApplicationSettings
{
    public string CaptureDirectory { get; set; } = string.Empty;

    public bool StartWithSystem { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;

    public bool CopyCaptureToClipboard { get; set; } = true;
}