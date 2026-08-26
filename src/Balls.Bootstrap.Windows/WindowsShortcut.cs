using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Balls.Bootstrap.Windows;

internal static class WindowsShortcut
{
    [SupportedOSPlatform("windows")]
    public static string Create(string launcherPath, string workingDirectory)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programs))
        {
            throw new InvalidOperationException("The current user Start Menu is unavailable.");
        }

        var destination = Path.Combine(programs, "Balls.lnk");
        var temporary = Path.Combine(programs, $"Balls-{Guid.NewGuid():N}.lnk");
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
                ?? throw new InvalidOperationException("The Windows shortcut service is unavailable.");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("The Windows shortcut service is unavailable.");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [temporary]);
            var shortcutType = shortcut?.GetType()
                ?? throw new InvalidOperationException("The Windows shortcut service is unavailable.");
            shortcutType.InvokeMember("TargetPath", PropertyFlags, null, shortcut, [launcherPath]);
            shortcutType.InvokeMember("WorkingDirectory", PropertyFlags, null, shortcut, [workingDirectory]);
            shortcutType.InvokeMember("Description", PropertyFlags, null, shortcut, ["Open Balls"]);
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private const System.Reflection.BindingFlags PropertyFlags =
        System.Reflection.BindingFlags.SetProperty;

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}
