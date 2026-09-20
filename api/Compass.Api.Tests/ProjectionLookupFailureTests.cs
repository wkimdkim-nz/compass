using System.Net;
using System.Text.Json;
using Compass.Api.MarketData;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Api.Tests;

/// <summary>
/// Reading a saved projection when the ticker lookup cannot seed it (ticket #74, ADR 0004 as
/// amended, point 2): the assumptions are answered all the same, with no base facts and the
/// lookup's reason. Each test boots its own host with the ports swapped, as <c>TickerLookupPortsTests</c> does.
/// </summary>
public class ProjectionLookupFailureTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    [Fact]
    public async Task With_the_fundamentals_source_failing_a_read_answers_the_assumptions_no_base_facts_and_the_reason()
    {
        var client = await SignedInClient(services => services.UseFundamentals(new FailingFundamentals()));
        await client.Save("AAPL", Projections.PeBase5y("bear"), Projections.PeBase5y("base"));

        var response = await client.GetAsync("/api/projections/AAPL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal(["bear", "base"], body.GetProperty("scenarios").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(0.1, body.GetProperty("scenarios")[1].GetProperty("growthRate").GetDouble());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("baseFacts").ValueKind);
        Assert.Contains("could not be fetched", body.GetProperty("baseFactsUnavailable").GetString());

        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FailingFundamentals.ProviderText, text);
        Assert.DoesNotContain("Exception", text);
    }

    /// <summary>A company typed in by hand, and a foreign filer: both save, and both reload the same way every time.</summary>
    [Theory]
    [InlineData("NOSUCH", "No company with that ticker")]
    [InlineData(StubFundamentalsSource.NotUsDomestic, "not a US domestic filer")]
    public async Task A_ticker_the_lookup_cannot_cover_still_reads_back_with_the_lookups_reason(string ticker, string reason)
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        await client.Save(ticker);

        var response = await client.GetAsync($"/api/projections/{ticker}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal(1, body.GetProperty("scenarios").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("baseFacts").ValueKind);
        Assert.Contains(reason, body.GetProperty("baseFactsUnavailable").GetString());
    }

    [Fact]
    public async Task With_only_the_quote_failing_a_read_answers_the_other_base_facts_and_names_the_price_as_missing()
    {
        var client = await SignedInClient(services => services.UseQuote(new FailingQuote()));
        await client.Save("AAPL");

        var body = await (await client.GetAsync("/api/projections/AAPL")).Body();

        var baseFacts = body.GetProperty("baseFacts");
        Assert.Equal(JsonValueKind.Null, baseFacts.GetProperty("currentPrice").ValueKind);
        Assert.Equal(100_000_000_000, baseFacts.GetProperty("ttmRevenue").GetDouble());
        Assert.Equal("2025-12-31", baseFacts.GetProperty("figuresTo").GetString());
        Assert.Equal(["currentPrice"], body.GetProperty("missing").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("baseFactsUnavailable").ValueKind);
    }

    private async Task<HttpClient> SignedInClient(Action<IServiceCollection> ports)
    {
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(ports)).CreateBrowserLikeClient();
        await client.RegisterNewUser();
        return client;
    }
}

public sealed class FailingFundamentals : IFundamentalsSource
{
    public const string ProviderText = "sec.gov said: Undeclared Automated Tool";

    public Task<FundamentalsLookup> LookUpAsync(string symbol, CancellationToken cancellationToken) =>
        throw new HttpRequestException(ProviderText);
}
