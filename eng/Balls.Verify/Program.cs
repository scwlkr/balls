using Balls.Verify;

try
{
    var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
    var runner = new ProcessCommandRunner();
    foreach (var command in VerificationPlanner.Create(args, repositoryRoot))
    {
        var exitCode = await runner.RunAsync(command, CancellationToken.None);
        if (exitCode != 0)
        {
            return exitCode;
        }
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

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
