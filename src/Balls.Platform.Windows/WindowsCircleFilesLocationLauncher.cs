using System.Diagnostics;
using System.Runtime.Versioning;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal interface IWindowsCircleFilesLocationProcess
{
    bool Start(ProcessStartInfo startInfo);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesLocationLauncher : ICircleFilesLocationLauncher
{
    private readonly IWindowsCircleFilesLocationProcess process;

    public WindowsCircleFilesLocationLauncher() : this(new WindowsCircleFilesLocationProcess())
    {
    }

    internal WindowsCircleFilesLocationLauncher(IWindowsCircleFilesLocationProcess process)
    {
        this.process = process;
    }

    public ValueTask OpenAsync(
        CircleFilesMappedLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(location);
        var drive = location.DriveLetter?.Trim().ToUpperInvariant();
        if (drive is null || drive.Length != 1 || drive[0] is < 'D' or > 'Z')
        {
            throw new CircleFilesHostingException(
                "mapping_request_invalid",
                "The Circle Files Explorer location is invalid.");
        }

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add($@"{drive}:\");
        try
        {
            if (!process.Start(startInfo))
            {
                throw LaunchFailed();
            }
        }
        catch (CircleFilesHostingException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and not OutOfMemoryException)
        {
            throw LaunchFailed();
        }

        return ValueTask.CompletedTask;
    }

    private static CircleFilesHostingException LaunchFailed() => new(
        "explorer_launch_failed",
        "The shared folder is connected, but File Explorer did not open. Try again.");
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesLocationProcess : IWindowsCircleFilesLocationProcess
{
    public bool Start(ProcessStartInfo startInfo)
    {
        using var started = Process.Start(startInfo);
        return started is not null;
    }
}
