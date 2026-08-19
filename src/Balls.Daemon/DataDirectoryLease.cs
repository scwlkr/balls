namespace Balls.Daemon;

public sealed class DataDirectoryInUseException(string dataDirectory, Exception innerException)
    : Exception(
        $"Another ballsd instance is already using data directory '{dataDirectory}'.",
        innerException)
{
    public string Code => "data_directory_in_use";
}

internal sealed class DataDirectoryLease : IDisposable
{
    private readonly FileStream lockStream;

    private DataDirectoryLease(FileStream lockStream)
    {
        this.lockStream = lockStream;
    }

    public static DataDirectoryLease Acquire(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var fullPath = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(fullPath);
        var lockPath = Path.Combine(fullPath, "ballsd.lock");

        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            return new DataDirectoryLease(stream);
        }
        catch (IOException exception)
        {
            throw new DataDirectoryInUseException(fullPath, exception);
        }
    }

    public void Dispose()
    {
        lockStream.Dispose();
    }
}
