using System.Runtime.Versioning;
using Balls.Platform;

namespace Balls.Platform.Linux;

[SupportedOSPlatform("linux")]
public static class LinuxHostPlatform
{
    public static HostPlatform Create()
    {
        var stateDirectory = GetDefaultStateDirectory();
        var transport = new LinuxUnixSocketControl();
        return new HostPlatform(
            new HostDefaults(
                stateDirectory,
                GetDefaultSocketPath(),
                Environment.MachineName,
                "Unix-domain socket",
                "socket"),
            new LinuxLocalStatePreparer(),
            transport,
            transport,
            new LinuxSystemBrowserLauncher(),
            new UnsupportedCircleFilesReadinessInspector());
    }

    private sealed class LinuxSystemBrowserLauncher : ISystemBrowserLauncher
    {
        public void Open(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            var startInfo = new System.Diagnostics.ProcessStartInfo("xdg-open")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(uri.AbsoluteUri);
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                throw new IOException("Linux did not start the default browser.");
            }
        }
    }

    public static string GetDefaultStateDirectory()
    {
        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgStateHome) && Path.IsPathFullyQualified(xdgStateHome))
        {
            return Path.Combine(Path.GetFullPath(xdgStateHome), "balls");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Path.IsPathFullyQualified(userProfile))
        {
            throw new InvalidOperationException(
                "Linux host defaults require an absolute HOME or XDG_STATE_HOME.");
        }

        return Path.Combine(Path.GetFullPath(userProfile), ".local", "state", "balls");
    }

    public static string GetDefaultSocketPath()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory)
            || !Path.IsPathFullyQualified(runtimeDirectory))
        {
            var standardRuntimeDirectory = Path.Combine(
                "/run/user",
                LinuxNativeFileSystem.EffectiveUserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            runtimeDirectory = Directory.Exists(standardRuntimeDirectory)
                ? standardRuntimeDirectory
                : Path.Combine(
                    Path.GetTempPath(),
                    $"balls-runtime-{LinuxNativeFileSystem.EffectiveUserId}");
        }

        return Path.Combine(Path.GetFullPath(runtimeDirectory), "balls", "control.sock");
    }

    private sealed class LinuxLocalStatePreparer : ILocalStatePreparer
    {
        public string Prepare(string dataDirectory)
        {
            return LinuxDataDirectorySecurity.Prepare(dataDirectory);
        }
    }
}
