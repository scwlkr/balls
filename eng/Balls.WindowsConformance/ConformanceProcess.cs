using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Balls.WindowsConformance;

internal sealed record ConformanceProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    int MaximumOutputBytes,
    string? StandardInput = null);

internal sealed record ConformanceProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface IConformanceProcessRunner
{
    Task<ConformanceProcessResult> RunAsync(
        ConformanceProcessRequest request,
        CancellationToken cancellationToken);
}

internal sealed class SystemConformanceProcessRunner : IConformanceProcessRunner
{
    public async Task<ConformanceProcessResult> RunAsync(
        ConformanceProcessRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = request.StandardInput is not null,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var started = false;
        try
        {
            try
            {
                started = process.Start();
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                throw new ConformanceRefusalException("transport_start_failed");
            }

            if (!started)
            {
                throw new ConformanceRefusalException("transport_start_failed");
            }

            var standardOutput = ReadBoundedAsync(
                process.StandardOutput,
                request.MaximumOutputBytes,
                timeout.Token);
            var standardError = ReadBoundedAsync(
                process.StandardError,
                request.MaximumOutputBytes,
                timeout.Token);
            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(
                    request.StandardInput.AsMemory(),
                    timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new ConformanceProcessResult(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new ConformanceRefusalException("transport_timeout");
        }
        catch (ConformanceOutputLimitException)
        {
            Kill(process);
            throw new ConformanceRefusalException("transport_output_oversized");
        }
        catch (IOException)
        {
            Kill(process);
            throw new ConformanceRefusalException("transport_io_failed");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                Kill(process);
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        var bytes = 0;
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return result.ToString();
            }

            bytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, count));
            if (bytes > maximumBytes)
            {
                throw new ConformanceOutputLimitException();
            }

            result.Append(buffer, 0, count);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class ConformanceOutputLimitException : Exception;
}
