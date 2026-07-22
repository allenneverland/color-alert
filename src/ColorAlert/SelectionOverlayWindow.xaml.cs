using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ColorAlert.Core;
using ColorAlert.Interop;
using ColorAlert.Services;

namespace ColorAlert;

public partial class SelectionOverlayWindow : Window
{
    private readonly ScreenRegion _virtualBounds;
    private HwndSource? _windowSource;
    private NativeMethods.NativePoint _anchor;
    private ScreenRegion? _candidateRegion;
    private bool _isDragging;

    public SelectionOverlayWindow()
    {
        InitializeComponent();
        _virtualBounds = DisplayService.GetVirtualScreenBounds();

        if (!_virtualBounds.IsValid)
        {
            throw new InvalidOperationException("目前沒有可用的顯示器範圍。");
        }

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
        };
    }

    internal ScreenRegion? SelectedRegion { get; private set; }

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
            throw new Win32Exception("無法建立全螢幕選取介面。");
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out _anchor))
        {
            return;
        }

        _candidateRegion = null;
        _isDragging = true;
        InstructionText.Text = "放開滑鼠後按 Enter 確認";
        _ = Mouse.Capture(OverlayRoot);
        DrawSelection(ScreenRegion.FromPoints(_anchor.X, _anchor.Y, _anchor.X, _anchor.Y));
        e.Handled = true;
    }

    private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging || !NativeMethods.GetCursorPos(out var current))
        {
            return;
        }

        DrawSelection(CreateClampedRegion(_anchor, current));
        e.Handled = true;
    }

    private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        Mouse.Capture(null);

        if (!NativeMethods.GetCursorPos(out var current))
        {
            return;
        }

        var region = CreateClampedRegion(_anchor, current);
        if (!region.IsValid)
        {
            _candidateRegion = null;
            InstructionText.Text = "區域太小，請至少選取 4 × 4 像素";
            return;
        }

        _candidateRegion = region;
        DrawSelection(region);
        InstructionText.Text = "按 Enter 確認；重新拖曳可重選；Esc 取消";
        e.Handled = true;
    }

    private void Overlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _candidateRegion is { IsValid: true } region)
        {
            SelectedRegion = region;
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }

    private ScreenRegion CreateClampedRegion(
        NativeMethods.NativePoint anchor,
        NativeMethods.NativePoint current)
    {
        var x1 = Math.Clamp(anchor.X, _virtualBounds.X, checked((int)_virtualBounds.Right));
        var y1 = Math.Clamp(anchor.Y, _virtualBounds.Y, checked((int)_virtualBounds.Bottom));
        var x2 = Math.Clamp(current.X, _virtualBounds.X, checked((int)_virtualBounds.Right));
        var y2 = Math.Clamp(current.Y, _virtualBounds.Y, checked((int)_virtualBounds.Bottom));

        return ScreenRegion.FromPoints(x1, y1, x2, y2);
    }

    private void DrawSelection(ScreenRegion region)
    {
        if (_windowSource?.CompositionTarget is null)
        {
            return;
        }

        var fromDevice = _windowSource.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new System.Windows.Point(
            region.X - _virtualBounds.X,
            region.Y - _virtualBounds.Y));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(
            region.Right - _virtualBounds.X,
            region.Bottom - _virtualBounds.Y));

        var width = Math.Max(0d, bottomRight.X - topLeft.X);
        var height = Math.Max(0d, bottomRight.Y - topLeft.Y);

        SelectionRectangle.Visibility = Visibility.Visible;
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        Canvas.SetLeft(SelectionRectangle, topLeft.X);
        Canvas.SetTop(SelectionRectangle, topLeft.Y);

        SizeBadge.Visibility = Visibility.Visible;
        SizeText.Text = $"{region.Width:N0} × {region.Height:N0} px";
        Canvas.SetLeft(SizeBadge, Math.Max(8d, topLeft.X));
        Canvas.SetTop(SizeBadge, Math.Max(8d, topLeft.Y - 34d));
    }
}
