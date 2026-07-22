namespace ColorAlert.Core;

public readonly record struct TargetColorStatistics(
    int SampleCount,
    int YellowCount,
    int BlueCount)
{
    public double YellowRatio => GetRatio(YellowCount);

    public double BlueRatio => GetRatio(BlueCount);

    public double GetRatio(MonitoredColor color) => color switch
    {
        MonitoredColor.Yellow => YellowRatio,
        MonitoredColor.Blue => BlueRatio,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };

    private double GetRatio(int count) => SampleCount > 0
        ? (double)count / SampleCount
        : 0d;
}
