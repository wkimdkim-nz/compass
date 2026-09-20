namespace Compass.Api.MarketData;

/// <summary>
/// The port the ticker lookup gets a company and its fundamentals from (ADR 0010). Today SEC
/// EDGAR, <see cref="EdgarFundamentalsSource"/>; <see cref="StubFundamentalsSource"/> where the
/// contact address is not configured. An outage is thrown, and is the lookup's 503.
/// </summary>
public interface IFundamentalsSource
{
    Task<FundamentalsLookup> LookUpAsync(string symbol, CancellationToken cancellationToken);
}

/// <summary>The three things a fundamentals source can say of a ticker without having failed.</summary>
public abstract record FundamentalsLookup
{
    private FundamentalsLookup() { }

    public sealed record Found(EdgarCompany Company, Fundamentals Fundamentals) : FundamentalsLookup;

    /// <summary>No such ticker: the lookup's 404.</summary>
    public sealed record UnknownTicker : FundamentalsLookup;

    /// <summary>A company the source knows and cannot normalise, with the plain reason the lookup's 422 carries.</summary>
    public sealed record Unsupported(string Reason) : FundamentalsLookup;
}
