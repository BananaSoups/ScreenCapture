using Microsoft.Extensions.DependencyInjection;

namespace ScreenCapture.Platform;

public interface IPlatformServiceRegistrar
{
    void RegisterServices(IServiceCollection services);
}