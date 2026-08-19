using System.Net.Http.Json;
using Balls.Daemon;
using Balls.Host;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

const string readyPrefix = "BALLS_BROWSER_READY ";
var identifier = Guid.NewGuid().ToString("N");
var root = OperatingSystem.IsLinux()
    ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local",
        "state",
        "balls-browser-tests",
        identifier)
    : Path.Combine(Path.GetTempPath(), "balls-browser-tests", identifier);
var stateDirectory = Path.Combine(root, "state");
var endpoint = OperatingSystem.IsWindows()
    ? $"balls-browser-tests-{identifier}"
    : Path.Combine(root, "runtime", "control.sock");
DaemonInstance? daemon = null;

try
{
    daemon = await StartAsync();
    await WriteLaunchAsync();

    while (await Console.In.ReadLineAsync() is { } command)
    {
        if (string.Equals(command, "restart", StringComparison.Ordinal))
        {
            await daemon.DisposeAsync();
            daemon = await StartAsync();
            await WriteLaunchAsync();
        }
        else if (string.Equals(command, "quit", StringComparison.Ordinal))
        {
            break;
        }
    }
}
finally
{
    if (daemon is not null)
    {
        await daemon.DisposeAsync();
    }

    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

async Task<DaemonInstance> StartAsync() => await DaemonHost.StartAsync(
    new DaemonOptions(stateDirectory, endpoint, "Browser-PC"));

async Task WriteLaunchAsync()
{
    var selection = HostPlatformSelector.SelectCurrent();
    if (selection is not SupportedHostPlatform supported)
    {
        throw new PlatformNotSupportedException("The browser harness requires a supported host.");
    }

    using var client = supported.Platform.LocalControlClient.CreateClient(endpoint);
    using var response = await client.PostAsync(ControlRoutes.BrowserLaunch, null);
    response.EnsureSuccessStatusCode();
    var launch = await response.Content.ReadFromJsonAsync<LaunchBrowserResponse>(ControlJson.Options)
        ?? throw new InvalidOperationException("ballsd returned an empty browser launch response.");
    Console.WriteLine(readyPrefix + launch.Url);
}
