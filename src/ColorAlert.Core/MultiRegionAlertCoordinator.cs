namespace ColorAlert.Core;

public readonly record struct TargetObservation(
    Guid RegionId,
    MonitoredColor Color,
    double MatchedRatio);

public readonly record struct TargetAlertResult(
    Guid RegionId,
    MonitoredColor Color,
    AlertTransition Transition,
    bool IsPresent);

public sealed record MultiRegionAlertResult(
    IReadOnlyList<TargetAlertResult> Targets,
    bool ShouldAlert);

public sealed class MultiRegionAlertCoordinator
{
    private readonly Dictionary<TargetKey, TargetAreaStateMachine> _stateMachines = [];

    public void Synchronize(IEnumerable<Guid> regionIds)
    {
        ArgumentNullException.ThrowIfNull(regionIds);
        var currentIds = regionIds.Where(id => id != Guid.Empty).ToHashSet();

        foreach (var removedKey in _stateMachines.Keys
                     .Where(key => !currentIds.Contains(key.RegionId))
                     .ToArray())
        {
            _stateMachines.Remove(removedKey);
        }

        foreach (var regionId in currentIds)
        {
            foreach (var color in Enum.GetValues<MonitoredColor>())
            {
                _stateMachines.TryAdd(
                    new TargetKey(regionId, color),
                    new TargetAreaStateMachine());
            }
        }
    }

    public MultiRegionAlertResult Observe(
        IEnumerable<TargetObservation> observations,
        DetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(settings);

        var results = new List<TargetAlertResult>();
        var shouldAlert = false;

        foreach (var observation in observations)
        {
            var key = new TargetKey(observation.RegionId, observation.Color);
            if (!_stateMachines.TryGetValue(key, out var stateMachine))
            {
                throw new InvalidOperationException("收到未知監看區域或顏色的取樣結果。");
            }

            var transition = stateMachine.Observe(observation.MatchedRatio, settings);
            shouldAlert |= transition == AlertTransition.Triggered;
            results.Add(new TargetAlertResult(
                observation.RegionId,
                observation.Color,
                transition,
                stateMachine.IsPresent));
        }

        return new MultiRegionAlertResult(results, shouldAlert);
    }

    public bool IsPresent(Guid regionId) =>
        Enum.GetValues<MonitoredColor>().Any(color => IsPresent(regionId, color));

    public bool IsPresent(Guid regionId, MonitoredColor color) =>
        _stateMachines.TryGetValue(new TargetKey(regionId, color), out var stateMachine) &&
        stateMachine.IsPresent;

    public void Reset(Guid regionId)
    {
        foreach (var color in Enum.GetValues<MonitoredColor>())
        {
            if (_stateMachines.TryGetValue(new TargetKey(regionId, color), out var stateMachine))
            {
                stateMachine.Reset();
            }
        }
    }

    public void ResetAll()
    {
        foreach (var stateMachine in _stateMachines.Values)
        {
            stateMachine.Reset();
        }
    }

    private readonly record struct TargetKey(Guid RegionId, MonitoredColor Color);
}
