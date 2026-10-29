using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PacketPeek.Protocol;

namespace PacketPeek;

[Authorize(Policy = "Operator")]
public class LiveHub : Hub { }

public class LiveEvents(IHubContext<LiveHub> hub) : BackgroundService
{
    private PacketEvent? latest;
    public void Packet(PacketEvent packet) => Interlocked.Exchange(ref latest, packet);
    public Task Alert(Alert alert, CancellationToken ct) => hub.Clients.All.SendAsync("Alert", new
    { alert.Id, alert.RuleId, alert.FiredAt, alert.GroupKey, alert.Value }, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long last = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            long now = Stopwatch.GetTimestamp();
            if (last != 0 && Stopwatch.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(50)) continue;
            var packet = Interlocked.Exchange(ref latest, null);
            if (packet is null) continue;
            last = now;
            await hub.Clients.All.SendAsync("PacketEvent", packet, stoppingToken);
        }
    }
}
