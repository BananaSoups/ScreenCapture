using Microsoft.Extensions.DependencyInjection;
using ScreenCapture.Platform;
using ScreenCapture.Platform.Linux.X11;

namespace ScreenCapture.Platform.Linux;

public sealed class LinuxPlatformServiceRegistrar
    : IPlatformServiceRegistrar
{
    public void RegisterServices(
        IServiceCollection services)
    {
        services.AddSingleton<IScreenCaptureService>(
            _ => new LinuxScreenCaptureService(
                new X11ScreenCaptureBackend()));

        services.AddSingleton<IClipboardService>(
            _ => new X11ClipboardService());
    }
}