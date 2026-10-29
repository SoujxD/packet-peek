using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PacketPeek.Protocol;
using Xunit;

namespace PacketPeek.Tests;

[Collection("database")]
public class SignalRTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Authenticated_client_receives_every_persisted_alert_and_sampled_packets()
    {
        using var client = RuleTests.OperatorClient(fixture.Factory);
        string token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        await using var connection = new HubConnectionBuilder().WithUrl("http://localhost/hubs/live", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
        }).Build();
        var alerts = new ConcurrentDictionary<int, JsonElement>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var packets = new ConcurrentQueue<long>();
        int ruleId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rule = new Rule { Name = "Hub test", Metric = "packets", Grouping = "src_ip", WindowSeconds = 1, Threshold = 0 };
            db.Rules.Add(rule); await db.SaveChangesAsync(); ruleId = rule.Id;
        }
        connection.On<JsonElement>("Alert", alert =>
        {
            if (alert.GetProperty("ruleId").GetInt32() != ruleId) return;
            alerts.TryAdd((int)alert.GetProperty("id").GetInt64(), alert);
            if (alerts.Count == 3) received.TrySetResult();
        });
        connection.On<JsonElement>("PacketEvent", _ => packets.Enqueue(Stopwatch.GetTimestamp()));
        await connection.StartAsync();
        var queue = fixture.Factory.Services.GetRequiredService<PacketQueue>();
        for (uint n = 0; n < 3; ++n)
            await queue.Channel.Writer.WriteAsync(new PacketEvent { TimestampUs = 1000000 + n, SrcIp = $"203.0.113.{n}", DstIp = "198.51.100.1", Bytes = 60 });
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(3, await db.Alerts.CountAsync(x => x.RuleId == ruleId));
        }
        // Exercise the broadcaster directly at a rate far above the sample budget.
        var live = fixture.Factory.Services.GetRequiredService<LiveEvents>();
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromMilliseconds(1100))
        {
            live.Packet(new PacketEvent { Bytes = 99 }); await Task.Delay(2);
        }
        await Task.Delay(100);
        var times = packets.ToArray(); Assert.NotEmpty(times);
        foreach (var start in times)
            Assert.True(times.Count(t => t >= start && Stopwatch.GetElapsedTime(start, t) < TimeSpan.FromMilliseconds(990)) <= 20);
        await connection.StopAsync();
    }

    [Fact]
    public async Task Anonymous_hub_connection_is_rejected()
    {
        await using var connection = new HubConnectionBuilder().WithUrl("http://localhost/hubs/live", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
        }).Build();
        await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
    }
}
