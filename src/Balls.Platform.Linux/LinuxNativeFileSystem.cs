using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Balls.Platform.Linux;

[SupportedOSPlatform("linux")]
internal static class LinuxNativeFileSystem
{
    private const int AtFileSystemCurrentWorkingDirectory = -100;
    private const int AtSymbolicLinkNoFollow = 0x100;
    private const uint StatxBasicStats = 0x7ff;
    private const int StatxBufferSize = 256;
    private const int UserIdOffset = 20;
    private const int ModeOffset = 28;
    private const ushort FileTypeMask = 0xf000;
    private const ushort DirectoryType = 0x4000;
    private const ushort RegularFileType = 0x8000;
    private const ushort SymbolicLinkType = 0xa000;
    private const ushort SocketType = 0xc000;
    private const int NoSuchFileOrDirectory = 2;

    public static uint EffectiveUserId => GetEffectiveUserId();

    public static LinuxFileStatus ReadStatus(string path)
    {
        return TryReadStatus(path)
            ?? throw new FileNotFoundException("The filesystem entry does not exist.", path);
    }

    public static LinuxFileStatus? TryReadStatus(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var buffer = Marshal.AllocHGlobal(StatxBufferSize);
        try
        {
            for (var index = 0; index < StatxBufferSize; index += sizeof(long))
            {
                Marshal.WriteInt64(buffer, index, 0);
            }

            if (Statx(
                    AtFileSystemCurrentWorkingDirectory,
                    path,
                    AtSymbolicLinkNoFollow,
                    StatxBasicStats,
                    buffer) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == NoSuchFileOrDirectory)
                {
                    return null;
                }

                throw new Win32Exception(error, $"Could not inspect Linux filesystem entry '{path}'.");
            }

            var mode = unchecked((ushort)Marshal.ReadInt16(buffer, ModeOffset));
            var userId = unchecked((uint)Marshal.ReadInt32(buffer, UserIdOffset));
            return new LinuxFileStatus(userId, mode);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        IntPtr buffer);

    internal sealed record LinuxFileStatus(uint UserId, ushort Mode)
    {
        public bool IsDirectory => (Mode & FileTypeMask) == DirectoryType;

        public bool IsRegularFile => (Mode & FileTypeMask) == RegularFileType;

        public bool IsSymbolicLink => (Mode & FileTypeMask) == SymbolicLinkType;

        public bool IsSocket => (Mode & FileTypeMask) == SocketType;

        public UnixFileMode Permissions => (UnixFileMode)(Mode & 0x0fff);
    }
}
