using System.Buffers.Binary;
using System.Threading.Channels;
using Google.Protobuf;
using PacketPeek.Protocol;
using Xunit;

namespace PacketPeek.Tests;

public class BridgeTests
{
    private static byte[] Frame(EngineEvent message)
    {
        var payload = message.ToByteArray(); var frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length); payload.CopyTo(frame, 4); return frame;
    }

    [Fact]
    public async Task Full_channel_blocks_then_delivers_without_drops()
    {
        var channel = Channel.CreateBounded<PacketEvent>(1);
        await channel.Writer.WriteAsync(new PacketEvent { Bytes = 1 });
        using var stream = new MemoryStream(Frame(new EngineEvent { Packet = new PacketEvent { Bytes = 2 } }));
        var pumping = FrameReader.PumpAsync(stream, channel.Writer, (_, _) => Task.CompletedTask, default);
        Assert.False(pumping.IsCompleted);
        Assert.Equal(1u, (await channel.Reader.ReadAsync()).Bytes);
        await pumping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2u, (await channel.Reader.ReadAsync()).Bytes);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Cancellation_unblocks_full_channel()
    {
        var channel = Channel.CreateBounded<PacketEvent>(1);
        await channel.Writer.WriteAsync(new PacketEvent());
        using var stream = new MemoryStream(Frame(new EngineEvent { Packet = new PacketEvent() }));
        using var cancel = new CancellationTokenSource();
        var task = FrameReader.PumpAsync(stream, channel.Writer, (_, _) => Task.CompletedTask, cancel.Token);
        Assert.False(task.IsCompleted); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 0, 0, 3, 1 })]
    public async Task Truncated_frames_fail(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<EndOfStreamException>(() => FrameReader.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 32, 0, 0 })]
    public async Task Invalid_sizes_fail_before_allocation(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameReader.ReadAsync(stream, default));
    }

    [Fact]
    public async Task Eof_and_flow_envelope_are_supported()
    {
        using var empty = new MemoryStream(); Assert.Null(await FrameReader.ReadAsync(empty, default));
        using var stream = new MemoryStream(Frame(new EngineEvent { Flow = new Protocol.FlowSummary { Packets = 7 } }));
        var channel = Channel.CreateBounded<PacketEvent>(1); ulong packets = 0;
        await FrameReader.PumpAsync(stream, channel.Writer, (flow, _) => { packets = flow.Packets; return Task.CompletedTask; }, default);
        Assert.Equal(7ul, packets); Assert.False(channel.Reader.TryRead(out _));
    }
}
