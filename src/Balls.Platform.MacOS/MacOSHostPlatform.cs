using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Balls.Platform;

namespace Balls.Platform.MacOS;

[SupportedOSPlatform("macos")]
public static class MacOSHostPlatform
{
    public static HostPlatform Create()
    {
        var transport = new MacOSUnixSocketControl();
        return new HostPlatform(
            new HostDefaults(
                GetDefaultStateDirectory(),
                GetDefaultSocketPath(),
                Environment.MachineName,
                "Unix-domain socket",
                "socket"),
            new MacOSLocalStatePreparer(),
            transport,
            transport,
            new MacOSSystemBrowserLauncher(),
            new UnsupportedCircleFilesReadinessInspector(),
            new UnsupportedCircleFilesHostProvisioner(),
            new UnsupportedCircleFilesGrantCredentialProvisioner());
    }

    public static string GetDefaultStateDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Path.IsPathFullyQualified(userProfile))
        {
            throw new InvalidOperationException(
                "macOS host defaults require an absolute user profile directory.");
        }

        return Path.Combine(
            Path.GetFullPath(userProfile),
            "Library",
            "Application Support",
            "Balls");
    }

    public static string GetDefaultSocketPath()
    {
        var runtimeRoot = Path.GetTempPath();
        if (!Path.IsPathFullyQualified(runtimeRoot))
        {
            runtimeRoot = Path.Combine(
                "/private/tmp",
                $"balls-{MacOSNativeFileSystem.EffectiveUserId.ToString(CultureInfo.InvariantCulture)}");
        }

        var fullRuntimeRoot = Path.GetFullPath(runtimeRoot);
        if (fullRuntimeRoot.StartsWith("/var/", StringComparison.Ordinal))
        {
            fullRuntimeRoot = "/private" + fullRuntimeRoot;
        }

        return Path.Combine(fullRuntimeRoot, "balls", "control.sock");
    }

    private sealed class MacOSLocalStatePreparer : ILocalStatePreparer
    {
        public string Prepare(string dataDirectory)
        {
            return MacOSDataDirectorySecurity.Prepare(dataDirectory);
        }
    }

    private sealed class MacOSSystemBrowserLauncher : ISystemBrowserLauncher
    {
        public void Open(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            var startInfo = new ProcessStartInfo("/usr/bin/open")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(uri.AbsoluteUri);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new IOException("macOS did not start the default browser.");
            }
        }
    }
}
