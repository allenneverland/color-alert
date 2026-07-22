namespace ColorAlert.Core;

public enum MonitoredColor
{
    Yellow,
    Blue,
}

public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public string ToHex() => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

public static class MonitoredColorPalette
{
    public static RgbColor GetRgb(MonitoredColor color) => color switch
    {
        MonitoredColor.Yellow => new RgbColor(0xCE, 0xB3, 0x74),
        MonitoredColor.Blue => new RgbColor(0x60, 0x88, 0xC5),
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };
}
