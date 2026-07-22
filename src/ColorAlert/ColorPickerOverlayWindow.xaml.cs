using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ColorAlert.Core;
using ColorAlert.Interop;
using ColorAlert.Services;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WindowsPoint = System.Windows.Point;
using WindowsSize = System.Windows.Size;

namespace ColorAlert;

public partial class ColorPickerOverlayWindow : Window
{
    private const int PreviewDimension = 11;
    private const double PreviewOffset = 22d;

    private readonly ScreenRegion _virtualBounds;
    private readonly DispatcherTimer _previewTimer;
    private readonly List<SolidColorBrush> _pixelBrushes = [];
    private HwndSource? _windowSource;

    public ColorPickerOverlayWindow()
    {
        InitializeComponent();
        _virtualBounds = DisplayService.GetVirtualScreenBounds();
        if (!_virtualBounds.IsValid)
        {
            throw new InvalidOperationException("目前沒有可用的顯示器範圍。");
        }

        CreatePixelGrid();
        _previewTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _previewTimer.Tick += PreviewTimer_Tick;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
            _previewTimer.Start();
            UpdatePreview();
        };
        Closed += (_, _) => _previewTimer.Stop();
    }

    internal NativeMethods.NativePoint? SelectedPoint { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        _windowSource = HwndSource.FromHwnd(helper.Handle);
        var positioned = NativeMethods.SetWindowPos(
            helper.Handle,
            NativeMethods.HwndTopmost,
            _virtualBounds.X,
            _virtualBounds.Y,
            _virtualBounds.Width,
            _virtualBounds.Height,
            NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpShowWindow);

        if (!positioned)
        {
            throw new Win32Exception("無法建立全螢幕取色介面。");
        }
    }

    private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PositionPreview();
        e.Handled = true;
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        SelectedPoint = point;
        DialogResult = true;
        e.Handled = true;
    }

    private void Overlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        DialogResult = false;
        e.Handled = true;
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e) => UpdatePreview();

    private void CreatePixelGrid()
    {
        var centerIndex = (PreviewDimension * PreviewDimension) / 2;
        for (var index = 0; index < PreviewDimension * PreviewDimension; index++)
        {
            var brush = new SolidColorBrush(Colors.Black);
            _pixelBrushes.Add(brush);
            PixelGrid.Children.Add(new Border
            {
                Background = brush,
                BorderBrush = index == centerIndex
                    ? MediaBrushes.White
                    : MediaBrushes.Transparent,
                BorderThickness = index == centerIndex ? new Thickness(2) : new Thickness(0),
            });
        }
    }

    private void UpdatePreview()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        try
        {
            var colors = ScreenPixelSampler.SampleGrid(point.X, point.Y, PreviewDimension);
            for (var index = 0; index < colors.Length; index++)
            {
                var color = colors[index];
                _pixelBrushes[index].Color = MediaColor.FromRgb(
                    color.Red,
                    color.Green,
                    color.Blue);
            }

            PreviewHexText.Text = colors[colors.Length / 2].ToHex();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            PreviewHexText.Text = "無法讀取像素";
        }

        PositionPreview(point);
    }

    private void PositionPreview()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            PositionPreview(point);
        }
    }

    private void PositionPreview(NativeMethods.NativePoint point)
    {
        if (_windowSource?.CompositionTarget is null)
        {
            return;
        }

        var fromDevice = _windowSource.CompositionTarget.TransformFromDevice;
        var cursor = fromDevice.Transform(new WindowsPoint(
            point.X - _virtualBounds.X,
            point.Y - _virtualBounds.Y));
        PreviewPanel.Measure(new WindowsSize(
            double.PositiveInfinity,
            double.PositiveInfinity));
        var panelSize = PreviewPanel.DesiredSize;

        var left = cursor.X + PreviewOffset;
        if (left + panelSize.Width > ActualWidth)
        {
            left = cursor.X - PreviewOffset - panelSize.Width;
        }

        var top = cursor.Y + PreviewOffset;
        if (top + panelSize.Height > ActualHeight)
        {
            top = cursor.Y - PreviewOffset - panelSize.Height;
        }

        Canvas.SetLeft(PreviewPanel, Math.Clamp(left, 0d, Math.Max(0d, ActualWidth - panelSize.Width)));
        Canvas.SetTop(PreviewPanel, Math.Clamp(top, 0d, Math.Max(0d, ActualHeight - panelSize.Height)));
    }
}
