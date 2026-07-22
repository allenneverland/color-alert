namespace ColorAlert.Core;

public readonly record struct SampleStatistics(int SampleCount, int MismatchCount)
{
    public double MismatchRatio => SampleCount > 0
        ? (double)MismatchCount / SampleCount
        : 0d;
}
