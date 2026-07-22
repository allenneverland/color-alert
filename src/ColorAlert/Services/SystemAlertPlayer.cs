using ColorAlert.Core;
using System.IO;
using System.Media;
using System.Reflection;

namespace ColorAlert.Services;

internal interface IAlertPlayer
{
    Task PlayAsync(AlertRepeatMode repeatMode, CancellationToken cancellationToken = default);
}

internal sealed class SystemAlertPlayer : IAlertPlayer, IAsyncDisposable
{
    private const string AlertSoundResourceName = "ColorAlert.Assets.AlertSiren.wav";
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(500);

    private readonly byte[] _alertSound = LoadAlertSound();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private int _disposeStarted;

    public async Task PlayAsync(
        AlertRepeatMode repeatMode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeStarted) != 0,
            this);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var hasPlaybackGate = false;

        try
        {
            await _playbackGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            hasPlaybackGate = true;
            await PlaySequenceAsync(repeatMode, linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            if (hasPlaybackGate)
            {
                _playbackGate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        await _lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        await _playbackGate.WaitAsync().ConfigureAwait(false);
        _playbackGate.Release();
        _playbackGate.Dispose();
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PlaySequenceAsync(
        AlertRepeatMode repeatMode,
        CancellationToken cancellationToken)
    {
        var repeatCount = repeatMode == AlertRepeatMode.ThreeTimes ? 3 : 1;
        using var soundStream = new MemoryStream(_alertSound, writable: false);
        using var player = new SoundPlayer(soundStream);
        using var cancellationRegistration = cancellationToken.UnsafeRegister(
            static state => ((SoundPlayer)state!).Stop(),
            player);

        await Task.Run(
            () =>
            {
                player.Load();
                cancellationToken.ThrowIfCancellationRequested();
            },
            CancellationToken.None).ConfigureAwait(false);

        for (var index = 0; index < repeatCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(player.PlaySync, CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (index < repeatCount - 1)
            {
                await Task.Delay(RepeatDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static byte[] LoadAlertSound()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var resourceStream = assembly.GetManifestResourceStream(AlertSoundResourceName)
            ?? throw new InvalidOperationException("找不到內嵌的警笛提示音。");
        using var buffer = new MemoryStream();
        resourceStream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
