using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ScreenCapture.App.Services;
using ScreenCapture.App.ViewModels;
using ScreenCapture.App.Views;
using ScreenCapture.Application.Capture;
using ScreenCapture.Application.Settings;
using ScreenCapture.Application.Storage;
using ScreenCapture.Application.UI;
using ScreenCapture.Platform;
using ScreenCapture.Platform.Linux;
using Avalonia.Threading;

namespace ScreenCapture.App;

public partial class App : Avalonia.Application
{
    private MainWindow? _mainWindow;
    private bool _isExiting;

    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode =
                ShutdownMode.OnExplicitShutdown;

            _mainWindow = new MainWindow
            {
                DataContext =
                    new MainViewModel()
            };

            _mainWindow.Closing +=
                MainWindow_Closing;

            desktop.MainWindow =
                _mainWindow;

            BuildApplicationServices();

            if (_serviceProvider is null)
            {
                throw new InvalidOperationException(
                    "Application services have not been initialized.");
            }

            var settingsService =
                _serviceProvider
                    .GetRequiredService<IApplicationSettingsService>();

            await settingsService.LoadAsync();

            var hotkeyService =
                _serviceProvider
                    .GetRequiredService<IGlobalHotkeyService>();

            await hotkeyService.InitializeAsync();

            await hotkeyService.RegisterAsync(
                settingsService.Settings.Hotkeys.FullScreenCapture,
                async () =>
                {
                    await Dispatcher.UIThread.InvokeAsync(
                        CaptureScreenAsync);
                });

            await hotkeyService.RegisterAsync(
                settingsService.Settings.Hotkeys.RegionCapture,
                async () =>
                {
                    await Dispatcher.UIThread.InvokeAsync(
                        CaptureRegionAsync);
                });

            _mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task CaptureScreenAsync()
    {
        if (_serviceProvider is null)
            return;

        var workflow =
            _serviceProvider
                .GetRequiredService<ScreenCaptureWorkflow>();

        try
        {
            await workflow.CaptureAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Full screen capture failed: {ex}");
        }
    }

    private async Task CaptureRegionAsync()
    {
        if (_serviceProvider is null)
            return;

        var workflow =
            _serviceProvider
                .GetRequiredService<RegionCaptureWorkflow>();

        try
        {
            await workflow.CaptureAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Region capture failed: {ex}");
        }
    }

    private void BuildApplicationServices()
    {
        if (_mainWindow is null)
        {
            throw new InvalidOperationException(
                "Main window has not been created.");
        }

        var services =
            new ServiceCollection();

        services.AddSingleton(_mainWindow);

        services.AddSingleton<CaptureStorageService>();

        services.AddSingleton<
            AvaloniaRegionSelectionService>();

        services.AddSingleton<
            IRegionSelectionService>(
            provider =>
                provider.GetRequiredService<
                    AvaloniaRegionSelectionService>());

        services.AddSingleton<
            IClipboardService,
            AvaloniaClipboardService>();

        services.AddSingleton<
            IApplicationSettingsService,
            ApplicationSettingsService>();

        var platformRegistrar =
            CreatePlatformServiceRegistrar();

        platformRegistrar.RegisterServices(
            services);

        services.AddSingleton<ScreenCaptureWorkflow>();
        services.AddSingleton<RegionCaptureWorkflow>();

        _serviceProvider =
            services.BuildServiceProvider();
    }

    private static IPlatformServiceRegistrar
        CreatePlatformServiceRegistrar()
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxPlatformServiceRegistrar();
        }

        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows platform services are not implemented yet.");
        }

        throw new PlatformNotSupportedException(
            "This operating system is not supported.");
    }

    private void MainWindow_Closing(
        object? sender,
        WindowClosingEventArgs e)
    {
        if (_isExiting)
            return;

        e.Cancel = true;

        _mainWindow?.Hide();
    }

    private async void CaptureRegion_Click(
    object? sender,
    EventArgs e)
    {
        await CaptureRegionAsync();
    }

    private async void CaptureScreen_Click(
        object? sender,
        EventArgs e)
    {
        await CaptureScreenAsync();
    }

    private void OpenScreenCapture_Click(
        object? sender,
        EventArgs e)
    {
        ShowMainWindow();
    }

    private void History_Click(
        object? sender,
        EventArgs e)
    {
        // Implement later.
    }

    private void Settings_Click(
        object? sender,
        EventArgs e)
    {
        // Implement later.
    }

    private void Exit_Click(
        object? sender,
        EventArgs e)
    {
        ExitApplication();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;

        _serviceProvider?.Dispose();
        _serviceProvider = null;

        if (ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
