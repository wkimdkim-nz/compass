namespace Compass.Api.MarketData;

/// <summary>
/// The port the ticker lookup gets the current price from (ADR 0010). Today Finnhub,
/// <see cref="FinnhubQuoteSource"/>; <see cref="StubQuoteSource"/> where the key is not configured.
/// A failure is thrown, and costs the lookup the price and nothing else.
/// </summary>
public interface IQuoteSource
{
    /// <param name="ticker">As the SEC writes it, upper-cased and with a share class as <c>BRK-B</c>.</param>
    Task<double> GetPriceAsync(string ticker, CancellationToken cancellationToken);
}
