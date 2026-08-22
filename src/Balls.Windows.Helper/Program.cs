using Balls.Platform.Windows;

if (!OperatingSystem.IsWindows())
{
    return 2;
}

return await WindowsCircleFilesHelperCommand.RunAsync(args);
