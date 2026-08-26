using Balls.Bootstrap.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This Balls bootstrap is Windows-only.");
    return 1;
}
if (!Environment.Is64BitOperatingSystem)
{
    Console.Error.WriteLine("The published Balls Windows package requires x64 Windows.");
    return 1;
}

try
{
    var options = BootstrapOptionsParser.Parse(args);
    using var installer = new WindowsBootstrapInstaller();
    await installer.InstallAsync(options, CancellationToken.None);
    return 0;
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    Console.Error.WriteLine($"Balls installation failed: {exception.Message}");
    return 1;
}
