using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Compass.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Api.Tests;

/// <summary>
/// Saving, replacing and loading a projection through the HTTP seam (ticket #74, ADR 0004 as
/// amended), on the stub ports the test host boots with. Reading with a lookup that cannot seed is
/// in <c>ProjectionLookupFailureTests</c>.
/// </summary>
/// <remarks>
/// The expected outputs are the vectors file's, <c>pe-base-5y</c> and <c>ps-base-5y</c>, sent with
/// that file's inputs: hand-computed there, not worked out again here.
/// </remarks>
public class ProjectionTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    private const int Precision = 9;

    [Fact]
    public async Task Creating_answers_201_with_the_saved_record_and_each_scenarios_recomputed_outputs()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.PostAsJsonAsync("/api/projections", Projections.Request(
            "aapl",
            Projections.PeFacts,
            Projections.PeBase5y("base"),
            Projections.PeBase5y("bull") with { GrowthRate = 0.1, ExitMultiple = new("pe", 40) }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/projections/AAPL", response.Headers.Location?.ToString());
        var body = await response.Body();
        Assert.Equal("AAPL", body.GetProperty("ticker").GetString());
        Assert.Equal("Made Up Inc.", body.GetProperty("companyName").GetString());
        Assert.Equal(320193, body.GetProperty("cik").GetInt64());
        Assert.Equal(body.GetProperty("createdAt").GetString(), body.GetProperty("updatedAt").GetString());

        var scenarios = body.GetProperty("scenarios").EnumerateArray().ToList();
        Assert.Equal(["base", "bull"], scenarios.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(0.02, scenarios[0].GetProperty("dilutionRate").GetDouble());
        Assert.Equal("pe", scenarios[0].GetProperty("exitMultiple").GetProperty("kind").GetString());
        AssertOutputs(scenarios[0], futurePrice: 58.347541061567114, x: 1.4586885265391779, cagr: 0.078431372549019608);
        // Twice the exit multiple is twice the price: X doubles, and 1 + CAGR is 55/51 times the fifth root of two, 1.148698354997035.
        AssertOutputs(scenarios[1], futurePrice: 2 * 58.347541061567114, x: 2 * 1.4586885265391779, cagr: 0.2387923436242534);

        // No base facts and no outputs are anywhere in what was saved, only in what was answered.
        Assert.False(body.TryGetProperty("baseFacts", out _));
    }

    [Fact]
    public async Task The_price_to_sales_path_is_recomputed_too()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.PostAsJsonAsync("/api/projections", Projections.Request(
            "LCID", Projections.PsFacts, Projections.PsBase5y("base")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scenario = Assert.Single((await response.Body()).GetProperty("scenarios").EnumerateArray());
        AssertOutputs(scenario, futurePrice: 143.46792598909772, x: 2.3911320998182954, cagr: 0.19047619047619048);
    }

    [Fact]
    public async Task A_second_create_for_the_same_company_answers_409_however_the_ticker_is_cased()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        await client.Save("MSFT");

        var second = await client.PostAsJsonAsync("/api/projections", Projections.Request(
            " msft ", Projections.PeFacts, Projections.PeBase5y("base") with { GrowthRate = 0.5 }));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(409, (await second.Body()).GetProperty("status").GetInt32());
        // The first is as it was: a refused create overwrote nothing.
        var saved = await (await client.GetAsync("/api/projections/MSFT")).Body();
        Assert.Equal(0.1, saved.GetProperty("scenarios")[0].GetProperty("growthRate").GetDouble());
    }

    [Fact]
    public async Task A_horizon_of_zero_answers_400_with_a_Problem_body_naming_the_field_and_saves_nothing()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.PostAsJsonAsync("/api/projections", Projections.Request(
            "NVDA", Projections.PeFacts, Projections.PeBase5y("bear"), Projections.PeBase5y("base") with { Horizon = 0 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await response.Body()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("scenarios[1].horizon", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projections/NVDA")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task What_the_calculator_or_the_record_cannot_take_answers_400_under_the_field_at_fault(string field, object request)
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.PostAsJsonAsync("/api/projections", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Body()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"No error under {field} in {errors}");
    }

    [Theory]
    [InlineData("baseFacts.currentPrice")]
    [InlineData("baseFacts.ttmRevenue")]
    [InlineData("baseFacts.dilutedShares")]
    [InlineData("scenarios[0].growthRate")]
    [InlineData("scenarios[0].futureNetMargin")]
    [InlineData("scenarios[0].exitMultiple.value")]
    [InlineData("scenarios[0].dilutionRate")]
    [InlineData("scenarios[0].horizon")]
    public async Task A_figure_left_out_answers_400_under_its_field_rather_than_being_saved_as_zero(string field)
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        var body = JsonSerializer.SerializeToNode(
            Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base")), JsonSerializerOptions.Web)!;
        var path = field.Replace("[0]", ".0").Split('.');
        var parent = path[..^1].Aggregate(body, (node, step) => int.TryParse(step, out var index) ? node[index]! : node[step]!);
        Assert.True(parent.AsObject().Remove(path[^1]), $"The request has no {field} to leave out.");

        var response = await client.PostAsync("/api/projections", JsonContent.Create(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Body()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"No error under {field} in {errors}");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projections/AMD")).StatusCode);
    }

    public static TheoryData<string, object> InvalidRequests() => new()
    {
        { "ticker", Projections.Request("  ", Projections.PeFacts, Projections.PeBase5y("base")) },
        { "companyName", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base")) with { CompanyName = "" } },
        { "baseFacts.currentPrice", Projections.Request("AMD", Projections.PeFacts with { CurrentPrice = 0 }, Projections.PeBase5y("base")) },
        { "baseFacts.dilutedShares", Projections.Request("AMD", Projections.PeFacts with { DilutedShares = -1 }, Projections.PeBase5y("base")) },
        { "baseFacts", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base")) with { BaseFacts = null } },
        { "scenarios", Projections.Request("AMD", Projections.PeFacts) },
        { "scenarios", Projections.Request("AMD", Projections.PeFacts,
            Projections.PeBase5y("bear"), Projections.PeBase5y("base"), Projections.PeBase5y("bull"), Projections.PeBase5y("bear")) },
        { "scenarios[1].name", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base"), Projections.PeBase5y("base")) },
        { "scenarios[0].name", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("sideways")) },
        { "scenarios[0].exitMultiple.kind", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base") with { ExitMultiple = new("ev", 20) }) },
        { "scenarios[0].exitMultiple", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base") with { ExitMultiple = null }) },
        { "scenarios[0].growthRate", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base") with { GrowthRate = -1.5 }) },
        { "scenarios[0].dilutionRate", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base") with { DilutionRate = -1 }) },
        { "scenarios[0].futureNetMargin", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base") with { FutureNetMargin = -0.1 }) },
        // More than a numeric column's worth, and more than a double's once compounded.
        { "scenarios[0].growthRate", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base") with { GrowthRate = 1e30 }) },
        { "scenarios[0]", Projections.Request("AMD", Projections.PeFacts, Projections.PeBase5y("base") with { GrowthRate = 1e20, Horizon = 100 }) },
    };

    [Fact]
    public async Task Replacing_answers_200_with_recomputed_outputs_and_the_new_scenarios_in_place_of_the_old()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        var created = await (await client.Save("GOOGL", Projections.PeBase5y("bear"), Projections.PeBase5y("base"))).Body();

        var response = await client.PutAsJsonAsync("/api/projections/googl", Projections.Request(
            null, Projections.PsFacts, Projections.PsBase5y("bull")) with { CompanyName = "Alphabet Inc.", Cik = null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal("GOOGL", body.GetProperty("ticker").GetString());
        Assert.Equal("Alphabet Inc.", body.GetProperty("companyName").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cik").ValueKind);
        Assert.Equal(created.GetProperty("createdAt").GetString(), body.GetProperty("createdAt").GetString());
        Assert.True(body.GetProperty("updatedAt").GetDateTimeOffset() >= body.GetProperty("createdAt").GetDateTimeOffset());
        var scenario = Assert.Single(body.GetProperty("scenarios").EnumerateArray());
        Assert.Equal("bull", scenario.GetProperty("name").GetString());
        AssertOutputs(scenario, futurePrice: 143.46792598909772, x: 2.3911320998182954, cagr: 0.19047619047619048);

        // And that is what reads back: the replace reached the database, and the stamps it answered with are the stored ones.
        var read = await (await client.GetAsync("/api/projections/GOOGL")).Body();
        Assert.Equal(["bull"], read.GetProperty("scenarios").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(body.GetProperty("updatedAt").GetString(), read.GetProperty("updatedAt").GetString());
    }

    [Fact]
    public async Task Replacing_a_scenario_with_one_of_the_same_name_keeps_the_new_assumptions()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        await client.Save("META", Projections.PeBase5y("base"), Projections.PeBase5y("bull"));

        var response = await client.PutAsJsonAsync("/api/projections/META", Projections.Request(
            null, Projections.PeFacts, Projections.PeBase5y("bull") with { GrowthRate = 0.3 }, Projections.PeBase5y("base") with { GrowthRate = 0.2 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await (await client.GetAsync("/api/projections/META")).Body();
        Assert.Equal(
            [("base", 0.2), ("bull", 0.3)],
            read.GetProperty("scenarios").EnumerateArray().Select(s => (s.GetProperty("name").GetString()!, s.GetProperty("growthRate").GetDouble())));
    }

    [Fact]
    public async Task A_share_class_is_one_company_however_its_ticker_is_punctuated()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var created = await client.Save("brk.b");
        var second = await client.Save("BRK-B");
        var read = await client.GetAsync("/api/projections/BRK.B");

        Assert.Equal("BRK-B", (await created.Body()).GetProperty("ticker").GetString());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Replacing_where_none_exists_answers_404_and_creates_nothing()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.PutAsJsonAsync("/api/projections/JPM", Projections.Request(null, Projections.PeFacts, Projections.PeBase5y("base")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projections/JPM")).StatusCode);
    }

    [Fact]
    public async Task Replacing_validates_as_creating_does_and_a_body_naming_another_ticker_is_refused()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        await client.Save("TSLA");

        var invalid = await client.PutAsJsonAsync("/api/projections/TSLA", Projections.Request(null, Projections.PeFacts, Projections.PeBase5y("base") with { Horizon = -1 }));
        var otherTicker = await client.PutAsJsonAsync("/api/projections/TSLA", Projections.Request("AAPL", Projections.PeFacts, Projections.PeBase5y("base")));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await invalid.Body()).GetProperty("errors").TryGetProperty("scenarios[0].horizon", out _));
        Assert.Equal(HttpStatusCode.BadRequest, otherTicker.StatusCode);
        Assert.True((await otherTicker.Body()).GetProperty("errors").TryGetProperty("ticker", out _));
    }

    [Fact]
    public async Task Reading_answers_the_saved_assumptions_with_live_base_facts_and_the_figures_to_date()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();
        // Saved against figures of the user's own: what reads back beside the assumptions is the lookup's, not these.
        await client.PostAsJsonAsync("/api/projections", Projections.Request(
            "AAPL", Projections.PeFacts, Projections.PeBase5y("base") with { GrowthRate = 0.03275991627577457 }));

        var response = await client.GetAsync("/api/projections/aapl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal("AAPL", body.GetProperty("ticker").GetString());
        Assert.Equal("Made Up Inc.", body.GetProperty("companyName").GetString());
        var scenario = Assert.Single(body.GetProperty("scenarios").EnumerateArray());
        // To the last digit: a numeric column holds the rate as it was sent.
        Assert.Equal(0.03275991627577457, scenario.GetProperty("growthRate").GetDouble());
        Assert.Equal(0.2, scenario.GetProperty("futureNetMargin").GetDouble());
        Assert.Equal("pe", scenario.GetProperty("exitMultiple").GetProperty("kind").GetString());
        Assert.Equal(20, scenario.GetProperty("exitMultiple").GetProperty("value").GetDouble());
        Assert.Equal(0.02, scenario.GetProperty("dilutionRate").GetDouble());
        Assert.Equal(5, scenario.GetProperty("horizon").GetInt32());
        Assert.False(scenario.TryGetProperty("outputs", out _));

        // The stub's Apple, at the stub's price.
        var baseFacts = body.GetProperty("baseFacts");
        Assert.Equal(100, baseFacts.GetProperty("currentPrice").GetDouble());
        Assert.Equal(100_000_000_000, baseFacts.GetProperty("ttmRevenue").GetDouble());
        Assert.Equal(5_000_000_000, baseFacts.GetProperty("dilutedShares").GetDouble());
        Assert.Equal(0.25, baseFacts.GetProperty("currentNetMargin").GetDouble());
        Assert.Equal("2025-12-31", baseFacts.GetProperty("figuresTo").GetString());
        Assert.Equal(0, body.GetProperty("missing").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("baseFactsUnavailable").ValueKind);
    }

    [Fact]
    public async Task Reading_where_no_projection_is_saved_answers_404_even_for_a_ticker_the_lookup_knows()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.RegisterNewUser();

        var response = await client.GetAsync("/api/projections/AAPL");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task One_users_projection_is_a_404_to_another_on_read_and_replace_and_no_bar_to_their_own()
    {
        var alice = factory.CreateBrowserLikeClient();
        await alice.RegisterNewUser();
        await alice.Save("AAPL");
        var bob = factory.CreateBrowserLikeClient();
        await bob.RegisterNewUser();

        var read = await bob.GetAsync("/api/projections/AAPL");
        var replace = await bob.PutAsJsonAsync("/api/projections/AAPL", Projections.Request(null, Projections.PsFacts, Projections.PsBase5y("bull")));
        var create = await bob.Save("AAPL", Projections.PeBase5y("bear"));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, replace.StatusCode);
        // The uniqueness is per user: Alice's projection is not Bob's conflict.
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var alices = await (await alice.GetAsync("/api/projections/AAPL")).Body();
        Assert.Equal(["base"], alices.GetProperty("scenarios").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Signed_out_visitors_are_refused_with_401_on_create_replace_and_read()
    {
        var client = factory.CreateAnonymousClient();
        var request = Projections.Request("AAPL", Projections.PeFacts, Projections.PeBase5y("base"));

        var create = await client.PostAsJsonAsync("/api/projections", request);
        var replace = await client.PutAsJsonAsync("/api/projections/AAPL", request);
        var read = await client.GetAsync("/api/projections/AAPL");

        Assert.All([create, replace, read], response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        });
    }

    [Fact]
    public async Task A_state_changing_POST_succeeds_carrying_only_the_session_cookie()
    {
        // The executable record of "no antiforgery": the session cookie is the only thing the
        // browser has, and nothing is attached by hand.
        var client = factory.CreateBrowserLikeClient();
        var registered = await client.Register(ApiClient.UniqueEmail());
        Assert.Single(registered.Headers.GetValues("Set-Cookie")); // only compass_auth, no token cookie
        Assert.Empty(client.DefaultRequestHeaders);

        var created = await client.Save("AAPL");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_projection_and_its_scenarios_in_the_database()
    {
        var client = factory.CreateBrowserLikeClient();
        var me = await client.RegisterNewUser();
        await client.Save("AAPL", Projections.PeBase5y("bear"), Projections.PeBase5y("base"));

        Guid projectionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            projectionId = (await db.Projections.AsNoTracking().SingleAsync(p => p.UserId == me.Id)).Id;
            Assert.Equal(2, await db.Scenarios.CountAsync(s => s.ProjectionId == projectionId));

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(me.Id.ToString());
            Assert.True((await users.DeleteAsync(user!)).Succeeded);
        }

        // Asked of Postgres directly, with a fresh context: the user is gone, so there is no session
        // to ask the API with, and the cascade fired in the database, not in EF's change tracker.
        using var check = factory.Services.CreateScope();
        var remaining = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await remaining.Projections.CountAsync(p => p.UserId == me.Id));
        Assert.Equal(0, await remaining.Scenarios.CountAsync(s => s.ProjectionId == projectionId));
    }

    private static void AssertOutputs(JsonElement scenario, double futurePrice, double x, double cagr)
    {
        var outputs = scenario.GetProperty("outputs");
        Assert.Equal(futurePrice, outputs.GetProperty("futurePrice").GetDouble(), Precision);
        Assert.Equal(x, outputs.GetProperty("x").GetDouble(), Precision);
        Assert.Equal(cagr, outputs.GetProperty("cagr").GetDouble(), Precision);
    }
}

public record ExitMultipleBody(string? Kind, double Value);

public record ScenarioBody(
    string? Name,
    double GrowthRate,
    double FutureNetMargin,
    ExitMultipleBody? ExitMultiple,
    double DilutionRate,
    int Horizon);

public record BaseFactsBody(double CurrentPrice, double TtmRevenue, double DilutedShares);

public record ProjectionBody(string? Ticker, string? CompanyName, long? Cik, BaseFactsBody? BaseFacts, ScenarioBody[] Scenarios);

/// <summary>Projection requests as a browser would send them, on the vectors file's two base cases.</summary>
public static class Projections
{
    public static readonly BaseFactsBody PeFacts = new(40, 1_000_000_000, 100_000_000);

    public static readonly BaseFactsBody PsFacts = new(60, 500_000_000, 50_000_000);

    public static ScenarioBody PeBase5y(string name) => new(name, 0.1, 0.2, new("pe", 20), 0.02, 5);

    public static ScenarioBody PsBase5y(string name) => new(name, 0.25, 0.08, new("ps", 6), 0.05, 5);

    public static ProjectionBody Request(string? ticker, BaseFactsBody facts, params ScenarioBody[] scenarios) =>
        new(ticker, "Made Up Inc.", 320193, facts, scenarios);

    /// <summary>Creates a projection for the ticker: one Base scenario unless others are given.</summary>
    public static Task<HttpResponseMessage> Save(this HttpClient client, string ticker, params ScenarioBody[] scenarios) =>
        client.PostAsJsonAsync("/api/projections", Request(ticker, PeFacts, scenarios is [] ? [PeBase5y("base")] : scenarios));
}
