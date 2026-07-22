using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ColorAlert.Core;
using ColorAlert.Interop;

namespace ColorAlert.Services;

internal sealed class RegionOutlineOverlay : IDisposable
{
    private const int LineThickness = 2;
    private readonly List<Window> _lineWindows = [];
    private bool _disposed;

    internal void Show(ScreenRegion region)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Hide();

        if (!region.IsValid || !DisplayService.IsAvailable(region))
        {
            return;
        }

        var virtualBounds = DisplayService.GetVirtualScreenBounds();
        AddLine(new ScreenRegion(
            region.X,
            region.Y - LineThickness,
            region.Width,
            LineThickness), virtualBounds);
        AddLine(new ScreenRegion(
            region.X,
            checked((int)region.Bottom),
            region.Width,
            LineThickness), virtualBounds);
        AddLine(new ScreenRegion(
            region.X - LineThickness,
            region.Y,
            LineThickness,
            region.Height), virtualBounds);
        AddLine(new ScreenRegion(
            checked((int)region.Right),
            region.Y,
            LineThickness,
            region.Height), virtualBounds);
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

    private void AddLine(ScreenRegion requestedLine, ScreenRegion virtualBounds)
    {
        var line = Intersect(requestedLine, virtualBounds);
        if (line.Width <= 0 || line.Height <= 0)
        {
            return;
        }

        var window = new Window
        {
            AllowsTransparency = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)),
            Height = 1,
            Width = 1,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
        };

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
            line.X,
            line.Y,
            line.Width,
            line.Height,
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
