using System.Runtime.Versioning;
using System.Text;

namespace Balls.Platform.Linux;

[SupportedOSPlatform("linux")]
public static class LinuxDataDirectorySecurity
{
    private const string MarkerFileName = ".balls-state";
    private const string MarkerContent = "Balls local state v1\n";
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly HashSet<string> AllowedNames = new(StringComparer.Ordinal)
    {
        MarkerFileName,
        "balls.db",
        "balls.db-wal",
        "balls.db-shm",
        "ballsd.lock",
        "automatic-private-listeners-v1.json",
    };
    private static readonly HashSet<string> SupportedLocalFileSystems = new(StringComparer.Ordinal)
    {
        "bcachefs",
        "btrfs",
        "ecryptfs",
        "ext2",
        "ext3",
        "ext4",
        "f2fs",
        "jfs",
        "overlay",
        "reiserfs",
        "ubifs",
        "xfs",
        "zfs",
    };

    public static string Prepare(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The Linux data directory must be an absolute path.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The filesystem root cannot be a Balls data directory.");
        }

        EnsureOwnedDirectory(fullPath, allowStickySharedParent: false);
        EnsureLocalFileSystem(fullPath);
        ValidateContents(fullPath);
        File.SetUnixFileMode(fullPath, PrivateDirectoryMode);

        var markerPath = Path.Combine(fullPath, MarkerFileName);
        if (LinuxNativeFileSystem.TryReadStatus(markerPath) is null)
        {
            using var marker = new FileStream(
                markerPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough);
            var content = Encoding.UTF8.GetBytes(MarkerContent);
            marker.Write(content);
            marker.Flush(flushToDisk: true);
        }

        var databasePath = Path.Combine(fullPath, "balls.db");
        if (LinuxNativeFileSystem.TryReadStatus(databasePath) is null)
        {
            using var database = new FileStream(
                databasePath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    UnixCreateMode = PrivateFileMode,
                    Options = FileOptions.WriteThrough,
                });
            database.Flush(flushToDisk: true);
        }

        ProtectKnownFiles(fullPath);
        return fullPath;
    }

    internal static void EnsurePrivateRuntimeDirectory(string path)
    {
        EnsureOwnedDirectory(path, allowStickySharedParent: true);
        File.SetUnixFileMode(path, PrivateDirectoryMode);
    }

    private static void EnsureOwnedDirectory(string fullPath, bool allowStickySharedParent)
    {
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path has no filesystem root.", nameof(fullPath));
        var components = Path.GetRelativePath(root, fullPath)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        var creating = false;

        foreach (var component in components)
        {
            current = Path.Combine(current, component);
            var status = LinuxNativeFileSystem.TryReadStatus(current);
            if (status is null)
            {
                if (!creating)
                {
                    var parent = Directory.GetParent(current)?.FullName ?? root;
                    var parentStatus = LinuxNativeFileSystem.ReadStatus(parent);
                    var ownedParent = parentStatus.UserId == LinuxNativeFileSystem.EffectiveUserId;
                    var stickySharedParent = allowStickySharedParent
                        && parentStatus.UserId == 0
                        && parentStatus.Permissions.HasFlag(UnixFileMode.StickyBit);
                    if (!ownedParent && !stickySharedParent)
                    {
                        throw new UnauthorizedAccessException(
                            "The nearest existing parent must be owned by the current Linux user.");
                    }

                    creating = true;
                }

                Directory.CreateDirectory(current, PrivateDirectoryMode);
                status = LinuxNativeFileSystem.ReadStatus(current);
            }

            if (!status.IsDirectory || status.IsSymbolicLink)
            {
                throw new UnauthorizedAccessException(
                    "The Balls path cannot traverse a symbolic link or non-directory entry.");
            }

            var writableByOthers = status.Permissions
                & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
            if (!creating && writableByOthers != 0)
            {
                var isAllowedStickyParent = status.UserId == 0
                    && status.Permissions.HasFlag(UnixFileMode.StickyBit);
                if (!isAllowedStickyParent)
                {
                    throw new UnauthorizedAccessException(
                        "The Balls path cannot traverse a group- or other-writable directory.");
                }
            }

            if (creating)
            {
                if (status.UserId != LinuxNativeFileSystem.EffectiveUserId)
                {
                    throw new UnauthorizedAccessException(
                        "A newly created Balls directory is not owned by the current Linux user.");
                }

                File.SetUnixFileMode(current, PrivateDirectoryMode);
            }
        }

        var target = LinuxNativeFileSystem.ReadStatus(fullPath);
        if (target.UserId != LinuxNativeFileSystem.EffectiveUserId)
        {
            throw new UnauthorizedAccessException(
                "The Balls directory must be owned by the current Linux user.");
        }
    }

    private static void ValidateContents(string fullPath)
    {
        var entries = Directory.EnumerateFileSystemEntries(fullPath).ToArray();
        if (entries.Length == 0)
        {
            return;
        }

        var markerPath = Path.Combine(fullPath, MarkerFileName);
        var marker = LinuxNativeFileSystem.TryReadStatus(markerPath);
        if (marker is null
            || !marker.IsRegularFile
            || marker.UserId != LinuxNativeFileSystem.EffectiveUserId
            || !string.Equals(File.ReadAllText(markerPath), MarkerContent, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "An existing nonempty data directory must already be initialized by Balls.");
        }

        if (entries.Any(entry => !AllowedNames.Contains(Path.GetFileName(entry))))
        {
            throw new UnauthorizedAccessException(
                "The Balls data directory contains an unexpected filesystem entry.");
        }
    }

    private static void ProtectKnownFiles(string fullPath)
    {
        foreach (var fileName in AllowedNames)
        {
            var filePath = Path.Combine(fullPath, fileName);
            var status = LinuxNativeFileSystem.TryReadStatus(filePath);
            if (status is null)
            {
                continue;
            }

            if (!status.IsRegularFile || status.UserId != LinuxNativeFileSystem.EffectiveUserId)
            {
                throw new UnauthorizedAccessException(
                    "Balls state files must be regular files owned by the current Linux user.");
            }

            File.SetUnixFileMode(filePath, PrivateFileMode);
        }
    }

    private static void EnsureLocalFileSystem(string path)
    {
        const string mountInfoPath = "/proc/self/mountinfo";
        if (!File.Exists(mountInfoPath))
        {
            throw new UnauthorizedAccessException(
                "The Linux data-directory filesystem could not be verified as local.");
        }

        string? selectedMount = null;
        string? selectedType = null;
        foreach (var line in File.ReadLines(mountInfoPath))
        {
            var separator = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var left = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var right = line[(separator + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (left.Length < 5 || right.Length == 0)
            {
                continue;
            }

            var mountPoint = DecodeMountInfoPath(left[4]);
            if (!PathContains(mountPoint, path)
                || selectedMount is not null && selectedMount.Length >= mountPoint.Length)
            {
                continue;
            }

            selectedMount = mountPoint;
            selectedType = right[0];
        }

        if (selectedType is null || !SupportedLocalFileSystems.Contains(selectedType))
        {
            throw new UnauthorizedAccessException(
                "The Balls data directory must be on a verified local filesystem.");
        }
    }

    private static bool PathContains(string parent, string child)
    {
        return string.Equals(parent, child, StringComparison.Ordinal)
            || child.StartsWith(
                parent.EndsWith(Path.DirectorySeparatorChar)
                    ? parent
                    : parent + Path.DirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    private static string DecodeMountInfoPath(string value)
    {
        return value
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
    }
}
