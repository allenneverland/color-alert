namespace ColorAlert.Core;

public sealed record MonitoredRegionDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ScreenRegion Bounds { get; init; }
}
