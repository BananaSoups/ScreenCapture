using Microsoft.Extensions.DependencyInjection;
using ScreenCapture.Platform;

namespace ScreenCapture.Platform.Windows;

public sealed class WindowsPlatformServiceRegistrar
    : IPlatformServiceRegistrar
{
    public void RegisterServices(
        IServiceCollection services)
    {
        // Windows platform services will be registered here.
    }
}