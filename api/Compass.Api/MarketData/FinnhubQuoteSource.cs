using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Compass.Api.MarketData;

/// <summary>
/// The quote source over Finnhub's free <c>/quote</c> (ADR 0010). Every failure is thrown as
/// <see cref="HttpRequestException"/>, a 200 that holds no price included, for the lookup to
/// answer without one; Finnhub's body is never passed upward.
/// </summary>
/// <remarks>
/// Written from Finnhub's documentation, not from observed responses: no call without an account
/// returned data (<c>docs/research/edgar-finnhub-alternatives.md</c>). The deployed smoke test is
/// what proves it against the real thing.
/// </remarks>
public sealed class FinnhubQuoteSource(HttpClient http) : IQuoteSource
{
    /// <summary>The named client behind this adapter: where a test puts a network of its own.</summary>
    public const string HttpClientName = "Finnhub";

    public async Task<double> GetPriceAsync(string ticker, CancellationToken cancellationToken)
    {
        // The SEC writes a share class BRK-B, Finnhub BRK.B.
        var symbol = Uri.EscapeDataString(ticker.Replace('-', '.'));
        using var response = await http.GetAsync($"quote?symbol={symbol}", cancellationToken);
        response.EnsureSuccessStatusCode();

        Quote? quote;
        try
        {
            quote = await response.Content.ReadFromJsonAsync<Quote>(cancellationToken);
        }
        catch (System.Text.Json.JsonException unreadable)
        {
            throw new HttpRequestException("Finnhub answered with a document that could not be read.", unreadable);
        }

        // A symbol Finnhub does not know is a 200 of zeroes.
        return quote is { C: > 0 and var price } && double.IsFinite(price)
            ? price
            : throw new HttpRequestException("Finnhub answered with no current price.");
    }

    /// <summary><c>c</c> is the current price; the day's range, the change and the timestamp are not read.</summary>
    private sealed record Quote(double? C);
}

public static class FinnhubQuoteSourceRegistration
{
    /// <summary>
    /// Registers <see cref="FinnhubQuoteSource"/> as the quote source, with the key from
    /// <see cref="FinnhubOptions.ApiKey"/>. It travels in a header, which Finnhub documents beside
    /// the <c>token</c> query parameter, so that it is in no URL and so in no log of one.
    /// </summary>
    public static IHttpClientBuilder AddFinnhubQuoteSource(this IServiceCollection services) =>
        services.AddHttpClient<IQuoteSource, FinnhubQuoteSource>(FinnhubQuoteSource.HttpClientName, (provider, http) =>
        {
            http.BaseAddress = new Uri("https://finnhub.io/api/v1/");
            http.DefaultRequestHeaders.Add("X-Finnhub-Token", provider.GetRequiredService<IOptions<FinnhubOptions>>().Value.ApiKey);
        });
}
