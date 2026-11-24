using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading.Channels;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using PacketPeek.Protocol;

namespace PacketPeek;

public class PacketQueue
{
    public Channel<PacketEvent> Channel { get; }
    public int Capacity { get; }
    public PacketQueue(IConfiguration configuration)
    {
        int capacity = configuration.GetValue("Engine:ChannelCapacity", 1024);
        if (capacity < 1) throw new InvalidOperationException("Engine:ChannelCapacity must be positive.");
        Capacity = capacity;
        ObservabilityMetrics.SetChannelDepth(0, capacity);
        Channel = System.Threading.Channels.Channel.CreateBounded<PacketEvent>(new BoundedChannelOptions(capacity)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    }
}

public static class FrameReader
{
    public static async Task<EngineEvent?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        int first = await stream.ReadAsync(prefix.AsMemory(0, 1), ct);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1), ct);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (length is 0 or > 1048576) throw new InvalidDataException("Invalid protobuf frame size.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct);
        var message = EngineEvent.Parser.ParseFrom(payload);
        if (message.EventCase == EngineEvent.EventOneofCase.None) throw new InvalidDataException("Empty event envelope.");
        return message;
    }

    public static async Task PumpAsync(Stream stream, ChannelWriter<PacketEvent> packets,
        Func<Protocol.FlowSummary, CancellationToken, Task> onFlow, CancellationToken ct,
        Action? onPacketBuffered = null)
    {
        while (await ReadAsync(stream, ct) is { } message)
        {
            if (message.Packet is { } packet)
            {
                await packets.WriteAsync(packet, ct);
                onPacketBuffered?.Invoke();
                ObservabilityMetrics.PacketsReceived.Inc();
            }
            else
            {
                ObservabilityMetrics.FlowsCreated.Inc();
                await onFlow(message.Flow, ct);
            }
        }
    }
}

public class EngineBridge(PacketQueue queue, IServiceScopeFactory scopes, IConfiguration config, ILogger<EngineBridge> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool enabled = config.GetValue<bool>("Engine:Enabled");
        string? file = config["Engine:Pcap"];
        string? device = config["Engine:Interface"];
        bool captureExpected = enabled && !string.IsNullOrWhiteSpace(device) && string.IsNullOrWhiteSpace(file);
        ObservabilityMetrics.EngineCaptureExpected.Set(captureExpected ? 1 : 0);
        if (!enabled)
        {
            ObservabilityMetrics.EngineRunning.Set(0);
            return;
        }
        await Task.Yield();
        Exception? failure = null;
        try
        {
            var info = new ProcessStartInfo(config["Engine:Path"] ?? "libpeek")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            if (string.IsNullOrWhiteSpace(file) == string.IsNullOrWhiteSpace(device))
                throw new InvalidOperationException("Configure exactly one of Engine:Pcap or Engine:Interface.");
            info.ArgumentList.Add(!string.IsNullOrWhiteSpace(file) ? "--pcap" : "--interface");
            info.ArgumentList.Add(file ?? device!);
            info.ArgumentList.Add("--filter"); info.ArgumentList.Add(config["Engine:Filter"] ?? "ip or ip6");
            info.ArgumentList.Add("--idle-seconds"); info.ArgumentList.Add(config["Engine:IdleSeconds"] ?? "30");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start libpeek.");
            ObservabilityMetrics.EngineRunning.Set(1);
            var stderr = DrainErrors(process.StandardError);
            try
            {
                await FrameReader.PumpAsync(process.StandardOutput.BaseStream, queue.Channel.Writer, SaveFlow, stoppingToken,
                    () => ObservabilityMetrics.SetChannelDepth(queue.Channel.Reader.Count, queue.Capacity));
                await process.WaitForExitAsync(stoppingToken);
                if (process.ExitCode != 0) throw new InvalidOperationException($"libpeek exited with code {process.ExitCode}.");
            }
            finally
            {
                // Closing the read end delivers EPIPE to a writing engine.
                process.StandardOutput.Dispose();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                await stderr;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error) { failure = error; ObservabilityMetrics.EngineErrors.Inc(); throw; }
        finally
        {
            ObservabilityMetrics.EngineRunning.Set(0);
            queue.Channel.Writer.TryComplete(failure);
        }
    }

    private async Task DrainErrors(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line) logger.LogInformation("libpeek: {Message}", line);
    }

    private async Task SaveFlow(Protocol.FlowSummary flow, CancellationToken ct)
    {
        if (flow.Tuple is null) throw new InvalidDataException("Flow has no tuple.");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Flows.Add(new FlowSummary
        {
            SrcIp = flow.Tuple.SrcIp, DstIp = flow.Tuple.DstIp,
            SrcPort = checked((int)flow.Tuple.SrcPort), DstPort = checked((int)flow.Tuple.DstPort),
            Protocol = checked((int)flow.Tuple.Protocol), FirstUs = checked((long)flow.FirstUs), LastUs = checked((long)flow.LastUs),
            Packets = checked((long)flow.Packets), Bytes = checked((long)flow.Bytes)
        });
        await ObservabilityMetrics.MeasureDbOperationAsync(() => db.SaveChangesAsync(ct));
        ObservabilityMetrics.FlowsPersisted.Inc();
    }
}
