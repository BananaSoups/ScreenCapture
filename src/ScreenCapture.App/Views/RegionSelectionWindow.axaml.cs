using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ScreenCapture.Core.Imaging;
using ScreenCapture.Core.Models;

namespace ScreenCapture.App.Views;

public partial class RegionSelectionWindow : Window
{
    private CaptureResult? _desktopCapture;

    private Point? _startPoint;
    private bool _isSelecting;

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
        _desktopCapture = desktopCapture;

        Position = virtualDesktopBounds.Position;
        Width = virtualDesktopBounds.Width;
        Height = virtualDesktopBounds.Height;

        ShowActivated = true;

        LoadDesktopImage();
    }

    private void LoadDesktopImage()
    {
        if (_desktopCapture is null)
            return;

        var pngData = PngEncoder.EncodeRgba(
            _desktopCapture.PixelData,
            _desktopCapture.Width,
            _desktopCapture.Height);

        using var stream =
            new MemoryStream(pngData);

        DesktopImage.Source =
            new Bitmap(stream);
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

        Close();
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

        Close();
    }
}