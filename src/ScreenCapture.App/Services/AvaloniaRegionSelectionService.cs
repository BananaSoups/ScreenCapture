using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ScreenCapture.Application.UI;
using ScreenCapture.App.Views;
using ScreenCapture.Core.Models;

namespace ScreenCapture.App.Services;

public sealed class AvaloniaRegionSelectionService
    : IRegionSelectionService
{
    private readonly MainWindow _mainWindow;

    public AvaloniaRegionSelectionService(
        MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public async Task<CaptureRegion?> SelectRegionAsync(
        CaptureResult frozenDesktop,
        CancellationToken cancellationToken = default)
    {
        if (_mainWindow.Screens.All.Count == 0)
        {
            throw new InvalidOperationException(
                "Unable to determine the connected displays.");
        }

        var screens =
            _mainWindow.Screens.All;

        var left =
            screens.Min(screen => screen.Bounds.X);

        var top =
            screens.Min(screen => screen.Bounds.Y);

        var right =
            screens.Max(screen =>
                screen.Bounds.X +
                screen.Bounds.Width);

        var bottom =
            screens.Max(screen =>
                screen.Bounds.Y +
                screen.Bounds.Height);

        var virtualDesktopBounds =
            new PixelRect(
                left,
                top,
                right - left,
                bottom - top);

        Console.WriteLine(
            $"Virtual desktop: " +
            $"{virtualDesktopBounds.X}," +
            $"{virtualDesktopBounds.Y} " +
            $"{virtualDesktopBounds.Width}x" +
            $"{virtualDesktopBounds.Height}");

        Console.WriteLine(
            $"Captured desktop: " +
            $"{frozenDesktop.Width}x" +
            $"{frozenDesktop.Height}");

        var selectionWindow =
            new RegionSelectionWindow(
                frozenDesktop,
                virtualDesktopBounds);

        var closed =
            new TaskCompletionSource<bool>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        selectionWindow.Closed += (_, _) =>
        {
            closed.TrySetResult(true);
        };

        selectionWindow.Show();

        using var registration =
            cancellationToken.Register(
                selectionWindow.Close);

        await closed.Task;

        return selectionWindow.SelectedRegion;
    }
}
