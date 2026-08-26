namespace Balls.Platform;

public sealed record CircleFilesFolderSelection(string FolderPath, string DisplayName);

public interface ICircleFilesFolderPicker
{
    ValueTask<CircleFilesFolderSelection?> SelectAsync(
        CancellationToken cancellationToken = default);
}

public sealed class UnsupportedCircleFilesFolderPicker : ICircleFilesFolderPicker
{
    public ValueTask<CircleFilesFolderSelection?> SelectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new CircleFilesHostingException(
            "windows_required",
            "Choosing a Circle Files folder is currently available on Windows only.");
    }
}
