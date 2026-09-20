namespace Compass.Api.MarketData;

/// <summary>
/// The fundamentals source of a process with no <see cref="EdgarOptions.ContactEmail"/> (ADR 0010):
/// Development and the tests, never a deployed task. Four made-up companies, one for each answer
/// the lookup has, so the whole of the UI can be worked on with no network. Any other ticker is unknown.
/// </summary>
public sealed class StubFundamentalsSource : IFundamentalsSource
{
    public const string Profitable = "AAPL", Unprofitable = "LCID", OneYearOfHistory = "MRP", NotUsDomestic = "TSM";

    public Task<FundamentalsLookup> LookUpAsync(string symbol, CancellationToken cancellationToken) =>
        Task.FromResult<FundamentalsLookup>(symbol.Trim().ToUpperInvariant() switch
        {
            Profitable => Found(1, Profitable, "Stub Profitable Inc.", ttmNetIncome: 25_000_000_000,
                Year(2022, 80_000_000_000, 18_000_000_000, 5_200_000_000),
                Year(2023, 88_000_000_000, 20_000_000_000, 5_100_000_000),
                Year(2024, 94_000_000_000, 22_000_000_000, 5_050_000_000),
                Year(2025, 100_000_000_000, 24_000_000_000, 5_000_000_000)),
            Unprofitable => Found(2, Unprofitable, "Stub Unprofitable Inc.", ttmNetIncome: -2_500_000_000,
                Year(2023, 600_000_000, -2_800_000_000, 200_000_000),
                Year(2024, 800_000_000, -2_700_000_000, 250_000_000),
                Year(2025, 1_300_000_000, -2_600_000_000, 310_000_000)),
            OneYearOfHistory => Found(3, OneYearOfHistory, "Stub Newly Listed Inc.", ttmNetIncome: 400_000_000,
                Year(2025, 600_000_000, 380_000_000, 166_000_000)),
            // The normaliser's own words for a filer with no 10-K or 10-Q facts, which no document is.
            NotUsDomestic => new FundamentalsLookup.Unsupported(
                ((FundamentalsResult.Unsupported)EdgarNormaliser.Normalise(null)).Reason),
            _ => new FundamentalsLookup.UnknownTicker(),
        });

    private static AnnualFigures Year(int year, double revenue, double netIncome, double dilutedShares) =>
        new(new DateOnly(year, 12, 31), revenue, netIncome, dilutedShares);

    /// <summary>The latest year's revenue and shares stand as the TTM figures; the net income is given, to set the margin's sign.</summary>
    private static FundamentalsLookup.Found Found(long cik, string ticker, string name, double ttmNetIncome, params AnnualFigures[] history)
    {
        var latest = history[^1];
        return new(
            new EdgarCompany(cik, ticker, name),
            new Fundamentals(latest.Revenue, ttmNetIncome, ttmNetIncome / latest.Revenue, latest.DilutedShares, latest.FiscalYearEnd, history));
    }
}

/// <summary>The quote source of a process with no <see cref="FinnhubOptions.ApiKey"/> (ADR 0010): one price for every ticker.</summary>
public sealed class StubQuoteSource : IQuoteSource
{
    public const double Price = 100;

    public Task<double> GetPriceAsync(string ticker, CancellationToken cancellationToken) => Task.FromResult(Price);
}
