namespace ColorAlert.Core;

public enum AlertTransition
{
    None,
    Triggered,
    Rebased,
    Cleared,
}

public sealed class TargetAreaStateMachine
{
    private int _increaseFrameCount;
    private int _decreaseFrameCount;
    private int _clearFrameCount;

    public double ConfirmedRatio { get; private set; }

    public bool IsPresent { get; private set; }

    public AlertTransition Observe(double matchedRatio, DetectionSettings settings)
    {
        if (!double.IsFinite(matchedRatio) || matchedRatio < 0d || matchedRatio > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchedRatio),
                matchedRatio,
                "目標顏色比例必須介於 0 與 1 之間。");
        }

        ArgumentNullException.ThrowIfNull(settings);
        var normalizedSettings = settings.Normalize();

        if (matchedRatio <= normalizedSettings.DecreaseRatio)
        {
            _increaseFrameCount = 0;
            _decreaseFrameCount = 0;
            _clearFrameCount++;

            if (_clearFrameCount < normalizedSettings.StableFrameCount)
            {
                return AlertTransition.None;
            }

            var hadConfirmedTarget = IsPresent || ConfirmedRatio > 0d;
            Reset();
            return hadConfirmedTarget ? AlertTransition.Cleared : AlertTransition.None;
        }

        _clearFrameCount = 0;
        if (matchedRatio - ConfirmedRatio >= normalizedSettings.TargetPixelRatio)
        {
            _decreaseFrameCount = 0;
            _increaseFrameCount++;

            if (_increaseFrameCount < normalizedSettings.StableFrameCount)
            {
                return AlertTransition.None;
            }

            _increaseFrameCount = 0;
            ConfirmedRatio = matchedRatio;
            IsPresent = true;
            return AlertTransition.Triggered;
        }

        _increaseFrameCount = 0;
        if (ConfirmedRatio - matchedRatio >= normalizedSettings.DecreaseRatio)
        {
            _decreaseFrameCount++;

            if (_decreaseFrameCount < normalizedSettings.StableFrameCount)
            {
                return AlertTransition.None;
            }

            _decreaseFrameCount = 0;
            ConfirmedRatio = matchedRatio;
            return AlertTransition.Rebased;
        }

        _decreaseFrameCount = 0;
        return AlertTransition.None;
    }

    public void Reset()
    {
        _increaseFrameCount = 0;
        _decreaseFrameCount = 0;
        _clearFrameCount = 0;
        ConfirmedRatio = 0d;
        IsPresent = false;
    }
}
