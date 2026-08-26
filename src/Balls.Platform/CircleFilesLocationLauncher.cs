namespace Balls.Platform;

public sealed record CircleFilesMappedLocation(string DriveLetter);

public interface ICircleFilesLocationLauncher
{
    ValueTask OpenAsync(
        CircleFilesMappedLocation location,
        CancellationToken cancellationToken);
}

public sealed class UnsupportedCircleFilesLocationLauncher : ICircleFilesLocationLauncher
{
    public ValueTask OpenAsync(
        CircleFilesMappedLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new CircleFilesHostingException(
            "windows_required",
            "Opening a Circle Files folder in File Explorer is available only on Windows."));
    }
}
