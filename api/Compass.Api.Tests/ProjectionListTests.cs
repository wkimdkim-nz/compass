using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;

namespace Compass.Api.Tests;

/// <summary>Listing the caller's saved projections through the HTTP seam (ADR 0004, amended 2026-11-24).</summary>
public class ProjectionListTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    [Fact]
    public async Task A_new_user_has_an_empty_list()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.GetAsync("/api/projections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.Body()).GetArrayLength());
    }

    /// <summary>
    /// On a host whose fundamentals and quote sources throw, so any lookup the list made would turn
    /// it into a 500. The clock moves a minute between saves, so the order is the stamps' and not the insert's.
    /// </summary>
    [Fact]
    public async Task The_list_is_newest_first_by_last_save_and_calls_no_provider()
    {
        var clock = new ManualClock();
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
                .UseFundamentals(new FailingFundamentals())
                .UseQuote(new FailingQuote())
                .UseClock(clock)))
            .CreateBrowserLikeClient();
        await client.RegisterNewUser();
        foreach (var ticker in new[] { "AAPL", "MSFT", "NVDA" })
        {
            Assert.Equal(HttpStatusCode.Created, (await client.Save(ticker)).StatusCode);
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        var update = await client.PutAsJsonAsync("/api/projections/AAPL", Projections.Request(null, Projections.PeFacts, Projections.PeBase5y("bull")));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var response = await client.GetAsync("/api/projections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Body()).EnumerateArray().ToList();
        Assert.Equal(["AAPL", "NVDA", "MSFT"], list.Select(p => p.GetProperty("ticker").GetString()));
        Assert.Equal((await update.Body()).GetProperty("updatedAt").GetString(), list[0].GetProperty("updatedAt").GetString());
    }

    [Fact]
    public async Task Each_entry_names_its_company_and_only_the_scenarios_saved_bear_base_bull_in_that_order()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        await client.Save("brk.b", Projections.PeBase5y("bull"), Projections.PeBase5y("bear"));

        var entry = Assert.Single((await (await client.GetAsync("/api/projections")).Body()).EnumerateArray());

        Assert.Equal("BRK-B", entry.GetProperty("ticker").GetString());
        Assert.Equal("Made Up Inc.", entry.GetProperty("companyName").GetString());
        Assert.Equal(["bear", "bull"], entry.GetProperty("scenarios").EnumerateArray().Select(s => s.GetString()));
        Assert.True(DateTimeOffset.TryParse(entry.GetProperty("updatedAt").GetString(), out _));
        Assert.Equal(["ticker", "companyName", "scenarios", "updatedAt"], entry.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task A_projection_typed_in_by_hand_is_listed_too()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        await client.PostAsJsonAsync("/api/projections", Projections.Request("NOSUCH", Projections.PeFacts, Projections.PeBase5y("base")) with { Cik = null, CompanyName = "Corner Shop Ltd" });

        var entry = Assert.Single((await (await client.GetAsync("/api/projections")).Body()).EnumerateArray());

        Assert.Equal("NOSUCH", entry.GetProperty("ticker").GetString());
        Assert.Equal("Corner Shop Ltd", entry.GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task Only_the_callers_own_projections_are_listed()
    {
        var alice = factory.CreateBrowserLikeClient();
        await alice.RegisterNewUser();
        await alice.Save("AAPL");
        var bob = factory.CreateBrowserLikeClient();
        await bob.RegisterNewUser();
        await bob.Save("TSLA");

        var bobs = (await (await bob.GetAsync("/api/projections")).Body()).EnumerateArray();

        Assert.Equal(["TSLA"], bobs.Select(p => p.GetProperty("ticker").GetString()));
    }

    [Fact]
    public async Task Signed_out_visitors_are_refused_with_401()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/projections");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
