using Compass.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Compass.Api.Tests;

/// <summary>
/// Boots the real API in-process over a real Postgres (Testcontainers). Used as an
/// <see cref="IClassFixture{T}"/>, so every test class gets its own container and a
/// clean schema, applied by the same migrations the app ships.
/// </summary>
public class CompassApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("compass_test")
        .WithUsername("compass")
        .WithPassword("compass")
        .Build();

    /// <summary>The hosting environment the app boots in. Development unless a test overrides it.</summary>
    public string Environment { get; protected init; } = "Development";

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrations are applied explicitly, never on app startup (see README) — so the
        // test harness is where `dotnet ef database update` happens for the test database.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
