namespace ColorAlert.Core;

public readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width >= 4 && Height >= 4;

    public long Right => (long)X + Width;

    public long Bottom => (long)Y + Height;

    public bool IsContainedBy(ScreenRegion bounds) =>
        IsValid &&
        bounds.IsValid &&
        X >= bounds.X &&
        Y >= bounds.Y &&
        Right <= bounds.Right &&
        Bottom <= bounds.Bottom;

    public static ScreenRegion FromPoints(int x1, int y1, int x2, int y2)
    {
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        var right = Math.Max(x1, x2);
        var bottom = Math.Max(y1, y2);

        return new ScreenRegion(left, top, right - left, bottom - top);
    }
}

