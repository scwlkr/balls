using System.Buffers.Binary;

namespace Balls.Protocol.Remote.V1;

public sealed record RemoteChannelLimits(
    TimeSpan HandshakeTimeout,
    TimeSpan IoTimeout,
    int MaximumPayloadBytes,
    int MaximumReceivedOperations)
{
    public static RemoteChannelLimits Default { get; } = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        64 * 1024,
        4096);

    internal void Validate()
    {
        if (HandshakeTimeout < TimeSpan.FromMilliseconds(100)
            || HandshakeTimeout > TimeSpan.FromMinutes(1)
            || IoTimeout < TimeSpan.FromMilliseconds(100)
            || IoTimeout > TimeSpan.FromMinutes(1)
            || MaximumPayloadBytes is < 1 or > 1024 * 1024
            || MaximumReceivedOperations is < 1 or > 65_536)
        {
            throw new ArgumentException("Remote channel limits are outside the supported bounds.");
        }
    }
}

public sealed record RemoteFrame(Guid OperationId, byte[] Payload);

public sealed class RemoteChannelException : Exception
{
    public RemoteChannelException(string code)
        : base(MessageFor(code))
    {
        Code = code;
    }

    public string Code { get; }

    private static string MessageFor(string code) => code switch
    {
        "unsupported_version" => "The remote protocol version is unsupported.",
        "replayed" => "The remote operation was already received.",
        "oversized" => "The remote frame exceeds the configured limit.",
        "operation_limit" => "The remote channel operation limit was reached.",
        "timeout" => "The remote operation timed out.",
        "interrupted" => "The remote peer interrupted the operation.",
        "authentication_failed" => "The remote peer could not be authenticated.",
        "revoked" => "The remote peer credential is revoked.",
        "wrong_circle" => "The remote peer is authorized for another Circle.",
        "wrong_node" => "The remote peer is not the expected Node.",
        "downgraded" => "The remote protocol negotiation was rejected.",
        _ => "The remote protocol input is malformed.",
    };
}

public sealed class RemoteFrameWriter
{
    private const int HeaderLength = 28;
    private static readonly byte[] Magic = "BRF1"u8.ToArray();
    private readonly RemoteChannelLimits limits;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public RemoteFrameWriter(RemoteChannelLimits? limits = null)
    {
        this.limits = limits ?? RemoteChannelLimits.Default;
        this.limits.Validate();
    }

    public async Task WriteAsync(
        Stream stream,
        RemoteFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.OperationId == Guid.Empty)
        {
            throw new RemoteChannelException("malformed");
        }

        if (frame.Payload is null || frame.Payload.Length > limits.MaximumPayloadBytes)
        {
            throw new RemoteChannelException("oversized");
        }

        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), RemoteSecurityProtocol.Version);
        if (!frame.OperationId.TryWriteBytes(header.AsSpan(8, 16), bigEndian: true, out var bytesWritten)
            || bytesWritten != 16)
        {
            throw new RemoteChannelException("malformed");
        }

        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24, 4), frame.Payload.Length);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CreateTimeout(limits.IoTimeout, cancellationToken);
            try
            {
                await stream.WriteAsync(header, timeout.Token).ConfigureAwait(false);
                if (frame.Payload.Length > 0)
                {
                    await stream.WriteAsync(frame.Payload, timeout.Token).ConfigureAwait(false);
                }

                await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RemoteChannelException("timeout");
            }
            catch (IOException)
            {
                throw new RemoteChannelException("interrupted");
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    internal static CancellationTokenSource CreateTimeout(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }
}

public sealed class RemoteFrameReader
{
    private const int HeaderLength = 28;
    private static readonly byte[] Magic = "BRF1"u8.ToArray();
    private readonly RemoteChannelLimits limits;
    private readonly HashSet<Guid> receivedOperations = [];
    private readonly SemaphoreSlim readLock = new(1, 1);

    public RemoteFrameReader(RemoteChannelLimits? limits = null)
    {
        this.limits = limits ?? RemoteChannelLimits.Default;
        this.limits.Validate();
    }

    public async Task<RemoteFrame> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        await readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = RemoteFrameWriter.CreateTimeout(limits.IoTimeout, cancellationToken);
            try
            {
                var header = new byte[HeaderLength];
                await ReadExactlyAsync(stream, header, timeout.Token).ConfigureAwait(false);
                if (!header.AsSpan(0, 4).SequenceEqual(Magic))
                {
                    throw new RemoteChannelException("malformed");
                }

                var version = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4));
                if (version != RemoteSecurityProtocol.Version)
                {
                    throw new RemoteChannelException("unsupported_version");
                }

                var operationId = new Guid(header.AsSpan(8, 16), bigEndian: true);
                if (operationId == Guid.Empty)
                {
                    throw new RemoteChannelException("malformed");
                }

                var payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24, 4));
                if (payloadLength < 0)
                {
                    throw new RemoteChannelException("malformed");
                }

                if (payloadLength > limits.MaximumPayloadBytes)
                {
                    throw new RemoteChannelException("oversized");
                }

                if (receivedOperations.Contains(operationId))
                {
                    throw new RemoteChannelException("replayed");
                }

                if (receivedOperations.Count >= limits.MaximumReceivedOperations)
                {
                    throw new RemoteChannelException("operation_limit");
                }

                var payload = new byte[payloadLength];
                if (payloadLength > 0)
                {
                    await ReadExactlyAsync(stream, payload, timeout.Token).ConfigureAwait(false);
                }

                receivedOperations.Add(operationId);
                return new RemoteFrame(operationId, payload);
            }
            catch (RemoteChannelException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RemoteChannelException("timeout");
            }
            catch (IOException)
            {
                throw new RemoteChannelException("interrupted");
            }
        }
        finally
        {
            readLock.Release();
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = await stream.ReadAsync(destination[offset..], cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new RemoteChannelException("interrupted");
            }

            offset += read;
        }
    }
}
