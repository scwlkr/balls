using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static partial class WindowsBallsWizardSystemInventory
{
    public static string? ReadInstallationType()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return key?.GetValue("InstallationType") as string;
    }

    public static Balls.Platform.BallsWizardSystemContext Inspect(string wizardDirectory)
    {
        var memory = ReadMemory();
        return new Balls.Platform.BallsWizardSystemContext(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            ReadCpuName(),
            ReadGpuNames(),
            checked((long)memory.TotalPhys),
            checked((long)memory.AvailPhys),
            ReadFreeStorage(wizardDirectory));
    }

    private static MemoryStatusEx ReadMemory()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>(),
        };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new IOException("Windows did not report the local memory capacity.");
        }

        return status;
    }

    private static string ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            writable: false);
        return (key?.GetValue("ProcessorNameString") as string)?.Trim()
            ?? $"{Environment.ProcessorCount} logical processors";
    }

    private static IReadOnlyList<string> ReadGpuNames()
    {
        var results = new List<string>();
        using var video = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Video",
            writable: false);
        foreach (var adapterKeyName in video?.GetSubKeyNames() ?? [])
        {
            using var adapter = video?.OpenSubKey(Path.Combine(adapterKeyName, "0000"));
            var name = (adapter?.GetValue("DriverDesc") as string)?.Trim();
            if (!string.IsNullOrWhiteSpace(name)
                && !results.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(name);
            }
        }

        return results.Count == 0 ? ["Windows display adapter"] : results;
    }

    private static long ReadFreeStorage(string wizardDirectory)
    {
        var fullPath = Path.GetFullPath(wizardDirectory);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException("Windows did not report the Wizard storage volume.");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
