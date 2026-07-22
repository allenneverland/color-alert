using System.ComponentModel;
using System.Runtime.InteropServices;
using ColorAlert.Core;
using ColorAlert.Interop;

namespace ColorAlert.Services;

internal static class ScreenPixelSampler
{
    internal static RgbColor SamplePixel(int x, int y)
    {
        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法取得桌面畫面。");
        }

        try
        {
            return ReadPixel(screenDc, x, y);
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }

    internal static RgbColor[] SampleGrid(int centerX, int centerY, int dimension)
    {
        if (dimension <= 0 || dimension % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimension),
                dimension,
                "像素預覽尺寸必須是正奇數。");
        }

        var virtualBounds = DisplayService.GetVirtualScreenBounds();
        if (!virtualBounds.IsValid)
        {
            throw new InvalidOperationException("目前沒有可用的顯示器範圍。");
        }

        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法取得桌面畫面。");
        }

        try
        {
            var radius = dimension / 2;
            var colors = new RgbColor[dimension * dimension];
            for (var row = 0; row < dimension; row++)
            {
                var y = Math.Clamp(
                    centerY + row - radius,
                    virtualBounds.Y,
                    checked((int)virtualBounds.Bottom - 1));
                for (var column = 0; column < dimension; column++)
                {
                    var x = Math.Clamp(
                        centerX + column - radius,
                        virtualBounds.X,
                        checked((int)virtualBounds.Right - 1));
                    colors[(row * dimension) + column] = ReadPixel(screenDc, x, y);
                }
            }

            return colors;
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static RgbColor ReadPixel(nint screenDc, int x, int y)
    {
        var colorReference = NativeMethods.GetPixel(screenDc, x, y);
        if (colorReference == NativeMethods.InvalidColor)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法讀取畫面像素。");
        }

        return new RgbColor(
            (byte)(colorReference & 0xFF),
            (byte)((colorReference >> 8) & 0xFF),
            (byte)((colorReference >> 16) & 0xFF));
    }
}
