using System.Text.Json.Serialization;

namespace ColorAlert.Core;

public sealed record AppSettings
{
    public const int MaximumRegionCount = 10;

    public MonitoredRegionDefinition[] Regions { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScreenRegion? Region { get; init; }

    public DetectionSettings Detection { get; init; } = DetectionSettings.CreateDefault();

    public AlertRepeatMode AlertMode { get; init; } = AlertRepeatMode.Once;

    public bool ShowRegionOverlay { get; init; } = true;

    public AppSettings Normalize()
    {
        var normalizedRegions = (Regions ?? [])
            .Where(region => region.Bounds.IsValid)
            .Take(MaximumRegionCount)
            .Select(region => region with
            {
                Id = region.Id == Guid.Empty ? Guid.NewGuid() : region.Id,
            })
            .GroupBy(region => region.Id)
            .Select(group => group.First())
            .ToList();

        if (normalizedRegions.Count == 0 && Region is { IsValid: true } legacyRegion)
        {
            normalizedRegions.Add(new MonitoredRegionDefinition { Bounds = legacyRegion });
        }

        return this with
        {
            Regions = [.. normalizedRegions],
            Region = null,
            Detection = (Detection ?? DetectionSettings.CreateDefault())
                .MigrateSavedSensitivityScale(),
            AlertMode = Enum.IsDefined(AlertMode) ? AlertMode : AlertRepeatMode.Once,
        };
    }
}
