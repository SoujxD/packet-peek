using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PacketPeek.Tests;

[Collection("database")]
public class RuleTests(DatabaseFixture fixture)
{
    private static RuleRequest Valid => new("Test rule", "packets", "src_ip", 10, 2, true);
    internal static HttpClient OperatorClient(AppFactory factory, string? role = "Operator")
    {
        var claims = role is null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.Role, role)];
        var token = new JwtSecurityToken("PacketPeek", "PacketPeek", claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AppFactory.TestKey)), SecurityAlgorithms.HmacSha256));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    [Fact]
    public async Task Crud_success_and_missing_records()
    {
        using var client = OperatorClient(fixture.Factory);
        var create = await client.PostAsJsonAsync("/api/rules", Valid);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var rule = (await create.Content.ReadFromJsonAsync<Rule>())!;
        Assert.Equal($"/api/rules/{rule.Id}", create.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/rules/{rule.Id}")).StatusCode);
        Assert.Contains((await client.GetFromJsonAsync<List<Rule>>("/api/rules"))!, x => x.Id == rule.Id);
        var update = await client.PutAsJsonAsync($"/api/rules/{rule.Id}", Valid with { Metric = "bytes", Active = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False((await update.Content.ReadFromJsonAsync<Rule>())!.Active);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/rules/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/rules/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/rules/{rule.Id}", Valid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/rules/{rule.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("Name", "")]
    [InlineData("Metric", "http")]
    [InlineData("Grouping", "dst_ip")]
    [InlineData("WindowSeconds", "0")]
    [InlineData("WindowSeconds", "86401")]
    [InlineData("Threshold", "-1")]
    public async Task Invalid_rules_are_rejected_on_create_and_update(string field, string value)
    {
        using var client = OperatorClient(fixture.Factory);
        var invalid = field switch
        {
            "Name" => Valid with { Name = value }, "Metric" => Valid with { Metric = value },
            "Grouping" => Valid with { Grouping = value }, "WindowSeconds" => Valid with { WindowSeconds = int.Parse(value) },
            _ => Valid with { Threshold = long.Parse(value) }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/rules", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/rules/999999", invalid)).StatusCode);
    }

    [Fact]
    public async Task Authentication_and_role_are_required_for_every_rules_endpoint()
    {
        foreach (var authenticated in new[] { false, true })
        {
            using var client = authenticated ? OperatorClient(fixture.Factory, "Reader") : fixture.Factory.CreateClient();
            var expected = authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
            Assert.Equal(expected, (await client.GetAsync("/api/rules")).StatusCode);
            Assert.Equal(expected, (await client.GetAsync("/api/rules/1")).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync("/api/rules", Valid)).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync("/api/rules/1", Valid)).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync("/api/rules/1")).StatusCode);
        }
        using var authorized = OperatorClient(fixture.Factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await authorized.PostAsync("/api/rules", new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await authorized.PostAsJsonAsync("/api/rules", Valid with { Name = new string('x', 101) })).StatusCode);
    }

    [Fact]
    public async Task Login_uses_identity_password_and_operator_role()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Operator")) Assert.True((await roles.CreateAsync(new IdentityRole("Operator"))).Succeeded);
        var user = new User { UserName = "operator-test", LockoutEnabled = true };
        Assert.True((await users.CreateAsync(user, "Test-only-Password123!")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded);
        using var client = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/token", new LoginRequest("", ""))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/token", new LoginRequest("missing", "bad"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/token", new LoginRequest(user.UserName, "bad"))).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/token", new LoginRequest(user.UserName, "Test-only-Password123!"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/rules")).StatusCode);
    }
}
