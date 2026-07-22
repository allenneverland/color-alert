using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ColorAlert.Core;
using ColorAlert.Interop;

namespace ColorAlert.Services;

internal readonly record struct RegionOutlineItem(
    ScreenRegion Region,
    RegionMonitorStatus Status);

internal sealed class RegionOutlineOverlay : IDisposable
{
    private const int LineThickness = 2;
    private const int LocatorLineThickness = 5;
    private const int LocatorLabelWidth = 120;
    private const int LocatorLabelHeight = 40;
    private static readonly System.Windows.Media.Color NormalColor =
        System.Windows.Media.Color.FromRgb(37, 99, 235);
    private static readonly System.Windows.Media.Color AlertedColor =
        System.Windows.Media.Color.FromRgb(234, 88, 12);
    private static readonly System.Windows.Media.Color LocatorColor =
        System.Windows.Media.Color.FromRgb(219, 39, 119);

    private readonly List<Window> _lineWindows = [];
    private bool _disposed;

    internal void Show(IEnumerable<RegionOutlineItem> regions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(regions);
        Hide();

        var virtualBounds = DisplayService.GetVirtualScreenBounds();
        foreach (var item in regions)
        {
            var region = item.Region;
            if (!region.IsValid ||
                !DisplayService.IsAvailable(region) ||
                item.Status == RegionMonitorStatus.Invalid)
            {
                continue;
            }

            var color = item.Status == RegionMonitorStatus.Alerted
                ? AlertedColor
                : NormalColor;
            AddLine(new ScreenRegion(
                region.X,
                region.Y - LineThickness,
                region.Width,
                LineThickness), virtualBounds, color);
            AddLine(new ScreenRegion(
                region.X,
                checked((int)region.Bottom),
                region.Width,
                LineThickness), virtualBounds, color);
            AddLine(new ScreenRegion(
                region.X - LineThickness,
                region.Y,
                LineThickness,
                region.Height), virtualBounds, color);
            AddLine(new ScreenRegion(
                checked((int)region.Right),
                region.Y,
                LineThickness,
                region.Height), virtualBounds, color);
        }
    }

    internal void ShowLocator(ScreenRegion region, string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Hide();

        if (!region.IsValid || !DisplayService.IsAvailable(region))
        {
            throw new ArgumentException("無法定位目前顯示器範圍外的區域。", nameof(region));
        }

        var virtualBounds = DisplayService.GetVirtualScreenBounds();
        AddLine(new ScreenRegion(
            region.X,
            region.Y - LocatorLineThickness,
            region.Width,
            LocatorLineThickness), virtualBounds, LocatorColor);
        AddLine(new ScreenRegion(
            region.X,
            checked((int)region.Bottom),
            region.Width,
            LocatorLineThickness), virtualBounds, LocatorColor);
        AddLine(new ScreenRegion(
            region.X - LocatorLineThickness,
            region.Y,
            LocatorLineThickness,
            region.Height), virtualBounds, LocatorColor);
        AddLine(new ScreenRegion(
            checked((int)region.Right),
            region.Y,
            LocatorLineThickness,
            region.Height), virtualBounds, LocatorColor);
        AddLabel(region, virtualBounds, label);

        var animation = new DoubleAnimation
        {
            From = 1d,
            To = 0.25d,
            Duration = TimeSpan.FromMilliseconds(250),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(TimeSpan.FromSeconds(2)),
            FillBehavior = FillBehavior.Stop,
        };
        foreach (var window in _lineWindows)
        {
            window.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }

    internal void Hide()
    {
        foreach (var window in _lineWindows)
        {
            window.Close();
        }

        _lineWindows.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Hide();
        _disposed = true;
    }

    private void AddLine(
        ScreenRegion requestedLine,
        ScreenRegion virtualBounds,
        System.Windows.Media.Color color)
    {
        var line = Intersect(requestedLine, virtualBounds);
        if (line.Width <= 0 || line.Height <= 0)
        {
            return;
        }

        var window = CreateOverlayWindow();
        window.Background = new SolidColorBrush(color);
        ShowOverlayWindow(window, line);
    }

    private void AddLabel(
        ScreenRegion region,
        ScreenRegion virtualBounds,
        string label)
    {
        var labelWidth = Math.Min(LocatorLabelWidth, virtualBounds.Width);
        var labelHeight = Math.Min(LocatorLabelHeight, virtualBounds.Height);
        var labelX = checked((int)Math.Clamp(
            (long)region.X + 8,
            virtualBounds.X,
            virtualBounds.Right - labelWidth));
        var labelY = checked((int)Math.Clamp(
            (long)region.Y + 8,
            virtualBounds.Y,
            virtualBounds.Bottom - labelHeight));
        var labelBounds = new ScreenRegion(labelX, labelY, labelWidth, labelHeight);

        var labelBorder = new Border
        {
            Background = new SolidColorBrush(LocatorColor),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            },
        };

        var window = CreateOverlayWindow();
        window.Background = System.Windows.Media.Brushes.Transparent;
        window.Content = labelBorder;
        ShowOverlayWindow(window, labelBounds);
    }

    private static Window CreateOverlayWindow() => new()
    {
        AllowsTransparency = true,
        Height = 1,
        Width = 1,
        ResizeMode = ResizeMode.NoResize,
        ShowActivated = false,
        ShowInTaskbar = false,
        Topmost = true,
        WindowStartupLocation = WindowStartupLocation.Manual,
        WindowStyle = WindowStyle.None,
    };

    private void ShowOverlayWindow(Window window, ScreenRegion bounds)
    {
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        _ = NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle,
            extendedStyle |
            NativeMethods.WsExTransparent |
            NativeMethods.WsExToolWindow |
            NativeMethods.WsExNoActivate);

        _ = NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpNoOwnerZOrder |
            NativeMethods.SwpShowWindow);

        _lineWindows.Add(window);
    }

    private static ScreenRegion Intersect(ScreenRegion first, ScreenRegion second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);

        return right <= left || bottom <= top
            ? default
            : new ScreenRegion(
                left,
                top,
                checked((int)(right - left)),
                checked((int)(bottom - top)));
    }
}
