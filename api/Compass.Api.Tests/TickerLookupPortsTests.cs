using System.Net;
using System.Text.Json;
using Compass.Api.MarketData;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Api.Tests;

/// <summary>
/// The ticker lookup through the HTTP seam with its ports swapped (ticket #73, ADR 0010): the real
/// EDGAR adapter over the recorded documents, the real Finnhub adapter over a made-up network, and
/// ports that fail or count their calls. Each test boots its own host over the one database, at
/// the injection point the stubs use.
/// </summary>
/// <remarks>
/// The seeded figures are worked out by hand from the histories <see cref="EdgarNormaliserTests"/>
/// derives from the same fixtures, and the sums are written beside them.
/// </remarks>
public class TickerLookupPortsTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    private const int Precision = 12;

    private static readonly Fundamentals AnyCompany = new(100, 10, 0.1, 50, new DateOnly(2025, 12, 31), []);

    /// <summary>
    /// Apple, at a price of 200. Revenue 365,817 → 416,161 over four years; diluted shares
    /// 16,864,919,000 → 15,004,697,000, a buyback; TTM net income 128,930m on 14,714,676,000 shares.
    /// </summary>
    [Fact]
    public async Task A_profitable_filer_is_seeded_with_its_revenue_cagr_a_negative_dilution_and_its_pe()
    {
        var client = await SignedInClient(services => services.UseRecordedEdgar().UseQuote(new FixedQuote(200)));

        var response = await client.GetAsync("/api/tickers/AAPL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal("AAPL", body.GetProperty("ticker").GetString());
        Assert.Equal("Apple Inc.", body.GetProperty("companyName").GetString());
        Assert.Equal(320193, body.GetProperty("cik").GetInt64());

        var baseFacts = body.GetProperty("baseFacts");
        Assert.Equal(200, baseFacts.GetProperty("currentPrice").GetDouble());
        Assert.Equal(466_823_000_000, baseFacts.GetProperty("ttmRevenue").GetDouble());
        Assert.Equal(14_714_676_000, baseFacts.GetProperty("dilutedShares").GetDouble());
        Assert.Equal(128_930.0 / 466_823, baseFacts.GetProperty("currentNetMargin").GetDouble(), Precision);
        Assert.Equal("2026-06-27", baseFacts.GetProperty("figuresTo").GetString());

        Assert.Equal(5, body.GetProperty("history").GetArrayLength());
        Assert.Equal("2021-09-25", body.GetProperty("history")[0].GetProperty("fiscalYearEnd").GetString());
        Assert.Equal(365_817_000_000, body.GetProperty("history")[0].GetProperty("revenue").GetDouble());

        // (416,161 ÷ 365,817)^¼ − 1 = 0.0327599…; Bear is half of it and Bull half as much again.
        const double growth = 0.03275991627577457;
        var scenarios = Scenarios(body);
        Assert.Equal(["bear", "base", "bull"], scenarios.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(0.016379958137887285, scenarios[0].GetProperty("growthRate").GetDouble(), Precision);
        Assert.Equal(growth, scenarios[1].GetProperty("growthRate").GetDouble(), Precision);
        Assert.Equal(0.049139874413661855, scenarios[2].GetProperty("growthRate").GetDouble(), Precision);
        Assert.All(scenarios, scenario =>
        {
            Assert.Equal(128_930.0 / 466_823, scenario.GetProperty("futureNetMargin").GetDouble(), Precision);
            // (15,004,697 ÷ 16,864,919)^¼ − 1.
            Assert.Equal(-0.028795373563374027, scenario.GetProperty("dilutionRate").GetDouble(), Precision);
            Assert.Equal(5, scenario.GetProperty("horizon").GetInt32());
            Assert.Equal("pe", scenario.GetProperty("exitMultiple").GetProperty("kind").GetString());
            // 200 ÷ (128,930m ÷ 14,714,676,000 shares).
            Assert.Equal(22.825837276041263, scenario.GetProperty("exitMultiple").GetProperty("value").GetDouble(), Precision);
        });
        Assert.Equal(0, body.GetProperty("missing").GetArrayLength());
    }

    /// <summary>
    /// CoreWeave, at a price of 100: TTM net income −1,928m, so the multiple is price to sales,
    /// 100 ÷ (7,590m ÷ 551,000,000 shares). Revenue 229 → 5,131 and shares 192m → 436m over two years.
    /// </summary>
    [Fact]
    public async Task An_unprofitable_filer_is_seeded_with_its_ps()
    {
        var client = await SignedInClient(services => services.UseRecordedEdgar().UseQuote(new FixedQuote(100)));

        var scenarios = Scenarios(await (await client.GetAsync("/api/tickers/CRWV")).Body());

        Assert.Equal(3, scenarios.Count);
        Assert.Equal(3.7335096426560606, scenarios[1].GetProperty("growthRate").GetDouble(), Precision);
        Assert.All(scenarios, scenario =>
        {
            Assert.Equal("ps", scenario.GetProperty("exitMultiple").GetProperty("kind").GetString());
            Assert.Equal(7.259552042160738, scenario.GetProperty("exitMultiple").GetProperty("value").GetDouble(), Precision);
            Assert.Equal(-1_928.0 / 7_590, scenario.GetProperty("futureNetMargin").GetDouble(), Precision);
            Assert.Equal(0.5069284433354271, scenario.GetProperty("dilutionRate").GetDouble(), Precision);
        });
    }

    [Fact]
    public async Task A_filer_with_one_year_of_history_has_base_facts_and_no_scenarios()
    {
        var client = await SignedInClient(services => services.UseRecordedEdgar().UseQuote(new FixedQuote(30)));

        var response = await client.GetAsync("/api/tickers/MRP");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal(760_542_000, body.GetProperty("baseFacts").GetProperty("ttmRevenue").GetDouble());
        Assert.Equal(1, body.GetProperty("history").GetArrayLength());
        Assert.Empty(Scenarios(body));
    }

    /// <summary>No recorded filer is shrinking. Revenue 400 → 100 over two years is −50% a year: Bear −75%, Bull −25%.</summary>
    [Fact]
    public async Task A_shrinking_company_still_has_bear_below_base_below_bull()
    {
        var shrinking = new Fundamentals(100, 10, 0.1, 50, new DateOnly(2025, 12, 31),
        [
            new AnnualFigures(new DateOnly(2023, 12, 31), 400, 40, 50),
            new AnnualFigures(new DateOnly(2024, 12, 31), 200, 20, 50),
            new AnnualFigures(new DateOnly(2025, 12, 31), 100, 10, 50),
        ]);
        var client = await SignedInClient(services => services
            .UseFundamentals(new FixedFundamentals(shrinking))
            .UseQuote(new FixedQuote(10)));

        var scenarios = Scenarios(await (await client.GetAsync("/api/tickers/SHRK")).Body());

        Assert.Equal([-0.75, -0.5, -0.25], scenarios.Select(s => s.GetProperty("growthRate").GetDouble()));
    }

    /// <summary>
    /// Revenue 100 → 1 in a year is −99%, and Bear at base − ½|base| would be −148.5%: a rate both
    /// calculators reject. Bear stops at −100%, the lowest they take.
    /// </summary>
    [Fact]
    public async Task A_collapsing_company_has_bear_no_lower_than_losing_everything()
    {
        var collapsing = new Fundamentals(1, 0.1, 0.1, 50, new DateOnly(2025, 12, 31),
        [
            new AnnualFigures(new DateOnly(2024, 12, 31), 100, 10, 50),
            new AnnualFigures(new DateOnly(2025, 12, 31), 1, 0.1, 50),
        ]);
        var client = await SignedInClient(services => services
            .UseFundamentals(new FixedFundamentals(collapsing))
            .UseQuote(new FixedQuote(10)));

        var scenarios = Scenarios(await (await client.GetAsync("/api/tickers/CLPS")).Body());

        var rates = scenarios.Select(s => s.GetProperty("growthRate").GetDouble()).ToList();
        Assert.Equal(-1, rates[0]);
        Assert.Equal(-0.99, rates[1], Precision);
        Assert.Equal(-0.495, rates[2], Precision);
    }

    /// <summary>
    /// Over one year any ratio has a root, so revenue 100 → −20 would read as growth of −120% a
    /// year. There is no rate to speak of, and so nothing to seed.
    /// </summary>
    [Fact]
    public async Task A_history_that_ends_below_nothing_seeds_no_scenarios()
    {
        var nonsense = new Fundamentals(100, 10, 0.1, 50, new DateOnly(2025, 12, 31),
        [
            new AnnualFigures(new DateOnly(2024, 12, 31), 100, 10, 50),
            new AnnualFigures(new DateOnly(2025, 12, 31), -20, 10, 50),
        ]);
        var client = await SignedInClient(services => services.UseFundamentals(new FixedFundamentals(nonsense)));

        var body = await (await client.GetAsync("/api/tickers/ODD")).Body();

        Assert.Empty(Scenarios(body));
    }

    [Fact]
    public async Task A_ticker_the_SEC_does_not_list_is_a_404()
    {
        var client = await SignedInClient(services => services.UseRecordedEdgar());

        var response = await client.GetAsync("/api/tickers/NOSUCHTICKER");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>TSMC files a 20-F in IFRS tags: a ticker EDGAR knows, so never a 404 (ADR 0010).</summary>
    [Fact]
    public async Task A_20F_issuer_is_a_422_that_says_in_plain_words_why()
    {
        var client = await SignedInClient(services => services.UseRecordedEdgar());

        var response = await client.GetAsync("/api/tickers/TSM");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var detail = (await response.Body()).GetProperty("detail").GetString();
        Assert.Contains("not a US domestic filer", detail);
    }

    [Fact]
    public async Task A_company_EDGAR_holds_no_facts_for_is_a_422_too()
    {
        var client = await SignedInClient(services =>
            services.UseRecordedEdgar(new RecordedEdgar { CompanyFactsStatus = HttpStatusCode.NotFound }));

        var response = await client.GetAsync("/api/tickers/AAPL");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task When_EDGAR_fails_the_lookup_is_a_503_that_repeats_nothing_EDGAR_said()
    {
        var client = await SignedInClient(services => services.UseRecordedEdgar(new RecordedEdgar
        {
            CompanyFactsStatus = HttpStatusCode.Forbidden,
            Body = RecordedEdgar.ProviderText,
        }));

        var response = await client.GetAsync("/api/tickers/AAPL");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNothingLeaked(response);
    }

    [Fact]
    public async Task When_only_the_quote_fails_the_lookup_is_a_200_with_a_null_price_that_names_what_is_missing()
    {
        var client = await SignedInClient(services => services.UseRecordedEdgar().UseQuote(new FailingQuote()));

        var response = await client.GetAsync("/api/tickers/AAPL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("baseFacts").GetProperty("currentPrice").ValueKind);
        Assert.Equal(["currentPrice"], body.GetProperty("missing").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(466_823_000_000, body.GetProperty("baseFacts").GetProperty("ttmRevenue").GetDouble());
        // Everything that does not need the price is still seeded; the multiple keeps its kind and loses its value.
        Assert.All(Scenarios(body), scenario =>
        {
            Assert.Equal(-0.028795373563374027, scenario.GetProperty("dilutionRate").GetDouble(), Precision);
            Assert.Equal("pe", scenario.GetProperty("exitMultiple").GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, scenario.GetProperty("exitMultiple").GetProperty("value").ValueKind);
        });
        Assert.Equal(3, Scenarios(body).Count);
        await AssertNothingLeaked(response);
    }

    [Fact]
    public async Task The_Finnhub_adapter_sends_the_configured_key_and_reads_the_current_price()
    {
        var finnhub = new FakeFinnhub { Body = """{"c":213.55,"d":1.2,"dp":0.56,"h":215,"l":211,"o":212,"pc":212.35,"t":1790000000}""" };
        var client = await SignedInClient(services => services.UseRecordedEdgar().UseFinnhub(finnhub), finnhubKey: "the-configured-key");

        var body = await (await client.GetAsync("/api/tickers/AAPL")).Body();

        Assert.Equal(213.55, body.GetProperty("baseFacts").GetProperty("currentPrice").GetDouble());
        var request = Assert.Single(finnhub.Requests);
        Assert.Equal("https://finnhub.io/api/v1/quote?symbol=AAPL", request.Url);
        Assert.Equal("the-configured-key", request.Token);
    }

    /// <summary>
    /// A refusal, and the two 200s that are not a price: Finnhub answers a symbol it does not know
    /// with zeroes, and a body that is not JSON is somebody's error page.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"Invalid API key."}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":"API limit reached. Please try again later."}""")]
    [InlineData(HttpStatusCode.OK, """{"c":0,"d":null,"dp":null,"h":0,"l":0,"o":0,"pc":0,"t":0}""")]
    [InlineData(HttpStatusCode.OK, "<html>Bad gateway</html>")]
    public async Task A_Finnhub_failure_is_the_partial_200_never_a_503(HttpStatusCode status, string finnhubBody)
    {
        var finnhub = new FakeFinnhub { Status = status, Body = finnhubBody };
        var client = await SignedInClient(services => services.UseRecordedEdgar().UseFinnhub(finnhub), finnhubKey: "the-configured-key");

        var response = await client.GetAsync("/api/tickers/AAPL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Body();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("baseFacts").GetProperty("currentPrice").ValueKind);
        Assert.Equal(["currentPrice"], body.GetProperty("missing").EnumerateArray().Select(m => m.GetString()));
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("API key", text);
        Assert.DoesNotContain("API limit", text);
        Assert.DoesNotContain("the-configured-key", text);
        await AssertNothingLeaked(response);
    }

    /// <summary>The SEC writes a share class BRK-B; Finnhub, by its documentation, BRK.B.</summary>
    [Fact]
    public async Task The_Finnhub_adapter_asks_for_a_share_class_the_way_Finnhub_writes_it()
    {
        var finnhub = new FakeFinnhub { Body = """{"c":480.1}""" };
        var client = await SignedInClient(
            services => services.UseFundamentals(new FixedFundamentals(AnyCompany, ticker: "BRK-B")).UseFinnhub(finnhub),
            finnhubKey: "the-configured-key");

        await client.GetAsync("/api/tickers/brk.b");

        Assert.Equal("https://finnhub.io/api/v1/quote?symbol=BRK.B", Assert.Single(finnhub.Requests).Url);
    }

    [Fact]
    public async Task A_second_lookup_inside_the_ttl_calls_neither_port_and_each_expires_on_its_own()
    {
        var clock = new ManualClock();
        var fundamentals = new CountingFundamentals(new FixedFundamentals(AnyCompany));
        var quote = new CountingQuote();
        var client = await SignedInClient(services => services.UseFundamentals(fundamentals).UseQuote(quote).UseClock(clock));

        await client.GetAsync("/api/tickers/AAPL");
        // The same ticker however it is cased.
        var second = await client.GetAsync("/api/tickers/aapl");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((1, 1), (fundamentals.Calls, quote.Calls));

        clock.Advance(TimeSpan.FromMinutes(14));
        await client.GetAsync("/api/tickers/AAPL");
        Assert.Equal((1, 1), (fundamentals.Calls, quote.Calls));

        // Past the quote's 15 minutes, well inside the fundamentals' 24 hours.
        clock.Advance(TimeSpan.FromMinutes(2));
        await client.GetAsync("/api/tickers/AAPL");
        Assert.Equal((1, 2), (fundamentals.Calls, quote.Calls));

        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(43));
        await client.GetAsync("/api/tickers/AAPL");
        Assert.Equal(1, fundamentals.Calls);

        clock.Advance(TimeSpan.FromMinutes(2));
        await client.GetAsync("/api/tickers/AAPL");
        Assert.Equal(2, fundamentals.Calls);
    }

    /// <summary>The SEC's BRK-B is as often typed BRK.B, and is one company held once.</summary>
    [Fact]
    public async Task A_share_class_typed_either_way_is_the_same_held_lookup()
    {
        var fundamentals = new CountingFundamentals(new FixedFundamentals(AnyCompany, ticker: "BRK-B"));
        var quote = new CountingQuote();
        var client = await SignedInClient(services => services.UseFundamentals(fundamentals).UseQuote(quote));

        await client.GetAsync("/api/tickers/BRK.B");
        await client.GetAsync("/api/tickers/brk-b");

        Assert.Equal((1, 1), (fundamentals.Calls, quote.Calls));
    }

    [Fact]
    public async Task A_failed_quote_is_not_held_so_the_next_lookup_asks_again()
    {
        var quote = new CountingQuote { FailFirst = true };
        var client = await SignedInClient(services => services.UseFundamentals(new FixedFundamentals(AnyCompany)).UseQuote(quote));

        var first = await (await client.GetAsync("/api/tickers/AAPL")).Body();
        var second = await (await client.GetAsync("/api/tickers/AAPL")).Body();

        Assert.Equal(JsonValueKind.Null, first.GetProperty("baseFacts").GetProperty("currentPrice").ValueKind);
        Assert.Equal(CountingQuote.Price, second.GetProperty("baseFacts").GetProperty("currentPrice").GetDouble());
    }

    [Fact]
    public async Task The_ticker_file_is_fetched_once_for_lookups_of_different_tickers()
    {
        var edgar = new RecordedEdgar();
        var client = await SignedInClient(services => services.UseRecordedEdgar(edgar));

        await client.GetAsync("/api/tickers/AAPL");
        await client.GetAsync("/api/tickers/CRWV");
        await client.GetAsync("/api/tickers/NOSUCHTICKER");

        Assert.Single(edgar.Requests, url => url.EndsWith("company_tickers.json"));
        Assert.Equal(3, edgar.Requests.Count);
    }

    private static List<JsonElement> Scenarios(JsonElement body) => [.. body.GetProperty("scenarios").EnumerateArray()];

    /// <summary>No provider text, and nothing that reads as an exception or a stack trace.</summary>
    private static async Task AssertNothingLeaked(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(RecordedEdgar.ProviderText, text);
        Assert.DoesNotContain("Exception", text);
        Assert.DoesNotContain("   at ", text);
        Assert.DoesNotContain("sec.gov", text);
        Assert.DoesNotContain("finnhub", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="finnhubKey">Given, the host resolves the quote port to the real Finnhub adapter, as a deployed task does.</param>
    private async Task<HttpClient> SignedInClient(Action<IServiceCollection> ports, string? finnhubKey = null)
    {
        var client = factory
            .WithWebHostBuilder(builder =>
            {
                if (finnhubKey is not null) builder.UseSetting(FinnhubOptions.ApiKeySetting, finnhubKey);
                builder.ConfigureTestServices(ports);
            })
            .CreateBrowserLikeClient();
        await client.RegisterNewUser();
        return client;
    }
}

/// <summary>How a test fills the two ports, in place of the stubs the test host boots with.</summary>
public static class TestPorts
{
    /// <summary>The real EDGAR adapter, with the network answered from the recorded documents.</summary>
    public static IServiceCollection UseRecordedEdgar(this IServiceCollection services, RecordedEdgar? edgar = null)
    {
        edgar ??= new RecordedEdgar();
        services.AddHttpClient<EdgarClient>().ConfigurePrimaryHttpMessageHandler(() => edgar);
        return services.AddTransient<IFundamentalsSource, EdgarFundamentalsSource>();
    }

    /// <summary>The real Finnhub adapter, which the host registers once it has a key, with the network made up.</summary>
    public static IServiceCollection UseFinnhub(this IServiceCollection services, FakeFinnhub finnhub)
    {
        services.AddHttpClient(FinnhubQuoteSource.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => finnhub);
        return services;
    }

    public static IServiceCollection UseFundamentals(this IServiceCollection services, IFundamentalsSource source) =>
        services.AddSingleton(source);

    public static IServiceCollection UseClock(this IServiceCollection services, TimeProvider clock) =>
        services.AddSingleton(clock);

    public static IServiceCollection UseQuote(this IServiceCollection services, IQuoteSource source) =>
        services.AddSingleton(source);
}

/// <summary>SEC EDGAR as recorded: the ticker file, and the <c>companyfacts</c> of every filer with a fixture.</summary>
public sealed class RecordedEdgar : HttpMessageHandler
{
    public const string ProviderText = "Request Rate Threshold Exceeded";

    private static readonly string[] Recorded = ["AAPL", "CRWV", "MRP", "TSM"];

    private readonly Dictionary<string, string> _tickerByCik = Recorded.ToDictionary(
        ticker => $"CIK{EdgarFixtures.TickerMap().Resolve(ticker)!.Cik:D10}",
        ticker => ticker);

    public HttpStatusCode CompanyFactsStatus { get; init; } = HttpStatusCode.OK;

    /// <summary>What a <c>companyfacts</c> request is answered with, in place of the recorded document.</summary>
    public string? Body { get; init; }

    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        if (url.EndsWith("company_tickers.json"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EdgarFixtures.Json("company_tickers")),
            });
        }

        var cik = Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath);
        return Task.FromResult(new HttpResponseMessage(CompanyFactsStatus)
        {
            Content = new StringContent(Body ?? EdgarFixtures.Json(_tickerByCik[cik])),
        });
    }
}

public sealed class FixedQuote(double price) : IQuoteSource
{
    public Task<double> GetPriceAsync(string ticker, CancellationToken cancellationToken) => Task.FromResult(price);
}

public sealed class FailingQuote : IQuoteSource
{
    public Task<double> GetPriceAsync(string ticker, CancellationToken cancellationToken) =>
        throw new HttpRequestException("finnhub.io said: Invalid API key.");
}

/// <summary>Any symbol is this one company, under the ticker given or else the symbol itself.</summary>
public sealed class FixedFundamentals(Fundamentals fundamentals, string? ticker = null) : IFundamentalsSource
{
    public Task<FundamentalsLookup> LookUpAsync(string symbol, CancellationToken cancellationToken) =>
        Task.FromResult<FundamentalsLookup>(new FundamentalsLookup.Found(
            new EdgarCompany(1, ticker ?? symbol.ToUpperInvariant(), "Made Up Inc."), fundamentals));
}

public sealed class FakeFinnhub : HttpMessageHandler
{
    public List<(string Url, string? Token)> Requests { get; } = [];

    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

    public required string Body { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((
            request.RequestUri!.ToString(),
            request.Headers.TryGetValues("X-Finnhub-Token", out var token) ? string.Join(",", token) : null));
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
    }
}

public sealed class CountingFundamentals(IFundamentalsSource inner) : IFundamentalsSource
{
    public int Calls { get; private set; }

    public Task<FundamentalsLookup> LookUpAsync(string symbol, CancellationToken cancellationToken)
    {
        Calls++;
        return inner.LookUpAsync(symbol, cancellationToken);
    }
}

public sealed class CountingQuote : IQuoteSource
{
    public const double Price = 42;

    public int Calls { get; private set; }

    public bool FailFirst { get; init; }

    public Task<double> GetPriceAsync(string ticker, CancellationToken cancellationToken) =>
        ++Calls == 1 && FailFirst ? throw new HttpRequestException("down") : Task.FromResult(Price);
}

/// <summary>A clock that moves only when a test moves it.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
