using Balls.Daemon;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

return await DaemonCommand.RunAsync(args, Console.Out, Console.Error, shutdown.Token);
