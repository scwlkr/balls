using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Balls.Bootstrap.Windows;

internal static class RuntimeInspector
{
    public static void Require(RuntimeContract runtime)
    {
        if (runtime.Kind == "self-contained")
        {
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Framework-dependent Windows packages require Windows.");
        }

        var root = FindDotnetRoot();
        var executable = Path.Combine(root, "dotnet.exe");
        var label = string.Join(" and ", runtime.Frameworks.Select(FrameworkLabel));
        var error = $"This Balls package requires the x64 {label} runtime" +
            (runtime.Frameworks.Count == 1 ? "." : "s.");
        if (!IsX64PortableExecutable(executable))
        {
            throw new InvalidOperationException(error);
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--list-runtimes");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException(error);
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || runtime.Frameworks.Any(framework =>
                !output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(line =>
                    line.StartsWith($"{framework.Name} {framework.Major}.", StringComparison.Ordinal))))
        {
            throw new InvalidOperationException(error);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string FindDotnetRoot()
    {
        foreach (var variable in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Path.GetFullPath(value);
            }
        }

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var installKey = baseKey.OpenSubKey("SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64");
        if (installKey?.GetValue("InstallLocation") is string installLocation &&
            !string.IsNullOrWhiteSpace(installLocation) &&
            File.Exists(Path.Combine(installLocation, "dotnet.exe")))
        {
            return Path.GetFullPath(installLocation);
        }

        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ??
            Environment.GetEnvironmentVariable("ProgramFiles") ??
            throw new InvalidOperationException("The x64 Program Files location is unavailable.");
        return Path.GetFullPath(Path.Combine(programFiles, "dotnet"));
    }

    private static bool IsX64PortableExecutable(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 70 || reader.ReadUInt16() != 0x5a4d)
            {
                return false;
            }
            stream.Position = 0x3c;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset + 6 > stream.Length)
            {
                return false;
            }
            stream.Position = peOffset;
            return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string FrameworkLabel(RuntimeFramework framework) => framework.Name switch
    {
        "Microsoft.NETCore.App" => $".NET {framework.Major}",
        "Microsoft.AspNetCore.App" => $"ASP.NET Core {framework.Major}",
        _ => $"{framework.Name} {framework.Major}",
    };
}
