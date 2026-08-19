using Balls.Verify;

var exitCode = VerificationExitCodes.Success;
var resultsDirectory = Path.Combine(
    Path.GetTempPath(),
    $"balls-verify-{Environment.ProcessId}-{Guid.NewGuid():N}");

try
{
    var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
    var request = VerificationRequestParser.Parse(args);
    var plan = VerificationPlanner.Create(request, repositoryRoot, resultsDirectory);
    exitCode = await new VerificationEngine(new ProcessCommandRunner()).ExecuteAsync(plan);
}
catch (UsageException exception)
{
    Console.Error.WriteLine(exception.Message);
    exitCode = VerificationExitCodes.Usage;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    exitCode = 1;
}
finally
{
    if (Directory.Exists(resultsDirectory))
    {
        Directory.Delete(resultsDirectory, recursive: true);
    }
}

return exitCode;

static string FindRepositoryRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find Balls.slnx from the current directory.");
}
