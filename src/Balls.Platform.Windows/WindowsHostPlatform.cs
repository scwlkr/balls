using System.Runtime.Versioning;
using Balls.Platform;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsHostPlatform
{
    public static HostPlatform Create()
    {
        var transport = new WindowsLocalControlTransport();
        return new HostPlatform(
            new HostDefaults(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Balls"),
                WindowsNamedPipeDefaults.GetCurrentUserPipeName(),
                Environment.MachineName,
                "named pipe",
                "pipe"),
            new WindowsLocalStatePreparer(),
            transport,
            transport,
            new WindowsSystemBrowserLauncher());
    }

    private sealed class WindowsLocalStatePreparer : ILocalStatePreparer
    {
        public string Prepare(string dataDirectory)
        {
            return WindowsDataDirectorySecurity.Prepare(dataDirectory);
        }
    }

    private sealed class WindowsLocalControlTransport :
        ILocalControlServerTransport,
        ILocalControlClientTransport
    {
        public void ValidateEndpoint(string endpoint)
        {
            WindowsNamedPipeControl.ValidatePipeName(endpoint);
        }

        public void PrepareEndpoint(string endpoint)
        {
            ValidateEndpoint(endpoint);
        }

        public void ConfigureServices(IServiceCollection services)
        {
            WindowsNamedPipeControl.ConfigureServices(services);
        }

        public void ConfigureServer(KestrelServerOptions serverOptions, string endpoint)
        {
            WindowsNamedPipeControl.ConfigureServer(serverOptions, endpoint);
        }

        public void SecureEndpoint(string endpoint)
        {
            ValidateEndpoint(endpoint);
        }

        public void CleanupEndpoint(string endpoint)
        {
            ValidateEndpoint(endpoint);
        }

        public HttpClient CreateClient(string endpoint, TimeSpan? timeout = null)
        {
            return WindowsNamedPipeHttpClient.Create(endpoint, timeout);
        }
    }

    private sealed class WindowsSystemBrowserLauncher : ISystemBrowserLauncher
    {
        public void Open(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true,
                });
            if (process is null)
            {
                throw new IOException("Windows did not start the default browser.");
            }
        }
    }
}
