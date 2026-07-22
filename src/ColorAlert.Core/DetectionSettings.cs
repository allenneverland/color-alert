namespace ColorAlert.Core;

public sealed record DetectionSettings
{
    public const int DefaultSensitivity = 50;
    public const int DefaultSampleIntervalMilliseconds = 250;
    public const int DefaultStableFrameCount = 3;

    public int Sensitivity { get; init; } = DefaultSensitivity;

    public int SampleIntervalMilliseconds { get; init; } = DefaultSampleIntervalMilliseconds;

    public int StableFrameCount { get; init; } = DefaultStableFrameCount;

    public int ColorTolerance => GetThresholds(Sensitivity).ColorTolerance;

    public double TargetPixelRatio => GetThresholds(Sensitivity).TargetPixelRatio;

    public double DecreaseRatio => TargetPixelRatio / 2d;

    public DetectionSettings Normalize() => this with
    {
        Sensitivity = Math.Clamp(Sensitivity, 1, 100),
        SampleIntervalMilliseconds = Math.Clamp(SampleIntervalMilliseconds, 100, 5_000),
        StableFrameCount = Math.Clamp(StableFrameCount, 1, 10),
    };

    public static SensitivityThresholds GetThresholds(int sensitivity)
    {
        var normalizedSensitivity = Math.Clamp(sensitivity, 1, 100);

        if (normalizedSensitivity == 1)
        {
            return new SensitivityThresholds(2, 0.20);
        }

        if (normalizedSensitivity == 50)
        {
            return new SensitivityThresholds(12, 0.01);
        }

        if (normalizedSensitivity == 100)
        {
            return new SensitivityThresholds(24, 0.001);
        }

        if (normalizedSensitivity < 50)
        {
            var progress = (normalizedSensitivity - 1d) / 49d;
            return new SensitivityThresholds(
                InterpolateInteger(2, 12, progress),
                Interpolate(0.20, 0.01, progress));
        }

        var highProgress = (normalizedSensitivity - 50d) / 50d;
        return new SensitivityThresholds(
            InterpolateInteger(12, 24, highProgress),
            Interpolate(0.01, 0.001, highProgress));
    }

    private static int InterpolateInteger(int start, int end, double progress) =>
        (int)Math.Round(Interpolate(start, end, progress), MidpointRounding.AwayFromZero);

    private static double Interpolate(double start, double end, double progress) =>
        start + ((end - start) * progress);
}

public readonly record struct SensitivityThresholds(
    int ColorTolerance,
    double TargetPixelRatio);
