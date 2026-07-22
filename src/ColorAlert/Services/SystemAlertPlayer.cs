using System.Media;

namespace ColorAlert.Services;

internal interface IAlertPlayer
{
    void Play();
}

internal sealed class SystemAlertPlayer : IAlertPlayer
{
    public void Play() => SystemSounds.Exclamation.Play();
}

