namespace ColorAlert.Core;

public enum MonitoredColor
{
    Primary,
    Secondary,
}

public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public string ToHex() => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

public static class TargetColorDefaults
{
    public static RgbColor Primary { get; } = new(0xE3, 0xB3, 0x41);

    public static RgbColor Secondary { get; } = new(0x60, 0x88, 0xC5);
}
