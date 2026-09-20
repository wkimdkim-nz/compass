using System.Net;
using Microsoft.Extensions.Options;

namespace Compass.Api.MarketData;

/// <summary>
/// The two SEC EDGAR documents the fundamentals source reads (ADR 0010), fetched and parsed and
/// nothing more: no cache and no normalisation, which belong to the lookup and to
/// <see cref="EdgarNormaliser"/>. A failure is thrown as <see cref="HttpRequestException"/> for
/// the caller to report as an outage; EDGAR's body is never passed upward.
/// </summary>
public sealed class EdgarClient(HttpClient http)
{
    // Two hosts, so no base address: the ticker file is on www, the XBRL API on data.
    private const string TickerFileUrl = "https://www.sec.gov/files/company_tickers.json";

    private static string CompanyFactsUrl(long cik) => $"https://data.sec.gov/api/xbrl/companyfacts/CIK{cik:D10}.json";

    public async Task<EdgarTickerMap> GetTickerMapAsync(CancellationToken cancellationToken) =>
        EdgarTickerMap.Parse(await http.GetStringAsync(TickerFileUrl, cancellationToken));

    /// <summary>Null when EDGAR holds no XBRL facts for the company, which it answers with a 404.</summary>
    public async Task<CompanyFacts?> GetCompanyFactsAsync(long cik, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(CompanyFactsUrl(cik), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return CompanyFacts.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}

public static class EdgarClientRegistration
{
    /// <summary>
    /// Registers <see cref="EdgarClient"/> with the User-Agent EDGAR insists on: it refuses any
    /// request that does not declare a contact address, and that address is
    /// <see cref="EdgarOptions.ContactEmail"/>, from configuration and never from source.
    /// </summary>
    public static IHttpClientBuilder AddEdgarClient(this IServiceCollection services) =>
        services
            .AddHttpClient<EdgarClient>((provider, http) =>
            {
                var contact = provider.GetRequiredService<IOptions<EdgarOptions>>().Value.ContactEmail;
                // The SEC's documented format is "name contact@address". An address is not a valid
                // product token, so the header is set without the validation that would reject it.
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Compass {contact}");
            })
            // A companyfacts document is several megabytes, and a fifteenth of that gzipped.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
}
