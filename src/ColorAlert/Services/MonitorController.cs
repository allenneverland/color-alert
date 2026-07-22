using ColorAlert.Core;

namespace ColorAlert.Services;

internal sealed class MonitorStatusChangedEventArgs(
    MonitorStatus status,
    string? errorMessage = null) : EventArgs
{
    internal MonitorStatus Status { get; } = status;

    internal string? ErrorMessage { get; } = errorMessage;
}

internal sealed class MonitorController : IAsyncDisposable
{
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly GdiScreenSampler _screenSampler;
    private readonly IAlertPlayer _alertPlayer;
    private readonly AlertStateMachine _stateMachine = new();

    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private DetectionSettings _settings = new();
    private MonitorStatus _status = MonitorStatus.Idle;
    private bool _disposed;

    internal MonitorController(GdiScreenSampler screenSampler, IAlertPlayer alertPlayer)
    {
        _screenSampler = screenSampler;
        _alertPlayer = alertPlayer;
    }

    internal event EventHandler<MonitorStatusChangedEventArgs>? StatusChanged;

    internal event EventHandler<SampleStatistics>? Sampled;

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

    internal void ResetDetection() => _stateMachine.Reset();

    internal async Task StartAsync(
        ScreenRegion region,
        DetectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!region.IsValid)
        {
            throw new ArgumentException("監看區域無效。", nameof(region));
        }

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            var monitorCancellation = new CancellationTokenSource();

            lock (_stateLock)
            {
                _settings = settings.Normalize();
                _monitorCancellation = monitorCancellation;
                _monitorTask = Task.Run(
                    () => MonitorLoopAsync(region, monitorCancellation.Token),
                    CancellationToken.None);
            }

            SetStatus(_stateMachine.IsAlerted ? MonitorStatus.Alerted : MonitorStatus.Monitoring);
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

    private async Task MonitorLoopAsync(ScreenRegion region, CancellationToken cancellationToken)
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
                lock (_stateLock)
                {
                    currentSettings = _settings;
                }

                try
                {
                    var statistics = _screenSampler.Sample(region, currentSettings.BlackTolerance);
                    Sampled?.Invoke(this, statistics);

                    var transition = _stateMachine.Observe(
                        statistics.NonBlackRatio,
                        currentSettings);

                    switch (transition)
                    {
                        case AlertTransition.Triggered:
                            _alertPlayer.Play();
                            SetStatus(MonitorStatus.Alerted);
                            break;

                        case AlertTransition.Rearmed:
                            SetStatus(MonitorStatus.Monitoring);
                            break;

                        case AlertTransition.None when Status == MonitorStatus.Error:
                            SetStatus(_stateMachine.IsAlerted
                                ? MonitorStatus.Alerted
                                : MonitorStatus.Monitoring);
                            break;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    SetStatus(MonitorStatus.Error, exception.Message);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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

    private void SetStatus(MonitorStatus status, string? errorMessage = null)
    {
        lock (_stateLock)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, new MonitorStatusChangedEventArgs(status, errorMessage));
    }
}
