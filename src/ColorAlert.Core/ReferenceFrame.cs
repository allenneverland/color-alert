namespace ColorAlert.Core;

public sealed class ReferenceFrame
{
    public ReferenceFrame(int width, int height, byte[] bgrPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        ArgumentNullException.ThrowIfNull(bgrPixels);
        if (bgrPixels.Length != checked(width * height * 3))
        {
            throw new ArgumentException("基準畫面的像素資料長度不正確。", nameof(bgrPixels));
        }

        Width = width;
        Height = height;
        BgrPixels = bgrPixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> BgrPixels { get; }
}
