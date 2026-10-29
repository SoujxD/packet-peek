using Microsoft.EntityFrameworkCore;
using PacketPeek.Protocol;

namespace PacketPeek;

public class RuleEvaluator
{
    private sealed class Window
    {
        public List<PacketEvent> Events { get; } = [];
        public bool Above { get; set; }
    }
    private readonly Dictionary<int, (string Signature, Dictionary<string, Window> Groups)> states = [];
    private ulong watermark;

    public IReadOnlyList<Alert> Evaluate(PacketEvent packet, IReadOnlyList<Rule> rules)
    {
        watermark = Math.Max(watermark, packet.TimestampUs);
        var active = rules.Where(x => x.Active).ToList();
        foreach (var id in states.Keys.Except(active.Select(x => x.Id)).ToArray()) states.Remove(id);
        List<Alert> alerts = [];
        foreach (var rule in active)
        {
            string signature = $"{rule.Metric}:{rule.Grouping}:{rule.WindowSeconds}:{rule.Threshold}";
            if (!states.TryGetValue(rule.Id, out var state) || state.Signature != signature)
                states[rule.Id] = state = (signature, new Dictionary<string, Window>());
            ulong span = checked((ulong)rule.WindowSeconds * 1000000);
            ulong cutoff = watermark >= span ? watermark - span : 0;
            bool hasCutoff = watermark >= span;
            foreach (var entry in state.Groups.ToArray())
            {
                entry.Value.Events.RemoveAll(x => hasCutoff && x.TimestampUs <= cutoff);
                if (Value(rule, entry.Value.Events) <= rule.Threshold) entry.Value.Above = false;
                if (entry.Value.Events.Count == 0) state.Groups.Remove(entry.Key);
            }
            if (hasCutoff && packet.TimestampUs <= cutoff) continue;
            string group = rule.Grouping == "src_ip" ? packet.SrcIp : $"{packet.SrcIp}|{packet.DstPort}";
            if (!state.Groups.TryGetValue(group, out var window)) state.Groups[group] = window = new Window();
            window.Events.Add(packet);
            long value = Value(rule, window.Events);
            if (value > rule.Threshold && !window.Above)
            {
                alerts.Add(new Alert { RuleId = rule.Id, GroupKey = group, Value = value,
                    FiredAt = DateTimeOffset.UnixEpoch.AddTicks(checked((long)watermark * 10)) });
                window.Above = true;
            }
        }
        return alerts;
    }

    private static long Value(Rule rule, List<PacketEvent> events) => rule.Metric switch
    {
        "packets" => events.Count,
        "bytes" => events.Sum(x => (long)x.Bytes),
        "distinct_dst_ips" => events.Select(x => x.DstIp).Distinct(StringComparer.Ordinal).LongCount(),
        _ => throw new InvalidOperationException("Unsupported stored rule metric.")
    };
}

public class RuleWorker(PacketQueue queue, IServiceScopeFactory scopes, RuleEvaluator evaluator) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var packet in queue.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rules = await db.Rules.AsNoTracking().Where(x => x.Active).ToListAsync(stoppingToken);
            var alerts = evaluator.Evaluate(packet, rules);
            db.Alerts.AddRange(alerts);
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
