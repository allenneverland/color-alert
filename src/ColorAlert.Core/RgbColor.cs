using System.Globalization;

namespace ColorAlert.Core;

public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public static RgbColor Black { get; } = new(0, 0, 0);

    public string ToHex() => string.Create(
        CultureInfo.InvariantCulture,
        $"#{Red:X2}{Green:X2}{Blue:X2}");
}

