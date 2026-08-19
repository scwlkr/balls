using Balls.Canary;

try
{
    var request = CanaryCommandParser.Parse(args);
    var result = CanaryPackageBuilder.Build(request);
    Console.WriteLine($"Artifact: {result.ArchivePath}");
    Console.WriteLine($"Checksum: {result.ChecksumPath}");
    if (result.InstallerPath is not null)
    {
        Console.WriteLine($"Installer: {result.InstallerPath}");
    }

    return 0;
}
catch (CanaryUsageException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
