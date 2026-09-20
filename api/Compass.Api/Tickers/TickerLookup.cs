using Compass.Api.MarketData;

namespace Compass.Api.Tickers;

/// <param name="CurrentPrice">Null when the quote source failed; <see cref="TickerResponse.Missing"/> then names it.</param>
/// <param name="CurrentNetMargin">Null when TTM revenue is not positive.</param>
/// <param name="FiguresTo">The period end the TTM figures run to.</param>
public sealed record BaseFactsResponse(
    double? CurrentPrice,
    double TtmRevenue,
    double DilutedShares,
    double? CurrentNetMargin,
    DateOnly FiguresTo);

/// <summary>What the ticker lookup answers with. Raw values and decimals: millions and whole-number percents are the UI's.</summary>
/// <param name="Scenarios">Bear, Base, Bull; or none, when the history is too short to seed from.</param>
/// <param name="Missing">The fields that could not be fetched, by their names here. Empty when everything was.</param>
public sealed record TickerResponse(
    string Ticker,
    string CompanyName,
    long Cik,
    BaseFactsResponse BaseFacts,
    IReadOnlyList<AnnualFigures> History,
    IReadOnlyList<SeededScenario> Scenarios,
    IReadOnlyList<string> Missing);

public abstract record TickerLookupResult
{
    private TickerLookupResult() { }

    public sealed record Found(TickerResponse Response) : TickerLookupResult;

    public sealed record UnknownTicker : TickerLookupResult;

    public sealed record Unsupported(string Reason) : TickerLookupResult;

    public sealed record FundamentalsUnavailable : TickerLookupResult;
}

/// <summary>
/// Composes the two market-data ports into the ticker lookup (ADR 0010). The fundamentals decide
/// the answer; the quote adds one field, and its failure costs that field only. Both are held in
/// <see cref="MarketDataCache"/> on their own TTLs, so a lookup inside them calls neither port.
/// </summary>
public sealed class TickerLookup(
    IFundamentalsSource fundamentalsSource,
    IQuoteSource quoteSource,
    MarketDataCache cache,
    ILogger<TickerLookup> logger)
{
    private const string CurrentPriceField = "currentPrice";

    public async Task<TickerLookupResult> LookUpAsync(string symbol, CancellationToken cancellationToken)
    {
        FundamentalsLookup lookup;
        try
        {
            lookup = await cache.GetOrFetchAsync(
                // Keyed the way the SEC writes a ticker, so that BRK.B and brk-b are held once.
                $"fundamentals:{symbol.Trim().ToUpperInvariant().Replace('.', '-')}",
                MarketDataCache.FundamentalsTtl,
                () => fundamentalsSource.LookUpAsync(symbol, cancellationToken),
                // Not an unknown ticker: what a user can mistype has no bound, and the ticker map it
                // was checked against is already held by the source.
                keep: answer => answer is not FundamentalsLookup.UnknownTicker);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            // Logged here and nowhere else: the response says only that the source is unavailable.
            logger.LogError(failure, "The fundamentals source failed for {Symbol}.", symbol);
            return new TickerLookupResult.FundamentalsUnavailable();
        }

        switch (lookup)
        {
            case FundamentalsLookup.UnknownTicker:
                return new TickerLookupResult.UnknownTicker();
            case FundamentalsLookup.Unsupported unsupported:
                return new TickerLookupResult.Unsupported(unsupported.Reason);
        }

        var (company, fundamentals) = (FundamentalsLookup.Found)lookup;
        var price = await PriceAsync(company.Ticker, cancellationToken);

        return new TickerLookupResult.Found(new TickerResponse(
            company.Ticker,
            company.Name,
            company.Cik,
            new BaseFactsResponse(
                price,
                fundamentals.TtmRevenue,
                fundamentals.DilutedShares,
                fundamentals.CurrentNetMargin,
                fundamentals.FiguresTo),
            fundamentals.History,
            ScenarioSeeder.Seed(fundamentals, price),
            price is null ? [CurrentPriceField] : []));
    }

    private async Task<double?> PriceAsync(string ticker, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrFetchAsync(
                $"quote:{ticker}",
                MarketDataCache.QuoteTtl,
                () => quoteSource.GetPriceAsync(ticker, cancellationToken));
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(failure, "The quote source failed for {Ticker}; answering without a price.", ticker);
            return null;
        }
    }
}
