using System.Net;
using Compass.Api.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Api.Tests;

/// <summary>
/// The EDGAR HTTP client as the app registers it (ticket #72), with the network replaced by a
/// handler that records the request and answers from the recorded fixtures. EDGAR refuses a
/// request whose User-Agent carries no contact address (ADR 0010), so that header is the point.
/// </summary>
public class EdgarClientTests
{
    private const string Contact = "operator@example.com";

    [Fact]
    public async Task Every_request_carries_the_configured_contact_address_as_its_user_agent()
    {
        var edgar = new RecordingEdgar();
        var client = ClientFor(edgar, Contact);

        await client.GetTickerMapAsync(CancellationToken.None);
        await client.GetCompanyFactsAsync(320193, CancellationToken.None);

        Assert.Equal(2, edgar.Requests.Count);
        Assert.All(edgar.Requests, request => Assert.Equal($"Compass {Contact}", request.UserAgent));
    }

    [Fact]
    public async Task The_ticker_file_and_a_companys_facts_are_fetched_from_where_the_SEC_serves_them()
    {
        var edgar = new RecordingEdgar();
        var client = ClientFor(edgar, Contact);

        var tickers = await client.GetTickerMapAsync(CancellationToken.None);
        var facts = await client.GetCompanyFactsAsync(320193, CancellationToken.None);

        Assert.Equal(
            ["https://www.sec.gov/files/company_tickers.json", "https://data.sec.gov/api/xbrl/companyfacts/CIK0000320193.json"],
            edgar.Requests.Select(r => r.Url));
        Assert.Equal(320193, tickers.Resolve("AAPL")?.Cik);
        Assert.Equal("Apple Inc.", facts?.EntityName);
    }

    [Fact]
    public async Task A_company_EDGAR_holds_no_facts_for_is_no_document_rather_than_an_error()
    {
        var client = ClientFor(new RecordingEdgar { CompanyFactsStatus = HttpStatusCode.NotFound }, Contact);

        Assert.Null(await client.GetCompanyFactsAsync(320193, CancellationToken.None));
    }

    [Fact]
    public async Task Any_other_failure_is_thrown_for_the_caller_to_report_as_an_outage()
    {
        var client = ClientFor(new RecordingEdgar { CompanyFactsStatus = HttpStatusCode.Forbidden }, Contact);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCompanyFactsAsync(320193, CancellationToken.None));
    }

    [Fact]
    public async Task A_document_that_cannot_be_read_is_an_outage_too()
    {
        var client = ClientFor(new RecordingEdgar { Body = "<html>Request Rate Threshold Exceeded</html>" }, Contact);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTickerMapAsync(CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCompanyFactsAsync(320193, CancellationToken.None));
    }

    /// <summary>Registers the client the way Program does, from the one configuration key, then swaps the network out.</summary>
    private static EdgarClient ClientFor(RecordingEdgar edgar, string contact)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [EdgarOptions.ContactEmailSetting] = contact })
            .Build();
        var services = new ServiceCollection();
        services.Configure<EdgarOptions>(configuration.GetSection(EdgarOptions.SectionName));
        services.AddEdgarClient().ConfigurePrimaryHttpMessageHandler(() => edgar);

        return services.BuildServiceProvider().GetRequiredService<EdgarClient>();
    }

    private sealed class RecordingEdgar : HttpMessageHandler
    {
        public List<(string Url, string UserAgent)> Requests { get; } = [];

        public HttpStatusCode CompanyFactsStatus { get; init; } = HttpStatusCode.OK;

        /// <summary>What every request is answered with, in place of the recorded documents.</summary>
        public string? Body { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            // Read raw: the SEC's own format, "name contact@address", is not a product token, so the typed
            // accessor would not show it.
            Requests.Add((url, string.Join(" ", request.Headers.GetValues("User-Agent"))));

            var isTickerFile = url.EndsWith("company_tickers.json");
            var response = new HttpResponseMessage(isTickerFile ? HttpStatusCode.OK : CompanyFactsStatus)
            {
                Content = new StringContent(Body ?? EdgarFixtures.Json(isTickerFile ? "company_tickers" : "AAPL")),
            };
            return Task.FromResult(response);
        }
    }
}
