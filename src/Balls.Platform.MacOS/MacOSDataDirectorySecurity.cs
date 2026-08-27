using System.Runtime.Versioning;
using System.Text;

namespace Balls.Platform.MacOS;

[SupportedOSPlatform("macos")]
public static class MacOSDataDirectorySecurity
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

    public static string Prepare(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The macOS data directory must be an absolute path.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith("/var/", StringComparison.Ordinal))
        {
            fullPath = "/private" + fullPath;
        }
        if (string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The filesystem root cannot be a Balls data directory.");
        }

        EnsureOwnedDirectory(fullPath, allowStickySharedParent: false);
        MacOSNativeFileSystem.EnsureLocalApfs(fullPath);
        EnsureNoExtendedAcl(fullPath, "The Balls data directory cannot grant extended ACL access.");
        ValidateContents(fullPath);
        File.SetUnixFileMode(fullPath, PrivateDirectoryMode);

        var markerPath = Path.Combine(fullPath, MarkerFileName);
        if (MacOSNativeFileSystem.TryReadStatus(markerPath) is null)
        {
            using var marker = new FileStream(
                markerPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = PrivateFileMode,
                    Options = FileOptions.WriteThrough,
                });
            var content = Encoding.UTF8.GetBytes(MarkerContent);
            marker.Write(content);
            marker.Flush(flushToDisk: true);
        }

        var databasePath = Path.Combine(fullPath, "balls.db");
        if (MacOSNativeFileSystem.TryReadStatus(databasePath) is null)
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
        MacOSNativeFileSystem.EnsureLocalApfs(path);
        EnsureNoExtendedAcl(path, "The Balls runtime directory cannot grant extended ACL access.");
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
            var status = MacOSNativeFileSystem.TryReadStatus(current);
            if (status is null)
            {
                if (!creating)
                {
                    var parent = Directory.GetParent(current)?.FullName ?? root;
                    var parentStatus = MacOSNativeFileSystem.ReadStatus(parent);
                    var ownedParent = parentStatus.UserId == MacOSNativeFileSystem.EffectiveUserId;
                    var stickySharedParent = allowStickySharedParent
                        && parentStatus.UserId == 0
                        && parentStatus.Permissions.HasFlag(UnixFileMode.StickyBit);
                    if (!ownedParent && !stickySharedParent)
                    {
                        throw new UnauthorizedAccessException(
                            "The nearest existing parent must be owned by the current macOS user.");
                    }

                    creating = true;
                }

                Directory.CreateDirectory(current, PrivateDirectoryMode);
                status = MacOSNativeFileSystem.ReadStatus(current);
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
                if (status.UserId != MacOSNativeFileSystem.EffectiveUserId)
                {
                    throw new UnauthorizedAccessException(
                        "A newly created Balls directory is not owned by the current macOS user.");
                }

                File.SetUnixFileMode(current, PrivateDirectoryMode);
                EnsureNoExtendedAcl(
                    current,
                    "A newly created Balls directory has an unexpected extended ACL.");
            }
        }

        var target = MacOSNativeFileSystem.ReadStatus(fullPath);
        if (target.UserId != MacOSNativeFileSystem.EffectiveUserId)
        {
            throw new UnauthorizedAccessException(
                "The Balls directory must be owned by the current macOS user.");
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
        var marker = MacOSNativeFileSystem.TryReadStatus(markerPath);
        if (marker is null
            || !marker.IsRegularFile
            || marker.UserId != MacOSNativeFileSystem.EffectiveUserId
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
            var status = MacOSNativeFileSystem.TryReadStatus(filePath);
            if (status is null)
            {
                continue;
            }

            if (!status.IsRegularFile || status.UserId != MacOSNativeFileSystem.EffectiveUserId)
            {
                throw new UnauthorizedAccessException(
                    "Balls state files must be regular files owned by the current macOS user.");
            }

            EnsureNoExtendedAcl(filePath, "Balls state files cannot grant extended ACL access.");
            File.SetUnixFileMode(filePath, PrivateFileMode);
        }
    }

    private static void EnsureNoExtendedAcl(string path, string message)
    {
        if (MacOSNativeFileSystem.HasExtendedAcl(path))
        {
            throw new UnauthorizedAccessException(message);
        }
    }
}
