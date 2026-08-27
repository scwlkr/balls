using Balls.Platform.Windows;

if (!OperatingSystem.IsWindows())
{
    return 2;
}

return args.Length > 0 && args[0] == "--revit-pipe-name"
    ? await WindowsRevitServerHelperCommand.RunAsync(args)
    : await WindowsCircleFilesHelperCommand.RunAsync(args);
