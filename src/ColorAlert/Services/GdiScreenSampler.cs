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

    internal ReferenceFrame CaptureReference(ScreenRegion region)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!region.IsValid)
        {
            throw new ArgumentException("監看區域無效。", nameof(region));
        }

        var targetWidth = Math.Min(region.Width, MaximumSampleDimension);
        var targetHeight = Math.Min(region.Height, MaximumSampleDimension);
        EnsureBuffer(targetWidth, targetHeight);
        Capture(region, targetWidth, targetHeight);
        Marshal.Copy(_bits, _pixelBuffer, 0, _pixelBuffer.Length);

        var bgrPixels = new byte[targetWidth * targetHeight * 3];
        for (var sourceOffset = 0; sourceOffset < _pixelBuffer.Length; sourceOffset += BytesPerPixel)
        {
            var targetOffset = (sourceOffset / BytesPerPixel) * 3;
            bgrPixels[targetOffset] = _pixelBuffer[sourceOffset];
            bgrPixels[targetOffset + 1] = _pixelBuffer[sourceOffset + 1];
            bgrPixels[targetOffset + 2] = _pixelBuffer[sourceOffset + 2];
        }

        return new ReferenceFrame(targetWidth, targetHeight, bgrPixels);
    }

    internal SampleStatistics Compare(
        ScreenRegion region,
        ReferenceFrame referenceFrame,
        int pixelDifferenceTolerance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(referenceFrame);

        if (!region.IsValid)
        {
            throw new ArgumentException("監看區域無效。", nameof(region));
        }

        var expectedWidth = Math.Min(region.Width, MaximumSampleDimension);
        var expectedHeight = Math.Min(region.Height, MaximumSampleDimension);
        if (referenceFrame.Width != expectedWidth || referenceFrame.Height != expectedHeight)
        {
            throw new ArgumentException("基準畫面尺寸與監看區域不一致。", nameof(referenceFrame));
        }

        EnsureBuffer(referenceFrame.Width, referenceFrame.Height);
        Capture(region, referenceFrame.Width, referenceFrame.Height);
        Marshal.Copy(_bits, _pixelBuffer, 0, _pixelBuffer.Length);

        var referencePixels = referenceFrame.BgrPixels.Span;
        var normalizedTolerance = Math.Clamp(pixelDifferenceTolerance, 0, 255);
        var mismatchCount = 0;

        for (var sourceOffset = 0; sourceOffset < _pixelBuffer.Length; sourceOffset += BytesPerPixel)
        {
            var referenceOffset = (sourceOffset / BytesPerPixel) * 3;
            var blueDifference = Math.Abs(
                _pixelBuffer[sourceOffset] - referencePixels[referenceOffset]);
            var greenDifference = Math.Abs(
                _pixelBuffer[sourceOffset + 1] - referencePixels[referenceOffset + 1]);
            var redDifference = Math.Abs(
                _pixelBuffer[sourceOffset + 2] - referencePixels[referenceOffset + 2]);

            if (redDifference > normalizedTolerance ||
                greenDifference > normalizedTolerance ||
                blueDifference > normalizedTolerance)
            {
                mismatchCount++;
            }
        }

        return new SampleStatistics(referenceFrame.Width * referenceFrame.Height, mismatchCount);
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

    private void Capture(ScreenRegion sourceRegion, int targetWidth, int targetHeight)
    {
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
                sourceRegion.X,
                sourceRegion.Y,
                sourceRegion.Width,
                sourceRegion.Height,
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
    }
}
