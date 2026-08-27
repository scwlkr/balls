using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsDataDirectorySecurity
{
    private const string MarkerFileName = ".balls-state";
    private const string MarkerContent = "Balls local state v1\n";

    public static string Prepare(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (new Uri(fullPath).IsUnc)
        {
            throw new UnauthorizedAccessException(
                "The Balls data directory must be on a local filesystem.");
        }

        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The data directory has no filesystem root.", nameof(path));
        if (new DriveInfo(root).DriveType == DriveType.Network)
        {
            throw new UnauthorizedAccessException(
                "The Balls data directory must not use a mapped network drive.");
        }

        Directory.CreateDirectory(fullPath);
        var directory = new DirectoryInfo(fullPath);
        for (var current = directory; current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    "The Balls data directory path cannot traverse a filesystem reparse point.");
            }
        }


        var markerPath = Path.Combine(fullPath, MarkerFileName);
        var entries = Directory.EnumerateFileSystemEntries(fullPath).ToArray();
        if (entries.Length > 0)
        {
            var marker = new FileInfo(markerPath);
            if (!marker.Exists
                || (marker.Attributes & FileAttributes.ReparsePoint) != 0
                || !string.Equals(File.ReadAllText(markerPath), MarkerContent, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    "An existing nonempty data directory must already be initialized by Balls.");
            }

            var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                MarkerFileName,
                "balls.db",
                "balls.db-wal",
                "balls.db-shm",
                "ballsd.lock",
                "automatic-private-listeners-v1.json",
            };
            if (entries.Any(entry => !allowedNames.Contains(Path.GetFileName(entry))))
            {
                throw new UnauthorizedAccessException(
                    "The Balls data directory contains an unexpected filesystem entry.");
            }
        }

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException(
                "The current Windows account has no security identifier.");
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        const FileSystemRights rights = FileSystemRights.FullControl;
        const InheritanceFlags inheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUser);
        security.AddAccessRule(
            new FileSystemAccessRule(
                currentUser,
                rights,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
        security.AddAccessRule(
            new FileSystemAccessRule(
                localSystem,
                rights,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
        directory.SetAccessControl(security);

        if (!File.Exists(markerPath))
        {
            File.WriteAllText(markerPath, MarkerContent);
        }

        foreach (var fileName in new[]
                 {
                     MarkerFileName,
                     "balls.db",
                     "balls.db-wal",
                     "balls.db-shm",
                     "ballsd.lock",
                     "automatic-private-listeners-v1.json",
                 })
        {
            var file = new FileInfo(Path.Combine(fullPath, fileName));
            if (!file.Exists)
            {
                continue;
            }

            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    "Balls state files cannot be filesystem reparse points.");
            }

            var fileSecurity = new FileSecurity();
            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            fileSecurity.SetOwner(currentUser);
            fileSecurity.AddAccessRule(
                new FileSystemAccessRule(
                    currentUser,
                    rights,
                    AccessControlType.Allow));
            fileSecurity.AddAccessRule(
                new FileSystemAccessRule(
                    localSystem,
                    rights,
                    AccessControlType.Allow));
            file.SetAccessControl(fileSecurity);
        }

        return fullPath;
    }
}
