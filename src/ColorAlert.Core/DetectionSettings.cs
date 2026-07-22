namespace ColorAlert.Core;

public sealed record DetectionSettings
{
    public const int DefaultBlackTolerance = 12;
    public const double DefaultTriggerRatio = 0.01;
    public const int DefaultSampleIntervalMilliseconds = 250;
    public const int DefaultStableFrameCount = 3;

    public int BlackTolerance { get; init; } = DefaultBlackTolerance;

    public double TriggerRatio { get; init; } = DefaultTriggerRatio;

    public int SampleIntervalMilliseconds { get; init; } = DefaultSampleIntervalMilliseconds;

    public int StableFrameCount { get; init; } = DefaultStableFrameCount;

    public double ResetRatio => TriggerRatio / 2d;

    public DetectionSettings Normalize() => this with
    {
        BlackTolerance = Math.Clamp(BlackTolerance, 0, 64),
        TriggerRatio = Math.Clamp(TriggerRatio, 0.001, 0.20),
        SampleIntervalMilliseconds = Math.Clamp(SampleIntervalMilliseconds, 100, 5_000),
        StableFrameCount = Math.Clamp(StableFrameCount, 1, 10),
    };
}

