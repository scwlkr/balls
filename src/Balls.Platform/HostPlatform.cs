using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Balls.Platform;

public sealed record HostDefaults(
    string DataDirectory,
    string LocalControlEndpoint,
    string NodeDisplayName,
    string LocalControlListenerDescription,
    string LocalControlEndpointDescription);

public interface ILocalStatePreparer
{
    string Prepare(string dataDirectory);

    void WriteNewPrivateFile(string path, ReadOnlyMemory<byte> content);
}

public interface ILocalControlServerTransport
{
    void ValidateEndpoint(string endpoint);

    void PrepareEndpoint(string endpoint);

    void ConfigureServices(IServiceCollection services);

    void ConfigureServer(KestrelServerOptions serverOptions, string endpoint);

    void SecureEndpoint(string endpoint);

    void CleanupEndpoint(string endpoint);
}

public interface ILocalControlClientTransport
{
    HttpClient CreateClient(string endpoint, TimeSpan? timeout = null);
}

public interface ISystemBrowserLauncher
{
    void Open(Uri uri);
}

public sealed record HostPlatform(
    HostDefaults Defaults,
    ILocalStatePreparer LocalState,
    ILocalControlServerTransport LocalControlServer,
    ILocalControlClientTransport LocalControlClient,
    ISystemBrowserLauncher SystemBrowser,
    ICircleFilesFolderPicker CircleFilesFolderPicker,
    ICircleFilesReadinessInspector CircleFilesReadiness,
    ICircleFilesHostProvisioner CircleFilesHosting,
    ICircleFilesGrantCredentialProvisioner CircleFilesGrantCredentials,
    ICircleFilesMemberMapper CircleFilesMemberMapping,
    ICircleFilesLocationLauncher CircleFilesLocationLauncher,
    ICircleFilesLifecycleManager CircleFilesLifecycle);
