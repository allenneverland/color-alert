using System.ComponentModel;
using System.Runtime.InteropServices;
using ColorAlert.Core;
using ColorAlert.Interop;

namespace ColorAlert.Services;

internal sealed class GdiScreenSampler : IDisposable
{
    private const int MaximumSampleDimension = 256;
    private const int BytesPerPixel = 4;

    private readonly nint _memoryDc;
    private nint _bitmap;
    private nint _previousBitmap;
    private nint _bits;
    private byte[] _pixelBuffer = [];
    private int _sampleWidth;
    private int _sampleHeight;
    private bool _disposed;

    internal GdiScreenSampler()
    {
        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法取得桌面裝置內容。");
        }

        try
        {
            _memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (_memoryDc == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "無法建立記憶體裝置內容。");
            }

            _ = NativeMethods.SetStretchBltMode(_memoryDc, NativeMethods.ColorOnColor);
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }

    internal SampleStatistics Sample(ScreenRegion region, int blackTolerance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!region.IsValid)
        {
            throw new ArgumentException("監看區域無效。", nameof(region));
        }

        var targetWidth = Math.Min(region.Width, MaximumSampleDimension);
        var targetHeight = Math.Min(region.Height, MaximumSampleDimension);
        EnsureBuffer(targetWidth, targetHeight);

        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法取得桌面畫面。");
        }

        try
        {
            var copied = NativeMethods.StretchBlt(
                _memoryDc,
                0,
                0,
                targetWidth,
                targetHeight,
                screenDc,
                region.X,
                region.Y,
                region.Width,
                region.Height,
                NativeMethods.SourceCopy | NativeMethods.CaptureLayeredWindows);

            if (!copied)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "螢幕擷取失敗。");
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }

        Marshal.Copy(_bits, _pixelBuffer, 0, _pixelBuffer.Length);

        var normalizedTolerance = Math.Clamp(blackTolerance, 0, 255);
        var nonBlackCount = 0;

        for (var offset = 0; offset < _pixelBuffer.Length; offset += BytesPerPixel)
        {
            var blue = _pixelBuffer[offset];
            var green = _pixelBuffer[offset + 1];
            var red = _pixelBuffer[offset + 2];

            if (red > normalizedTolerance || green > normalizedTolerance || blue > normalizedTolerance)
            {
                nonBlackCount++;
            }
        }

        return new SampleStatistics(targetWidth * targetHeight, nonBlackCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_bitmap != nint.Zero)
        {
            _ = NativeMethods.SelectObject(_memoryDc, _previousBitmap);
            _ = NativeMethods.DeleteObject(_bitmap);
        }

        if (_memoryDc != nint.Zero)
        {
            _ = NativeMethods.DeleteDC(_memoryDc);
        }

        _disposed = true;
    }

    private void EnsureBuffer(int width, int height)
    {
        if (width == _sampleWidth && height == _sampleHeight)
        {
            return;
        }

        if (_bitmap != nint.Zero)
        {
            _ = NativeMethods.SelectObject(_memoryDc, _previousBitmap);
            _ = NativeMethods.DeleteObject(_bitmap);
            _bitmap = nint.Zero;
            _previousBitmap = nint.Zero;
        }

        var bitmapInfo = new NativeMethods.BitmapInfo
        {
            Header = new NativeMethods.BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = NativeMethods.BiRgb,
                SizeImage = (uint)(width * height * BytesPerPixel),
            },
        };

        _bitmap = NativeMethods.CreateDIBSection(
            _memoryDc,
            in bitmapInfo,
            NativeMethods.DibRgbColors,
            out _bits,
            nint.Zero,
            0);

        if (_bitmap == nint.Zero || _bits == nint.Zero)
        {
            if (_bitmap != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(_bitmap);
                _bitmap = nint.Zero;
            }

            _bits = nint.Zero;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法建立螢幕擷取緩衝區。");
        }

        _previousBitmap = NativeMethods.SelectObject(_memoryDc, _bitmap);
        if (_previousBitmap == nint.Zero || _previousBitmap == new nint(-1))
        {
            _ = NativeMethods.DeleteObject(_bitmap);
            _bitmap = nint.Zero;
            _bits = nint.Zero;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法選取螢幕擷取緩衝區。");
        }

        _sampleWidth = width;
        _sampleHeight = height;
        _pixelBuffer = new byte[width * height * BytesPerPixel];
    }
}
