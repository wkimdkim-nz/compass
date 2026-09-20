using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compass.Api.MarketData;

/// <summary>A company as the SEC ticker file names it: the CIK EDGAR keys its filings by, the ticker, the registrant's name.</summary>
public sealed record EdgarCompany(long Cik, string Ticker, string Name);

/// <summary>
/// Ticker → CIK, from the SEC's <c>company_tickers.json</c> (ADR 0010). The file lists every
/// registrant with a ticker, foreign private issuers included, so a ticker that resolves is one
/// EDGAR knows, not one Compass can normalise: an unknown ticker is the lookup's 404, a known
/// filer <see cref="EdgarNormaliser"/> refuses is its 422.
/// </summary>
public sealed class EdgarTickerMap
{
    private readonly Dictionary<string, EdgarCompany> _byTicker;

    private EdgarTickerMap(Dictionary<string, EdgarCompany> byTicker) => _byTicker = byTicker;

    /// <summary>The file is one object keyed by row number: <c>{"0":{"cik_str":320193,"ticker":"AAPL","title":"Apple Inc."},…}</c>.</summary>
    public static EdgarTickerMap Parse(string json)
    {
        var rows = JsonSerializer.Deserialize<Dictionary<string, Row?>>(json, JsonSerializerOptions.Web) ?? [];
        var byTicker = new Dictionary<string, EdgarCompany>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Values)
        {
            if (row is { Ticker: { Length: > 0 } ticker })
            {
                // The file is ordered by market value, so on a repeated ticker the first row is the one meant.
                byTicker.TryAdd(ticker, new EdgarCompany(row.Cik, ticker.ToUpperInvariant(), row.Title ?? ticker));
            }
        }
        return new EdgarTickerMap(byTicker);
    }

    /// <summary>The company behind a ticker as a user types it, or null when the SEC lists no such ticker.</summary>
    public EdgarCompany? Resolve(string ticker) =>
        // The SEC writes a share class as BRK-B; it is as often typed BRK.B.
        _byTicker.GetValueOrDefault(ticker.Trim().Replace('.', '-'));

    private sealed record Row([property: JsonPropertyName("cik_str")] long Cik, string? Ticker, string? Title);
}
