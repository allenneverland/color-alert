using System.Text.Json.Serialization;

namespace ColorAlert.Core;

public sealed record DetectionSettings
{
    public const int DefaultColorSensitivity = 50;
    public const int DefaultAreaSensitivity = 50;
    public const int DefaultSampleIntervalMilliseconds = 250;
    public const int DefaultStableFrameCount = 3;
    public const int CurrentSensitivityScaleVersion = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Sensitivity { get; init; }

    public int ColorSensitivity { get; init; } = DefaultColorSensitivity;

    public int AreaSensitivity { get; init; } = DefaultAreaSensitivity;

    public int SensitivityScaleVersion { get; init; }

    public int SampleIntervalMilliseconds { get; init; } = DefaultSampleIntervalMilliseconds;

    public int StableFrameCount { get; init; } = DefaultStableFrameCount;

    public int ColorTolerance => GetColorTolerance(ColorSensitivity);

    public double TargetPixelRatio => GetTargetPixelRatio(AreaSensitivity);

    public double DecreaseRatio => TargetPixelRatio / 2d;

    public static DetectionSettings CreateDefault() => new()
    {
        SensitivityScaleVersion = CurrentSensitivityScaleVersion,
    };

    public DetectionSettings MigrateSavedSensitivityScale()
    {
        if (SensitivityScaleVersion >= CurrentSensitivityScaleVersion)
        {
            return Normalize();
        }

        var hasLegacySensitivity = Sensitivity != 0;
        var legacySensitivity = Math.Clamp(Sensitivity, 1, 100);
        var previousColorSensitivity = hasLegacySensitivity
            ? legacySensitivity
            : Math.Clamp(ColorSensitivity, 1, 100);
        var previousAreaSensitivity = hasLegacySensitivity
            ? legacySensitivity
            : Math.Clamp(AreaSensitivity, 1, 100);

        return (this with
        {
            Sensitivity = 0,
            ColorSensitivity = MigratePreviousSensitivity(previousColorSensitivity),
            AreaSensitivity = MigratePreviousSensitivity(previousAreaSensitivity),
            SensitivityScaleVersion = CurrentSensitivityScaleVersion,
        }).Normalize();
    }

    public DetectionSettings Normalize()
    {
        var hasLegacySensitivity = Sensitivity != 0;
        var legacySensitivity = Math.Clamp(Sensitivity, 1, 100);

        return this with
        {
            Sensitivity = 0,
            ColorSensitivity = hasLegacySensitivity
                ? legacySensitivity
                : Math.Clamp(ColorSensitivity, 1, 100),
            AreaSensitivity = hasLegacySensitivity
                ? legacySensitivity
                : Math.Clamp(AreaSensitivity, 1, 100),
            SensitivityScaleVersion = Math.Max(
                SensitivityScaleVersion,
                CurrentSensitivityScaleVersion),
            SampleIntervalMilliseconds = Math.Clamp(SampleIntervalMilliseconds, 100, 5_000),
            StableFrameCount = Math.Clamp(StableFrameCount, 1, 10),
        };
    }

    public static int GetColorTolerance(int sensitivity)
    {
        var normalizedSensitivity = Math.Clamp(sensitivity, 1, 100);

        if (normalizedSensitivity == 1)
        {
            return 12;
        }

        if (normalizedSensitivity == 50)
        {
            return 24;
        }

        if (normalizedSensitivity == 100)
        {
            return 48;
        }

        if (normalizedSensitivity < 50)
        {
            var progress = (normalizedSensitivity - 1d) / 49d;
            return InterpolateInteger(12, 24, progress);
        }

        var highProgress = (normalizedSensitivity - 50d) / 50d;
        return InterpolateInteger(24, 48, highProgress);
    }

    public static double GetTargetPixelRatio(int sensitivity)
    {
        var normalizedSensitivity = Math.Clamp(sensitivity, 1, 100);

        if (normalizedSensitivity == 1)
        {
            return 0.001;
        }

        if (normalizedSensitivity == 50)
        {
            return 0.0001;
        }

        if (normalizedSensitivity == 100)
        {
            return 0.00001;
        }

        if (normalizedSensitivity < 50)
        {
            var progress = (normalizedSensitivity - 1d) / 49d;
            return Interpolate(0.001, 0.0001, progress);
        }

        var highProgress = (normalizedSensitivity - 50d) / 50d;
        return Interpolate(0.0001, 0.00001, highProgress);
    }

    private static int MigratePreviousSensitivity(int sensitivity)
    {
        var normalizedSensitivity = Math.Clamp(sensitivity, 1, 100);
        if (normalizedSensitivity <= 50)
        {
            return 1;
        }

        var progress = (normalizedSensitivity - 50d) / 50d;
        return InterpolateInteger(1, 50, progress);
    }

    private static int InterpolateInteger(int start, int end, double progress) =>
        (int)Math.Round(Interpolate(start, end, progress), MidpointRounding.AwayFromZero);

    private static double Interpolate(double start, double end, double progress) =>
        start + ((end - start) * progress);
}
