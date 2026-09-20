using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Compass.Api.Tests;

/// <summary>
/// The load balancer's health check. It is deliberately shallow: it answers without touching the
/// database, so this class boots the app against a database that is not there and demands 200
/// anyway. A deep check would turn any database fault into an endless kill-and-replace loop, and
/// would make the migration race unrecoverable — restarting a task never repairs a database.
/// </summary>
public class HealthTests(HealthTests.UnreachableDatabaseFactory factory)
    : IClassFixture<HealthTests.UnreachableDatabaseFactory>
{
    /// <summary>
    /// The app with nowhere to connect: port 1 answers nothing. No container, because the point of
    /// the fixture is the absence of a database.
    /// </summary>
    public sealed class UnreachableDatabaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.WithoutMarketDataSettings().UseSetting(
                "ConnectionStrings:Default",
                "Host=127.0.0.1;Port=1;Database=compass;Username=compass;Password=compass;Timeout=2");
    }

    [Fact]
    public async Task The_health_path_answers_an_anonymous_request_with_200_while_the_database_is_unreachable()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_database_really_is_unreachable_from_this_fixture()
    {
        // The control for the test above: without it, a fixture that had quietly found a working
        // database would prove nothing about shallowness. Registering needs the database.
        var client = factory.CreateAnonymousClient();

        var response = await client.Register(ApiClient.UniqueEmail());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
