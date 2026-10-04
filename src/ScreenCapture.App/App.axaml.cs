using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScreenCapture.App.Services;
using ScreenCapture.App.ViewModels;
using ScreenCapture.App.Views;
using ScreenCapture.Application.Capture;
using ScreenCapture.Application.Storage;
using ScreenCapture.Platform;
using ScreenCapture.Platform.Linux;
using ScreenCapture.Platform.Linux.X11;

namespace ScreenCapture.App;

public partial class App : Avalonia.Application
{
    private MainWindow? _mainWindow;
    private bool _isExiting;

    private RegionCaptureWorkflow? _regionCaptureWorkflow;
    private ScreenCaptureWorkflow? _screenCaptureWorkflow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
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

            _mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildApplicationServices()
    {
        if (_mainWindow is null)
            throw new InvalidOperationException(
                "Main window has not been created.");

        var screenCaptureService =
            CreateScreenCaptureService();

        var regionSelectionService =
            new AvaloniaRegionSelectionService(
                _mainWindow);

        var storageService =
            new CaptureStorageService();

        _screenCaptureWorkflow =
            new ScreenCaptureWorkflow(
                screenCaptureService,
                storageService);

        _regionCaptureWorkflow =
            new RegionCaptureWorkflow(
                screenCaptureService,
                regionSelectionService,
                storageService);
    }

    private static IScreenCaptureService
        CreateScreenCaptureService()
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxScreenCaptureService(
                new X11ScreenCaptureBackend());
        }

        throw new PlatformNotSupportedException(
            "Screen capture is not implemented " +
            "for this operating system yet.");
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
        if (_regionCaptureWorkflow is null)
            return;

        try
        {
            await _regionCaptureWorkflow
                .CaptureAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Region capture failed: {ex}");
        }
    }

    private async void CaptureScreen_Click(
        object? sender,
        EventArgs e)
    {
        if (_screenCaptureWorkflow is null)
            return;

        try
        {
            await _screenCaptureWorkflow
                .CaptureAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Full screen capture failed: {ex}");
        }
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

        if (ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}