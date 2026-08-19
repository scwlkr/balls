using System.Runtime.InteropServices;
using Balls.Daemon;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
using var terminateRegistration = OperatingSystem.IsLinux()
    ? PosixSignalRegistration.Create(
        PosixSignal.SIGTERM,
        context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        })
    : null;

return await DaemonCommand.RunAsync(args, Console.Out, Console.Error, shutdown.Token);
