namespace ColorAlert.Core;

public readonly record struct RegionObservation(Guid RegionId, double MismatchRatio);

public readonly record struct RegionAlertResult(
    Guid RegionId,
    AlertTransition Transition,
    bool IsAlerted);

public sealed record MultiRegionAlertResult(
    IReadOnlyList<RegionAlertResult> Regions,
    bool ShouldAlert);

public sealed class MultiRegionAlertCoordinator
{
    private readonly Dictionary<Guid, AlertStateMachine> _stateMachines = [];

    public void Synchronize(IEnumerable<Guid> regionIds)
    {
        ArgumentNullException.ThrowIfNull(regionIds);
        var currentIds = regionIds.Where(id => id != Guid.Empty).ToHashSet();

        foreach (var removedId in _stateMachines.Keys.Except(currentIds).ToArray())
        {
            _stateMachines.Remove(removedId);
        }

        foreach (var regionId in currentIds)
        {
            _stateMachines.TryAdd(regionId, new AlertStateMachine());
        }
    }

    public MultiRegionAlertResult Observe(
        IEnumerable<RegionObservation> observations,
        DetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(settings);

        var results = new List<RegionAlertResult>();
        var shouldAlert = false;

        foreach (var observation in observations)
        {
            if (!_stateMachines.TryGetValue(observation.RegionId, out var stateMachine))
            {
                throw new InvalidOperationException("收到未知監看區域的取樣結果。");
            }

            var transition = stateMachine.Observe(observation.MismatchRatio, settings);
            shouldAlert |= transition == AlertTransition.Triggered;
            results.Add(new RegionAlertResult(
                observation.RegionId,
                transition,
                stateMachine.IsAlerted));
        }

        return new MultiRegionAlertResult(results, shouldAlert);
    }

    public bool IsAlerted(Guid regionId) =>
        _stateMachines.TryGetValue(regionId, out var stateMachine) && stateMachine.IsAlerted;

    public void Reset(Guid regionId)
    {
        if (_stateMachines.TryGetValue(regionId, out var stateMachine))
        {
            stateMachine.Reset();
        }
    }

    public void ResetAll()
    {
        foreach (var stateMachine in _stateMachines.Values)
        {
            stateMachine.Reset();
        }
    }
}
