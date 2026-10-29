using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PacketPeek.Tests;

public class AppFactory : WebApplicationFactory<Program>
{
    public const string TestKey = "test-only-signing-key-32-bytes-minimum-12345";
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestKey
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
}
