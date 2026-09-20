namespace Compass.Api.MarketData;

/// <summary>
/// The fundamentals source over SEC EDGAR (ADR 0010): the ticker resolved through the SEC's ticker
/// file, the company's <c>companyfacts</c> fetched and handed to <see cref="EdgarNormaliser"/>. The
/// ticker file is held for its own 24 hours; what is said of a company is held by the lookup. A
/// company EDGAR holds no facts for is one the normaliser refuses, not an outage.
/// </summary>
public sealed class EdgarFundamentalsSource(EdgarClient edgar, MarketDataCache cache) : IFundamentalsSource
{
    public async Task<FundamentalsLookup> LookUpAsync(string symbol, CancellationToken cancellationToken)
    {
        var tickers = await cache.GetOrFetchAsync(
            "edgar:ticker-map",
            MarketDataCache.TickerMapTtl,
            () => edgar.GetTickerMapAsync(cancellationToken));

        if (tickers.Resolve(symbol) is not { } company) return new FundamentalsLookup.UnknownTicker();

        return EdgarNormaliser.Normalise(await edgar.GetCompanyFactsAsync(company.Cik, cancellationToken)) switch
        {
            FundamentalsResult.Normalised normalised => new FundamentalsLookup.Found(company, normalised.Fundamentals),
            FundamentalsResult.Unsupported unsupported => new FundamentalsLookup.Unsupported(unsupported.Reason),
            var other => throw new InvalidOperationException($"Unhandled normaliser result {other.GetType().Name}."),
        };
    }
}
