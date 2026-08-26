using Balls.Canary;

try
{
    if (args.FirstOrDefault() == "package")
    {
        var request = CanaryCommandParser.Parse(args);
        var result = CanaryPackageBuilder.Build(request);
        Console.WriteLine($"Artifact: {result.ArchivePath}");
        Console.WriteLine($"Checksum: {result.ChecksumPath}");
        if (result.InstallerPath is not null)
        {
            Console.WriteLine($"Installer: {result.InstallerPath}");
        }
    }
    else
    {
        var request = DevelopmentManifestCommandParser.Parse(args);
        var result = DevelopmentManifestBuilder.Build(request);
        Console.WriteLine($"Version manifest: {result.VersionManifestPath}");
        Console.WriteLine($"Development pointer: {result.ChannelManifestPath}");
        Console.WriteLine($"Immutable Windows bootstrap manifest: {result.BootstrapVersionManifestPath}");
        Console.WriteLine($"Windows bootstrap pointer: {result.BootstrapManifestPath}");
        Console.WriteLine($"Release catalog: {result.ReleaseCatalogPath}");
        Console.WriteLine($"Previous pointer tag: {result.PreviousTag ?? "none"}");
        Console.WriteLine($"Previous pointer SHA-256: {result.PreviousSha256 ?? "none"}");
        Console.WriteLine($"Previous bootstrap pointer tag: {result.PreviousBootstrapTag ?? "none"}");
        Console.WriteLine($"Previous bootstrap pointer SHA-256: {result.PreviousBootstrapSha256 ?? "none"}");
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
