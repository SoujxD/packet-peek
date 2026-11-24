using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PacketPeek.Tests;

public class AppFactory : WebApplicationFactory<Program>
{
    public string? Connection { get; init; }
    public const string TestKey = "test-only-signing-key-32-bytes-minimum-12345";
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../PacketPeek")))
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestKey,
            ["ConnectionStrings:Postgres"] = Connection ?? "Host=localhost;Database=packetpeek;Username=packetpeek"
        }));
}

public class HealthTests : IClassFixture<AppFactory>
{
    private readonly AppFactory factory;
    public HealthTests(AppFactory factory) => this.factory = factory;

    [Fact]
    public async Task Health_returns_ok()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Liveness_returns_ok_without_database_access()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("live", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Metrics_endpoint_exports_packetpeek_metrics()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/metrics");
        response.EnsureSuccessStatusCode();
        var metrics = await response.Content.ReadAsStringAsync();
        Assert.Contains("packetpeek_packets_processed_total", metrics);
        Assert.Contains("packetpeek_flows_created_total", metrics);
        Assert.Contains("packetpeek_alerts_triggered_total", metrics);
        Assert.Contains("packetpeek_channel_depth", metrics);
        Assert.Contains("packetpeek_channel_capacity", metrics);
        Assert.Contains("packetpeek_channel_utilization_ratio", metrics);
        Assert.Contains("packetpeek_rule_evaluations_total", metrics);
        Assert.Contains("packetpeek_rule_evaluation_duration_seconds", metrics);
        Assert.Contains("packetpeek_engine_running", metrics);
        Assert.Contains("packetpeek_engine_capture_expected", metrics);
        Assert.Contains("packetpeek_engine_errors_total", metrics);
        Assert.Contains("packetpeek_db_operation_duration_seconds", metrics);
        Assert.Contains("packetpeek_db_operation_failures_total", metrics);
        Assert.Contains("http_requests_received_total", metrics);
        Assert.Contains("http_request_duration_seconds", metrics);
        Assert.Contains("http_requests_in_progress", metrics);
    }
}
