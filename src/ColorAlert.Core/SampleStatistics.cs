namespace ColorAlert.Core;

public readonly record struct SampleStatistics(int SampleCount, int NonBlackCount)
{
    public double NonBlackRatio => SampleCount > 0
        ? (double)NonBlackCount / SampleCount
        : 0d;
}

