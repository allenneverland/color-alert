using ColorAlert.Core;

namespace ColorAlert.Services;

internal sealed class MonitorStatusChangedEventArgs(
    MonitorStatus status,
    string? errorMessage = null) : EventArgs
{
    internal MonitorStatus Status { get; } = status;

    internal string? ErrorMessage { get; } = errorMessage;
}

internal sealed class RegionStatusChangedEventArgs(
    Guid regionId,
    RegionMonitorStatus status,
    double yellowRatio,
    double blueRatio,
    bool yellowPresent,
    bool bluePresent,
    string? errorMessage = null) : EventArgs
{
    internal Guid RegionId { get; } = regionId;

    internal RegionMonitorStatus Status { get; } = status;

    internal double YellowRatio { get; } = yellowRatio;

    internal double BlueRatio { get; } = blueRatio;

    internal bool YellowPresent { get; } = yellowPresent;

    internal bool BluePresent { get; } = bluePresent;

    internal string? ErrorMessage { get; } = errorMessage;
}

internal sealed class MonitorController : IAsyncDisposable
{
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly GdiScreenSampler _screenSampler;
    private readonly IAlertPlayer _alertPlayer;
    private readonly MultiRegionAlertCoordinator _coordinator = new();
    private readonly Dictionary<Guid, RegionRuntime> _regions = [];

    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private DetectionSettings _settings = new();
    private AlertRepeatMode _alertMode = AlertRepeatMode.Once;
    private MonitorStatus _status = MonitorStatus.Idle;
    private bool _disposed;

    internal MonitorController(GdiScreenSampler screenSampler, IAlertPlayer alertPlayer)
    {
        _screenSampler = screenSampler;
        _alertPlayer = alertPlayer;
    }

    internal event EventHandler<MonitorStatusChangedEventArgs>? StatusChanged;

    internal event EventHandler<RegionStatusChangedEventArgs>? RegionStatusChanged;

    internal bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _monitorTask is { IsCompleted: false };
            }
        }
    }

    internal MonitorStatus Status
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    internal void UpdateSettings(DetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_stateLock)
        {
            _settings = settings.Normalize();
        }
    }

    internal void UpdateAlertMode(AlertRepeatMode alertMode)
    {
        lock (_stateLock)
        {
            _alertMode = Enum.IsDefined(alertMode) ? alertMode : AlertRepeatMode.Once;
        }
    }

    internal void SynchronizeRegions(IReadOnlyList<MonitoredRegionDefinition> definitions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definitions);

        if (definitions.Count > AppSettings.MaximumRegionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitions),
                $"最多只能監看 {AppSettings.MaximumRegionCount} 個區域。");
        }

        List<RegionStatusChangedEventArgs> changes = [];
        lock (_stateLock)
        {
            EnsureStopped();
            var desiredIds = definitions.Select(definition => definition.Id).ToHashSet();
            foreach (var removedId in _regions.Keys.Except(desiredIds).ToArray())
            {
                _regions.Remove(removedId);
            }

            foreach (var definition in definitions)
            {
                if (definition.Id == Guid.Empty || !definition.Bounds.IsValid)
                {
                    throw new ArgumentException("監看區域包含無效資料。", nameof(definitions));
                }

                if (!_regions.TryGetValue(definition.Id, out var runtime))
                {
                    runtime = new RegionRuntime(definition);
                    _regions.Add(definition.Id, runtime);
                }
                else if (runtime.Definition.Bounds != definition.Bounds)
                {
                    runtime.Definition = definition;
                    ResetRuntime(runtime);
                    _coordinator.Reset(definition.Id);
                }
                else
                {
                    runtime.Definition = definition;
                }

                changes.Add(CreateEventArgs(runtime));
            }

            _coordinator.Synchronize(desiredIds);
        }

        RaiseRegionChanges(changes);
    }

    internal void ResetDetections()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        List<RegionStatusChangedEventArgs> changes = [];

        lock (_stateLock)
        {
            EnsureStopped();
            _coordinator.ResetAll();
            foreach (var runtime in _regions.Values)
            {
                ResetRuntime(runtime);
                changes.Add(CreateEventArgs(runtime));
            }
        }

        RaiseRegionChanges(changes);
    }

    internal async Task StartAsync(
        IReadOnlyList<MonitoredRegionDefinition> definitions,
        DetectionSettings settings,
        AlertRepeatMode alertMode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definitions);

        if (definitions.Count is 0 or > AppSettings.MaximumRegionCount)
        {
            throw new ArgumentException("監看區域數量無效。", nameof(definitions));
        }

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            var monitorCancellation = new CancellationTokenSource();

            lock (_stateLock)
            {
                SynchronizeRegionsCore(definitions);
                _settings = settings.Normalize();
                _alertMode = Enum.IsDefined(alertMode) ? alertMode : AlertRepeatMode.Once;
                _monitorCancellation = monitorCancellation;
                _monitorTask = Task.Run(
                    () => MonitorLoopAsync(monitorCancellation.Token),
                    CancellationToken.None);
            }

            SetStatus(GetAggregateStatus());
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            await StopCoreAsync();
            SetStatus(MonitorStatus.Paused);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate.WaitAsync();
        try
        {
            await StopCoreAsync();
            _screenSampler.Dispose();
            _disposed = true;
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        DetectionSettings initialSettings;
        lock (_stateLock)
        {
            initialSettings = _settings;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(initialSettings.SampleIntervalMilliseconds));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DetectionSettings currentSettings;
                AlertRepeatMode currentAlertMode;
                RegionSnapshot[] snapshots;
                lock (_stateLock)
                {
                    currentSettings = _settings;
                    currentAlertMode = _alertMode;
                    snapshots = _regions.Values
                        .Select(runtime => new RegionSnapshot(
                            runtime.Definition,
                            runtime.RetryAfterUtc))
                        .ToArray();
                }

                var samples = new List<RegionSample>(snapshots.Length);
                var errorChanges = new List<RegionStatusChangedEventArgs>();
                var now = DateTimeOffset.UtcNow;

                foreach (var snapshot in snapshots)
                {
                    if (snapshot.RetryAfterUtc > now)
                    {
                        continue;
                    }

                    try
                    {
                        var statistics = _screenSampler.SampleTargetColors(
                            snapshot.Definition.Bounds,
                            currentSettings.ColorTolerance);
                        samples.Add(new RegionSample(snapshot.Definition.Id, statistics));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        lock (_stateLock)
                        {
                            if (_regions.TryGetValue(snapshot.Definition.Id, out var runtime))
                            {
                                runtime.Status = RegionMonitorStatus.Error;
                                runtime.ErrorMessage = exception.Message;
                                runtime.RetryAfterUtc = now.AddSeconds(1);
                                errorChanges.Add(CreateEventArgs(runtime));
                            }
                        }
                    }
                }

                RaiseRegionChanges(errorChanges);

                var observations = samples.SelectMany(sample => new[]
                {
                    new TargetObservation(
                        sample.RegionId,
                        MonitoredColor.Yellow,
                        sample.Statistics.YellowRatio),
                    new TargetObservation(
                        sample.RegionId,
                        MonitoredColor.Blue,
                        sample.Statistics.BlueRatio),
                });

                MultiRegionAlertResult alertResult;
                List<RegionStatusChangedEventArgs> sampleChanges = [];
                lock (_stateLock)
                {
                    alertResult = _coordinator.Observe(observations, currentSettings);
                    foreach (var sample in samples)
                    {
                        if (!_regions.TryGetValue(sample.RegionId, out var runtime))
                        {
                            continue;
                        }

                        runtime.YellowRatio = sample.Statistics.YellowRatio;
                        runtime.BlueRatio = sample.Statistics.BlueRatio;
                        runtime.YellowPresent = _coordinator.IsPresent(
                            sample.RegionId,
                            MonitoredColor.Yellow);
                        runtime.BluePresent = _coordinator.IsPresent(
                            sample.RegionId,
                            MonitoredColor.Blue);
                        runtime.Status = runtime.YellowPresent || runtime.BluePresent
                            ? RegionMonitorStatus.Alerted
                            : RegionMonitorStatus.Monitoring;
                        runtime.ErrorMessage = null;
                        runtime.RetryAfterUtc = DateTimeOffset.MinValue;
                        sampleChanges.Add(CreateEventArgs(runtime));
                    }
                }

                RaiseRegionChanges(sampleChanges);
                SetStatus(GetAggregateStatus());

                if (alertResult.ShouldAlert)
                {
                    await _alertPlayer.PlayAsync(currentAlertMode, cancellationToken);
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when monitoring is paused or the application exits.
        }
    }

    private async Task StopCoreAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;

        lock (_stateLock)
        {
            cancellation = _monitorCancellation;
            task = _monitorTask;
            _monitorCancellation = null;
            _monitorTask = null;
        }

        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync();
        if (task is not null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // The monitor loop owns cancellation and normally absorbs this exception.
            }
        }

        cancellation.Dispose();
    }

    private void SynchronizeRegionsCore(IReadOnlyList<MonitoredRegionDefinition> definitions)
    {
        var desiredIds = definitions.Select(definition => definition.Id).ToHashSet();
        if (!_regions.Keys.ToHashSet().SetEquals(desiredIds) ||
            definitions.Any(definition =>
                !_regions.TryGetValue(definition.Id, out var runtime) ||
                runtime.Definition.Bounds != definition.Bounds))
        {
            throw new InvalidOperationException("監看區域已變更，請先同步區域設定。");
        }
    }

    private MonitorStatus GetAggregateStatus()
    {
        lock (_stateLock)
        {
            if (_regions.Values.Any(runtime => runtime.Status == RegionMonitorStatus.Error))
            {
                return MonitorStatus.Error;
            }

            return _regions.Values.Any(runtime => _coordinator.IsPresent(runtime.Definition.Id))
                ? MonitorStatus.Alerted
                : MonitorStatus.Monitoring;
        }
    }

    private void EnsureStopped()
    {
        if (_monitorTask is { IsCompleted: false })
        {
            throw new InvalidOperationException("變更監看區域前必須先暫停監看。");
        }
    }

    private void SetStatus(MonitorStatus status, string? errorMessage = null)
    {
        lock (_stateLock)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, new MonitorStatusChangedEventArgs(status, errorMessage));
    }

    private static void ResetRuntime(RegionRuntime runtime)
    {
        runtime.YellowRatio = 0d;
        runtime.BlueRatio = 0d;
        runtime.YellowPresent = false;
        runtime.BluePresent = false;
        runtime.Status = RegionMonitorStatus.Monitoring;
        runtime.ErrorMessage = null;
        runtime.RetryAfterUtc = DateTimeOffset.MinValue;
    }

    private static RegionStatusChangedEventArgs CreateEventArgs(RegionRuntime runtime) =>
        new(
            runtime.Definition.Id,
            runtime.Status,
            runtime.YellowRatio,
            runtime.BlueRatio,
            runtime.YellowPresent,
            runtime.BluePresent,
            runtime.ErrorMessage);

    private void RaiseRegionChanges(IEnumerable<RegionStatusChangedEventArgs> changes)
    {
        foreach (var change in changes)
        {
            RegionStatusChanged?.Invoke(this, change);
        }
    }

    private sealed class RegionRuntime(MonitoredRegionDefinition definition)
    {
        internal MonitoredRegionDefinition Definition { get; set; } = definition;

        internal RegionMonitorStatus Status { get; set; } = RegionMonitorStatus.Monitoring;

        internal double YellowRatio { get; set; }

        internal double BlueRatio { get; set; }

        internal bool YellowPresent { get; set; }

        internal bool BluePresent { get; set; }

        internal string? ErrorMessage { get; set; }

        internal DateTimeOffset RetryAfterUtc { get; set; }
    }

    private sealed record RegionSnapshot(
        MonitoredRegionDefinition Definition,
        DateTimeOffset RetryAfterUtc);

    private readonly record struct RegionSample(
        Guid RegionId,
        TargetColorStatistics Statistics);
}
