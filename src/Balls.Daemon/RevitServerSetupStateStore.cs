using System.Text.Json;
using System.Text.Json.Serialization;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal sealed record RevitServerSetupState(
    int SchemaVersion,
    long Revision,
    string AttemptId,
    string Stage,
    string Summary,
    string MediaPath,
    string PlanDigest,
    RevitServerSetupPlanResponse Plan,
    IReadOnlyList<RevitServerReadinessCheckResponse> Checks,
    DateTimeOffset UpdatedAtUtc);

internal interface IRevitServerSetupStateStore
{
    RevitServerSetupState? Load();
    void Save(RevitServerSetupState value);
}

internal sealed class FileRevitServerSetupStateStore(string path) : IRevitServerSetupStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public RevitServerSetupState? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length is 0 or > 128 * 1024)
            {
                throw new InvalidDataException("The Revit setup state is outside its size limit.");
            }

            var value = JsonSerializer.Deserialize<RevitServerSetupState>(bytes, Options);
            if (value is not { SchemaVersion: 1 } || !RevitServerSetupStages.All.Contains(value.Stage))
            {
                throw new InvalidDataException("The Revit setup state is invalid.");
            }

            return value;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("The Revit setup state could not be read.", exception);
        }
    }

    public void Save(RevitServerSetupState value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException();
        Directory.CreateDirectory(directory);
        var temporary = path + ".new";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed class MemoryRevitServerSetupStateStore : IRevitServerSetupStateStore
{
    private RevitServerSetupState? value;
    public RevitServerSetupState? Load() => value;
    public void Save(RevitServerSetupState state) => value = state;
}

internal static class RevitServerSetupStages
{
    internal const string ApplyingPrerequisites = "applying-prerequisites";
    internal const string PrerequisitesApplied = "prerequisites-applied";
    internal const string AwaitingAutodesk = "awaiting-autodesk";
    internal const string Verifying = "verifying";
    internal const string ReadyForHandoff = "ready-for-handoff";
    internal const string Incomplete = "incomplete";
    internal const string Failed = "failed";
    internal const string Blocked = "blocked";

    internal static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        ApplyingPrerequisites,
        PrerequisitesApplied,
        AwaitingAutodesk,
        Verifying,
        ReadyForHandoff,
        Incomplete,
        Failed,
        Blocked,
    };
}
