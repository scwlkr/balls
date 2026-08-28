using System.Runtime.InteropServices;
using Balls.WindowsConformance;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArguments) =>
{
    eventArguments.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
using var interrupt = OperatingSystem.IsWindows()
    ? null
    : PosixSignalRegistration.Create(PosixSignal.SIGINT, CancelFromSignal);
using var termination = OperatingSystem.IsWindows()
    ? null
    : PosixSignalRegistration.Create(PosixSignal.SIGTERM, CancelFromSignal);
try
{
    return await ConformanceCommand.RunAsync(
        args,
        Console.Out,
        Console.Error,
        cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

void CancelFromSignal(PosixSignalContext context)
{
    context.Cancel = true;
    cancellation.Cancel();
}
