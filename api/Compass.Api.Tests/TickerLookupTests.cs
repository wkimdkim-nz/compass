using System.Net;

namespace Compass.Api.Tests;

/// <summary>
/// The ticker lookup through the HTTP seam (ticket #73, ADR 0010), on the stub ports the test host
/// boots with. What the lookup does with the real adapters, and with ports that fail, is in
/// <c>TickerLookupPortsTests</c>.
/// </summary>
public class TickerLookupTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    [Fact]
    public async Task An_unauthenticated_lookup_is_a_401()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/tickers/AAPL");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task On_the_stub_ports_a_known_ticker_answers_with_base_facts_history_and_three_scenarios()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.GetAsync("/api/tickers/aapl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal("AAPL", body.GetProperty("ticker").GetString());
        Assert.True(body.GetProperty("baseFacts").GetProperty("currentPrice").GetDouble() > 0);
        Assert.True(body.GetProperty("baseFacts").GetProperty("ttmRevenue").GetDouble() > 0);
        Assert.True(body.GetProperty("history").GetArrayLength() >= 2);
        Assert.Equal(
            ["bear", "base", "bull"],
            body.GetProperty("scenarios").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(0, body.GetProperty("missing").GetArrayLength());
    }

    [Fact]
    public async Task On_the_stub_ports_an_unknown_ticker_is_a_404()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.GetAsync("/api/tickers/NOSUCH");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The stub has a company for each answer the lookup has, so the UI can be built against all of them.</summary>
    [Fact]
    public async Task The_stub_ports_also_hold_an_unprofitable_filer_a_newly_listed_one_and_a_foreign_one()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var unprofitable = await (await client.GetAsync("/api/tickers/LCID")).Body();
        var newlyListed = await (await client.GetAsync("/api/tickers/MRP")).Body();
        var foreign = await client.GetAsync("/api/tickers/TSM");

        Assert.All(
            unprofitable.GetProperty("scenarios").EnumerateArray(),
            scenario => Assert.Equal("ps", scenario.GetProperty("exitMultiple").GetProperty("kind").GetString()));
        Assert.Equal(3, unprofitable.GetProperty("scenarios").GetArrayLength());
        Assert.Equal(0, newlyListed.GetProperty("scenarios").GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, foreign.StatusCode);
        Assert.Contains("not a US domestic filer", (await foreign.Body()).GetProperty("detail").GetString());
    }
}
