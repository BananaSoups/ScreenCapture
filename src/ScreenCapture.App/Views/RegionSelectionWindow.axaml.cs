using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScreenCapture.Core.Models;
using System.Threading;

namespace ScreenCapture.App.Views;

public partial class RegionSelectionWindow : Window
{
    private CaptureResult? _desktopCapture;

    private Point? _startPoint;
    private bool _isSelecting;

    private TaskCompletionSource<CaptureRegion?>?
        _selectionCompletion;

    public CaptureRegion? SelectedRegion { get; private set; }

    public RegionSelectionWindow()
    {
        InitializeComponent();

        KeyDown += RegionSelectionWindow_KeyDown;

        Opened += (_, _) =>
        {
            Focus();
        };
    }

    public RegionSelectionWindow(
        CaptureResult desktopCapture,
        PixelRect virtualDesktopBounds)
        : this()
    {
        SetCapture(
            desktopCapture,
            virtualDesktopBounds);
    }

    public Task<CaptureRegion?> WaitForSelectionAsync(
        CancellationToken cancellationToken = default)
    {
        _selectionCompletion =
            new TaskCompletionSource<CaptureRegion?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                CompleteSelection(null);
            });
        }

        return _selectionCompletion.Task;
    }

    public void SetCapture(
        CaptureResult desktopCapture,
        PixelRect virtualDesktopBounds)
    {
        _desktopCapture = desktopCapture;

        Position = virtualDesktopBounds.Position;
        Width = virtualDesktopBounds.Width;
        Height = virtualDesktopBounds.Height;

        SelectedRegion = null;
        _startPoint = null;
        _isSelecting = false;

        SelectionBorder.IsVisible = false;
        SizeText.IsVisible = false;

        LoadDesktopImage();
    }

    private void LoadDesktopImage()
    {
        if (_desktopCapture is null)
            return;

        var bitmap = new WriteableBitmap(
            new PixelSize(
                _desktopCapture.Width,
                _desktopCapture.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Opaque);

        using (var framebuffer = bitmap.Lock())
        {
            var source =
                _desktopCapture.PixelData;

            var sourceRowBytes =
                _desktopCapture.Width * 4;

            if (framebuffer.RowBytes == sourceRowBytes)
            {
                Marshal.Copy(
                    source,
                    0,
                    framebuffer.Address,
                    source.Length);
            }
            else
            {
                for (var y = 0;
                     y < _desktopCapture.Height;
                     y++)
                {
                    Marshal.Copy(
                        source,
                        y * sourceRowBytes,
                        framebuffer.Address +
                            y * framebuffer.RowBytes,
                        sourceRowBytes);
                }
            }
        }

        DesktopImage.Source = bitmap;
    }

    private void Canvas_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this)
            .Properties.IsLeftButtonPressed)
        {
            return;
        }

        _startPoint =
            e.GetPosition(SelectionCanvas);

        _isSelecting = true;

        SelectionBorder.IsVisible = true;
        SizeText.IsVisible = true;

        UpdateSelection(
            _startPoint.Value);
    }

    private void Canvas_PointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (!_isSelecting ||
            _startPoint is null)
        {
            return;
        }

        UpdateSelection(
            e.GetPosition(SelectionCanvas));
    }

    private void Canvas_PointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!_isSelecting ||
            _startPoint is null)
        {
            return;
        }

        _isSelecting = false;

        var endPoint =
            e.GetPosition(SelectionCanvas);

        var x = Math.Min(
            _startPoint.Value.X,
            endPoint.X);

        var y = Math.Min(
            _startPoint.Value.Y,
            endPoint.Y);

        var width = Math.Abs(
            endPoint.X -
            _startPoint.Value.X);

        var height = Math.Abs(
            endPoint.Y -
            _startPoint.Value.Y);

        if (width < 2 || height < 2)
        {
            CancelSelection();
            return;
        }

        SelectedRegion =
            new CaptureRegion(
                (int)Math.Round(x),
                (int)Math.Round(y),
                (int)Math.Round(width),
                (int)Math.Round(height));

        CompleteSelection(
            SelectedRegion);
    }

    private void UpdateSelection(
        Point current)
    {
        if (_startPoint is null)
            return;

        var x = Math.Min(
            _startPoint.Value.X,
            current.X);

        var y = Math.Min(
            _startPoint.Value.Y,
            current.Y);

        var width = Math.Abs(
            current.X -
            _startPoint.Value.X);

        var height = Math.Abs(
            current.Y -
            _startPoint.Value.Y);

        Canvas.SetLeft(
            SelectionBorder,
            x);

        Canvas.SetTop(
            SelectionBorder,
            y);

        SelectionBorder.Width =
            width;

        SelectionBorder.Height =
            height;

        Canvas.SetLeft(
            SizeText,
            x);

        Canvas.SetTop(
            SizeText,
            Math.Max(0, y - 30));

        SizeText.Text =
            $"{Math.Round(width)} × {Math.Round(height)}";
    }

    private void RegionSelectionWindow_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelSelection();
        }
    }

    private void CancelSelection()
    {
        SelectedRegion = null;
        _isSelecting = false;
        _startPoint = null;

        SelectionBorder.IsVisible = false;
        SizeText.IsVisible = false;

        CompleteSelection(null);
    }

    private void CompleteSelection(
        CaptureRegion? region)
    {
        Hide();

        _selectionCompletion?
            .TrySetResult(region);
    }
}