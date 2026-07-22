using ColorAlert.Core;
using System.Media;

namespace ColorAlert.Services;

internal interface IAlertPlayer
{
    Task PlayAsync(AlertRepeatMode repeatMode, CancellationToken cancellationToken = default);
}

internal sealed class SystemAlertPlayer : IAlertPlayer
{
    public async Task PlayAsync(
        AlertRepeatMode repeatMode,
        CancellationToken cancellationToken = default)
    {
        var repeatCount = repeatMode == AlertRepeatMode.ThreeTimes ? 3 : 1;

        for (var index = 0; index < repeatCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemSounds.Exclamation.Play();

            if (index < repeatCount - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
    }
}
