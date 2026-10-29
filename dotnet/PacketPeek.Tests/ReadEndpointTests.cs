using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PacketPeek.Tests;

[Collection("database")]
public class ReadEndpointTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Lists_drilldown_and_history_protection_work()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = new Rule { Name = "History", WindowSeconds = 1, Threshold = 0 };
        db.Rules.Add(rule); await db.SaveChangesAsync();
        var alert = new Alert { RuleId = rule.Id, FiredAt = DateTimeOffset.UtcNow, GroupKey = "192.0.2.1", Value = 1 };
        db.Alerts.Add(alert); db.Flows.Add(new FlowSummary { SrcIp = "192.0.2.1", DstIp = "198.51.100.1", Packets = 1 });
        await db.SaveChangesAsync();
        using var client = RuleTests.OperatorClient(fixture.Factory);
        Assert.NotEmpty((await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/flows"))!);
        Assert.NotEmpty((await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/alerts"))!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/alerts/{alert.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/alerts/9223372036854775807")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/rules/{rule.Id}")).StatusCode);
        foreach (var path in new[] { "/api/flows", "/api/alerts", $"/api/alerts/{alert.Id}" })
        {
            using var anonymous = fixture.Factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        }
    }
}
