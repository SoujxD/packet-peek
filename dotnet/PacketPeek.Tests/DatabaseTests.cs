using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace PacketPeek.Tests;

public class DatabaseFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    public AppFactory Factory { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        Factory = new AppFactory { Connection = Container.GetConnectionString() };
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Container.DisposeAsync();
    }
}

[CollectionDefinition("database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

[Collection("database")]
public class DatabaseTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Migration_creates_all_tables_and_is_idempotent()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        foreach (var table in new[] { "AspNetUsers", "Rules", "Alerts", "Flows", "__EFMigrationsHistory" })
        {
            await using var command = new NpgsqlCommand("select count(*) from information_schema.tables where table_schema = 'public' and table_name = @table", connection);
            command.Parameters.AddWithValue("table", table);
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
