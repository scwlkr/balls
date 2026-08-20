using Balls.Core;
using Balls.Platform;
using Balls.Platform.Linux;
using Balls.Platform.Windows;
using Balls.Security.Linux;
using Balls.Security.Windows;

namespace Balls.Host;

public enum HostOperatingSystem
{
    Windows,
    Linux,
    MacOS,
    Unknown,
}

public abstract record HostSelectionResult;

public sealed record SupportedHostPlatform(
    HostPlatform Platform,
    IPrivateMaterialProtector PrivateMaterialProtector) : HostSelectionResult;

public sealed record UnsupportedHostPlatform(HostOperatingSystem OperatingSystem) : HostSelectionResult
{
    public string PlatformName => OperatingSystem.ToString().ToLowerInvariant();

    public string Message => $"the local host platform '{PlatformName}' is not supported yet.";
}

public static class HostPlatformSelector
{
    public static HostSelectionResult SelectCurrent()
    {
        return Select(DetectCurrent());
    }

    public static HostSelectionResult Select(HostOperatingSystem operatingSystem)
    {
        if (operatingSystem == HostOperatingSystem.Windows && OperatingSystem.IsWindows())
        {
            return new SupportedHostPlatform(
                WindowsHostPlatform.Create(),
                new WindowsCurrentUserPrivateMaterialProtector());
        }

        if (operatingSystem == HostOperatingSystem.Linux && OperatingSystem.IsLinux())
        {
            return new SupportedHostPlatform(
                LinuxHostPlatform.Create(),
                new LinuxOwnedStatePrivateMaterialProtector());
        }

        return new UnsupportedHostPlatform(operatingSystem);
    }

    private static HostOperatingSystem DetectCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            return HostOperatingSystem.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return HostOperatingSystem.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return HostOperatingSystem.MacOS;
        }

        return HostOperatingSystem.Unknown;
    }
}
