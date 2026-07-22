using ColorAlert.Core;
using ColorAlert.Interop;

namespace ColorAlert.Services;

internal static class DisplayService
{
    internal static ScreenRegion GetVirtualScreenBounds() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));

    internal static bool IsAvailable(ScreenRegion region) =>
        region.IsContainedBy(GetVirtualScreenBounds());
}

