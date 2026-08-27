using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Balls.Protocol.Remote.V1;
using Balls.Transport.Lan;

namespace Balls.Daemon;

internal sealed record AutomaticPrivateListenerPorts(int AdmissionPort, int MessagePort)
{
    internal static AutomaticPrivateListenerPorts FromBoundAddresses(
        RemoteTransportAddress admissionAddress,
        RemoteTransportAddress messageAddress) => new(
            IPEndPoint.Parse(admissionAddress.Value).Port,
            IPEndPoint.Parse(messageAddress.Value).Port);

    internal void Validate()
    {
        if (AdmissionPort is <= 0 or > 65535
            || MessagePort is <= 0 or > 65535
            || AdmissionPort == MessagePort)
        {
            throw new InvalidDataException("The automatic private listener port record is invalid.");
        }
    }
}

internal static class AutomaticPrivateListenerPortStore
{
    internal const string FileName = "automatic-private-listeners-v1.json";
    private const int MaximumBytes = 4096;

    internal static AutomaticPrivateListenerPorts? Load(string dataDirectory)
    {
        var path = GetPath(dataDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumBytes)
        {
            throw InvalidRecord();
        }

        try
        {
            var document = JsonSerializer.Deserialize<PortDocument>(File.ReadAllBytes(path));
            if (document is null || document.SchemaVersion != 1)
            {
                throw InvalidRecord();
            }

            var ports = new AutomaticPrivateListenerPorts(
                document.AdmissionPort,
                document.MessagePort);
            ports.Validate();
            return ports;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The automatic private listener port record is invalid.",
                exception);
        }
    }

    internal static void SaveIfMissing(
        string dataDirectory,
        AutomaticPrivateListenerPorts ports)
    {
        ports.Validate();
        var existing = Load(dataDirectory);
        if (existing is not null)
        {
            if (existing != ports)
            {
                throw InvalidRecord();
            }
            return;
        }

        var path = GetPath(dataDirectory);
        var parentDirectory = Path.GetDirectoryName(dataDirectory)
            ?? throw new InvalidDataException("The automatic private listener port record path is invalid.");
        var temporaryPath = Path.Combine(
            parentDirectory,
            $".balls-{FileName}.{Guid.NewGuid():N}.tmp");
        var content = JsonSerializer.SerializeToUtf8Bytes(
            new PortDocument(1, ports.AdmissionPort, ports.MessagePort));
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path);
        }
        catch (IOException) when (File.Exists(path))
        {
            var winner = Load(dataDirectory);
            if (winner != ports)
            {
                throw InvalidRecord();
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string GetPath(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return Path.Combine(dataDirectory, FileName);
    }

    private static InvalidDataException InvalidRecord() => new(
        "The automatic private listener port record is invalid.");

    private sealed record PortDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("admissionPort")] int AdmissionPort,
        [property: JsonPropertyName("messagePort")] int MessagePort);
}
