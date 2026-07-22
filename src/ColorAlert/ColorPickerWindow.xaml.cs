using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ColorAlert.Core;
using ColorAlert.Interop;
using ColorAlert.Services;

namespace ColorAlert;

public partial class ColorPickerWindow : Window
{
    private readonly ScreenRegion _virtualBounds;

    public ColorPickerWindow()
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

    internal (int X, int Y)? SelectedPoint { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var positioned = NativeMethods.SetWindowPos(
            handle,
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

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        SelectedPoint = (point.X, point.Y);
        DialogResult = true;
        Close();
        e.Handled = true;
    }

    private void Overlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        DialogResult = false;
        Close();
        e.Handled = true;
    }
}

