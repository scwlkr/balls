using System.Buffers.Binary;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class RemoteFrameTests
{
    [TestMethod]
    public async Task Frame_round_trip_is_bounded_versioned_and_replay_resistant()
    {
        var limits = new RemoteChannelLimits(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            MaximumPayloadBytes: 1024,
            MaximumReceivedOperations: 4);
        var writer = new RemoteFrameWriter(limits);
        var reader = new RemoteFrameReader(limits);
        var operationId = Guid.Parse("0198c837-5000-7000-8000-000000000003");
        var frame = new RemoteFrame(operationId, "hello"u8.ToArray());
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, frame);
        stream.Position = 0;
        var received = await reader.ReadAsync(stream);

        Assert.AreEqual(operationId, received.OperationId);
        CollectionAssert.AreEqual(frame.Payload, received.Payload);

        stream.Position = 0;
        var replay = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => reader.ReadAsync(stream));
        Assert.AreEqual("replayed", replay.Code);
    }

    [TestMethod]
    public async Task Oversized_unsupported_malformed_and_interrupted_frames_fail_before_allocation()
    {
        var limits = new RemoteChannelLimits(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            MaximumPayloadBytes: 32,
            MaximumReceivedOperations: 4);
        var writer = new RemoteFrameWriter(limits);

        var oversizedWrite = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => writer.WriteAsync(
                new MemoryStream(),
                new RemoteFrame(Guid.CreateVersion7(), new byte[33])));
        Assert.AreEqual("oversized", oversizedWrite.Code);

        var valid = new MemoryStream();
        await writer.WriteAsync(
            valid,
            new RemoteFrame(Guid.CreateVersion7(), "ok"u8.ToArray()));
        var encoded = valid.ToArray();

        var unsupported = encoded.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(unsupported.AsSpan(4, 4), 2);
        var unsupportedError = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => new RemoteFrameReader(limits).ReadAsync(new MemoryStream(unsupported)));
        Assert.AreEqual("unsupported_version", unsupportedError.Code);

        var oversized = encoded.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(oversized.AsSpan(24, 4), 33);
        var oversizedError = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => new RemoteFrameReader(limits).ReadAsync(new MemoryStream(oversized)));
        Assert.AreEqual("oversized", oversizedError.Code);

        var malformed = encoded.ToArray();
        malformed[0] ^= 0xff;
        var malformedError = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => new RemoteFrameReader(limits).ReadAsync(new MemoryStream(malformed)));
        Assert.AreEqual("malformed", malformedError.Code);

        var interruptedError = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => new RemoteFrameReader(limits).ReadAsync(
                new MemoryStream(encoded[..^1])));
        Assert.AreEqual("interrupted", interruptedError.Code);
    }

    [TestMethod]
    public async Task Frame_read_timeout_and_operation_count_are_bounded()
    {
        var limits = new RemoteChannelLimits(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(100),
            MaximumPayloadBytes: 32,
            MaximumReceivedOperations: 1);
        var reader = new RemoteFrameReader(limits);
        var writer = new RemoteFrameWriter(limits);
        using var first = new MemoryStream();
        await writer.WriteAsync(first, new RemoteFrame(Guid.CreateVersion7(), []));
        first.Position = 0;
        await reader.ReadAsync(first);
        using var second = new MemoryStream();
        await writer.WriteAsync(second, new RemoteFrame(Guid.CreateVersion7(), []));
        second.Position = 0;

        var countError = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => reader.ReadAsync(second));
        Assert.AreEqual("operation_limit", countError.Code);

        var timeoutError = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => new RemoteFrameReader(limits).ReadAsync(new NeverCompletingStream()));
        Assert.AreEqual("timeout", timeoutError.Code);
    }

    private sealed class NeverCompletingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
