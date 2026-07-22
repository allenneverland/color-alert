namespace ColorAlert.Core;

public readonly record struct TargetColorStatistics(
    int SampleCount,
    int PrimaryCount,
    int SecondaryCount)
{
    public double PrimaryRatio => GetRatio(PrimaryCount);

    public double SecondaryRatio => GetRatio(SecondaryCount);

    public double GetRatio(MonitoredColor color) => color switch
    {
        MonitoredColor.Primary => PrimaryRatio,
        MonitoredColor.Secondary => SecondaryRatio,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };

    private double GetRatio(int count) => SampleCount > 0
        ? (double)count / SampleCount
        : 0d;
}
