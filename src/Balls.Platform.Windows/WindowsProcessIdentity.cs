using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static partial class WindowsProcessIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;

    internal static bool TryGetExpectedDaemonUserSid(int processId, out string userSid)
    {
        userSid = string.Empty;
        try
        {
            using var process = Process.GetProcessById(processId);
            var actualPath = process.MainModule?.FileName;
            var expectedPath = Path.Combine(AppContext.BaseDirectory, "ballsd.exe");
            if (actualPath is null
                || !Path.GetFullPath(actualPath).Equals(
                    Path.GetFullPath(expectedPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }

        using var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (processHandle.IsInvalid
            || !OpenProcessToken(processHandle, TokenQuery, out var tokenHandle))
        {
            return false;
        }

        using (tokenHandle)
        {
            _ = GetTokenInformation(tokenHandle, TokenUser, IntPtr.Zero, 0, out var required);
            if (required <= 0)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(required);
            try
            {
                if (!GetTokenInformation(tokenHandle, TokenUser, buffer, required, out _))
                {
                    return false;
                }

                var tokenUser = Marshal.PtrToStructure<TokenUserValue>(buffer);
                userSid = new SecurityIdentifier(tokenUser.User.Sid).Value;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        internal IntPtr Sid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenUserValue
    {
        internal SidAndAttributes User;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(
        SafeProcessHandle processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
