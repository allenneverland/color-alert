namespace ColorAlert.Core;

public sealed record AppSettings
{
    public ScreenRegion? Region { get; init; }

    public DetectionSettings Detection { get; init; } = new();

    public AppSettings Normalize() => this with
    {
        Region = Region is { IsValid: true } region ? region : null,
        Detection = (Detection ?? new DetectionSettings()).Normalize(),
    };
}

