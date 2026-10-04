
using Microsoft.Extensions.DependencyInjection;
using ScreenCapture.Platform;

namespace ScreenCapture.Platform.Linux;

public sealed class LinuxPlatformServiceRegistrar
    : IPlatformServiceRegistrar
{
    public void RegisterServices(
        IServiceCollection services)
    {
        services.AddSingleton<IScreenCaptureService>(
            _ => new LinuxScreenCaptureService(
                new X11.X11ScreenCaptureBackend()));
    }
}