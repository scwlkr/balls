using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Balls.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal static class MacOSNativeFileSystem
{
    private const string SystemLibrary = "libSystem.B.dylib";
    private const int StatusBufferSize = 256;
    private const int ModeOffset = 4;
    private const int UserIdOffset = 16;
    private const int FileSystemBufferSize = 2200;
    private const int FileSystemFlagsOffset = 64;
    private const int FileSystemTypeOffset = 72;
    private const int FileSystemTypeLength = 16;
    private const uint LocalMountFlag = 0x00001000;
    private const int ExtendedAclType = 0x00000100;
    private const int FirstAclEntry = 0;
    private const ushort FileTypeMask = 0xf000;
    private const ushort DirectoryType = 0x4000;
    private const ushort RegularFileType = 0x8000;
    private const ushort SymbolicLinkType = 0xa000;
    private const ushort SocketType = 0xc000;
    private const int NoSuchFileOrDirectory = 2;

    public static uint EffectiveUserId => GetEffectiveUserId();

    public static MacOSFileStatus ReadStatus(string path)
    {
        return TryReadStatus(path)
            ?? throw new FileNotFoundException("The filesystem entry does not exist.", path);
    }

    public static MacOSFileStatus? TryReadStatus(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var buffer = Marshal.AllocHGlobal(StatusBufferSize);
        try
        {
            Zero(buffer, StatusBufferSize);
            if (LStat(path, buffer) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == NoSuchFileOrDirectory)
                {
                    return null;
                }

                throw new Win32Exception(
                    error,
                    $"Could not inspect macOS filesystem entry '{path}'.");
            }

            var mode = unchecked((ushort)Marshal.ReadInt16(buffer, ModeOffset));
            var userId = unchecked((uint)Marshal.ReadInt32(buffer, UserIdOffset));
            return new MacOSFileStatus(userId, mode);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void EnsureLocalApfs(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var buffer = Marshal.AllocHGlobal(FileSystemBufferSize);
        try
        {
            Zero(buffer, FileSystemBufferSize);
            if (StatFileSystem(path, buffer) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(
                    error,
                    $"Could not inspect the macOS filesystem for '{path}'.");
            }

            var flags = unchecked((uint)Marshal.ReadInt32(buffer, FileSystemFlagsOffset));
            var typeBytes = new byte[FileSystemTypeLength];
            Marshal.Copy(buffer + FileSystemTypeOffset, typeBytes, 0, typeBytes.Length);
            var terminator = Array.IndexOf(typeBytes, (byte)0);
            var type = Encoding.UTF8.GetString(
                typeBytes,
                0,
                terminator < 0 ? typeBytes.Length : terminator);
            if ((flags & LocalMountFlag) == 0
                || !string.Equals(type, "apfs", StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    "The Balls macOS path must be on a verified local APFS filesystem.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool HasExtendedAcl(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var acl = AclGetFile(path, ExtendedAclType);
        if (acl == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == NoSuchFileOrDirectory)
            {
                return false;
            }

            throw new Win32Exception(error, $"Could not inspect the macOS ACL for '{path}'.");
        }

        try
        {
            var result = AclGetEntry(acl, FirstAclEntry, out var entry);
            if (result < 0)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(error, $"Could not enumerate the macOS ACL for '{path}'.");
            }

            return entry != IntPtr.Zero;
        }
        finally
        {
            _ = AclFree(acl);
        }
    }

    private static void Zero(IntPtr buffer, int length)
    {
        for (var index = 0; index < length; index += sizeof(long))
        {
            Marshal.WriteInt64(buffer, index, 0);
        }
    }

    [DllImport(SystemLibrary, EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport(SystemLibrary, EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr buffer);

    [DllImport(SystemLibrary, EntryPoint = "statfs", SetLastError = true)]
    private static extern int StatFileSystem(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr buffer);

    [DllImport(SystemLibrary, EntryPoint = "acl_get_file", SetLastError = true)]
    private static extern IntPtr AclGetFile(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int type);

    [DllImport(SystemLibrary, EntryPoint = "acl_get_entry", SetLastError = true)]
    private static extern int AclGetEntry(IntPtr acl, int entryId, out IntPtr entry);

    [DllImport(SystemLibrary, EntryPoint = "acl_free")]
    private static extern int AclFree(IntPtr acl);

    internal sealed record MacOSFileStatus(uint UserId, ushort Mode)
    {
        public bool IsDirectory => (Mode & FileTypeMask) == DirectoryType;

        public bool IsRegularFile => (Mode & FileTypeMask) == RegularFileType;

        public bool IsSymbolicLink => (Mode & FileTypeMask) == SymbolicLinkType;

        public bool IsSocket => (Mode & FileTypeMask) == SocketType;

        public UnixFileMode Permissions => (UnixFileMode)(Mode & 0x0fff);
    }
}
