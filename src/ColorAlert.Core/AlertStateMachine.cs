namespace ColorAlert.Core;

public enum AlertTransition
{
    None,
    Triggered,
    Rearmed,
}

public sealed class AlertStateMachine
{
    private int _triggerFrameCount;
    private int _resetFrameCount;

    public bool IsAlerted { get; private set; }

    public AlertTransition Observe(double nonBlackRatio, DetectionSettings settings)
    {
        if (!double.IsFinite(nonBlackRatio) || nonBlackRatio < 0d || nonBlackRatio > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nonBlackRatio),
                nonBlackRatio,
                "非黑比例必須介於 0 與 1 之間。");
        }

        ArgumentNullException.ThrowIfNull(settings);
        var normalizedSettings = settings.Normalize();

        if (!IsAlerted)
        {
            _resetFrameCount = 0;
            _triggerFrameCount = nonBlackRatio >= normalizedSettings.TriggerRatio
                ? _triggerFrameCount + 1
                : 0;

            if (_triggerFrameCount < normalizedSettings.StableFrameCount)
            {
                return AlertTransition.None;
            }

            _triggerFrameCount = 0;
            IsAlerted = true;
            return AlertTransition.Triggered;
        }

        _triggerFrameCount = 0;
        _resetFrameCount = nonBlackRatio <= normalizedSettings.ResetRatio
            ? _resetFrameCount + 1
            : 0;

        if (_resetFrameCount < normalizedSettings.StableFrameCount)
        {
            return AlertTransition.None;
        }

        _resetFrameCount = 0;
        IsAlerted = false;
        return AlertTransition.Rearmed;
    }

    public void Reset()
    {
        _triggerFrameCount = 0;
        _resetFrameCount = 0;
        IsAlerted = false;
    }
}
