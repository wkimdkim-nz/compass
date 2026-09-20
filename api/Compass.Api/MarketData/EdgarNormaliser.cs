namespace Compass.Api.MarketData;

/// <summary>
/// What the fundamentals source knows of a company: the base facts that do not need a quote, the
/// date they run to, and the annual history the scenarios are seeded from (ADR 0004, amended).
/// Raw values, not millions; the margin is a decimal.
/// </summary>
/// <param name="CurrentNetMargin">TTM net income ÷ TTM revenue. Null when TTM revenue is not positive, where the ratio says nothing.</param>
/// <param name="DilutedShares">The diluted weighted average for the latest period filed: an average over that period, not a point-in-time count.</param>
/// <param name="FiguresTo">The period end the TTM figures run to.</param>
/// <param name="History">Oldest first, at most five fiscal years, each with all three figures. May hold fewer than two, which is too few to seed from.</param>
public sealed record Fundamentals(
    double TtmRevenue,
    double TtmNetIncome,
    double? CurrentNetMargin,
    double DilutedShares,
    DateOnly FiguresTo,
    IReadOnlyList<AnnualFigures> History);

public sealed record AnnualFigures(DateOnly FiscalYearEnd, double Revenue, double NetIncome, double DilutedShares);

/// <summary>Why a filer EDGAR knows could not be normalised. Each has its own plain reason.</summary>
public enum UnsupportedFiler
{
    NotUsDomesticFiler,
    NoUsableRevenue,
    TooFewPeriods,
    NoNetIncome,
    NoDilutedShares,
}

/// <summary>Either the fundamentals or the plain reason there are none, which the ticker lookup's 422 carries to the user.</summary>
public abstract record FundamentalsResult
{
    private FundamentalsResult() { }

    public sealed record Normalised(Fundamentals Fundamentals) : FundamentalsResult;

    public sealed record Unsupported(UnsupportedFiler Kind, string Reason) : FundamentalsResult;
}

/// <summary>
/// Turns a <c>companyfacts</c> document into <see cref="Fundamentals"/> (ADR 0010). EDGAR serves
/// raw filed XBRL facts and normalises nothing, so the choices are made here:
/// <list type="bullet">
/// <item>Only facts from <b>10-Ks and 10-Qs</b> (and their <c>/A</c> amendments) are read, and only
/// <c>us-gaap</c> ones. A filer with none is not a US domestic filer.</item>
/// <item>The same period is filed many times over, as a comparative in later filings and again
/// when restated. For each <c>(start, end)</c> the <b>latest-filed</b> fact is the one taken, so
/// nothing is counted twice and a restatement wins.</item>
/// <item><b>Revenue</b> has no single tag: a filer moves between them over the years, and may use
/// one in its 10-K and another in its 10-Qs. Each period takes the first tag in
/// <see cref="RevenueTags"/> that reports it.</item>
/// <item><b>TTM</b> is not a filed concept, and a 10-K carries no fourth quarter. It is the latest
/// fiscal year, plus the year to date since, less the same year to date a year earlier: the four
/// latest quarters with Q4 = FY − nine-month YTD, written without needing each quarter singly.</item>
/// </list>
/// Pure: no I/O, and it answers a document it cannot use with a reason rather than an exception.
/// </summary>
/// <remarks>
/// Not handled: a share split older than the comparatives of the latest 10-K leaves the earliest
/// years of <see cref="Fundamentals.History"/> on the pre-split count, because nothing later
/// restates them.
/// </remarks>
public static class EdgarNormaliser
{
    /// <summary>
    /// In order of preference. <c>Revenues</c> leads because where a filer reports it beside a
    /// contract-revenue tag it is the total and the other a part (Tesla's leasing income is
    /// outside its contract revenue).
    /// </summary>
    private static readonly string[] RevenueTags =
    [
        "Revenues",
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "RevenueFromContractWithCustomerIncludingAssessedTax",
        "SalesRevenueNet",
        "RevenuesNetOfInterestExpense",
        "SalesRevenueGoodsNet",
    ];

    private const string NetIncomeTag = "NetIncomeLoss";
    private const string DilutedSharesTag = "WeightedAverageNumberOfDilutedSharesOutstanding";

    private static readonly HashSet<string> Forms = ["10-K", "10-K/A", "10-Q", "10-Q/A"];

    private const int MaxHistoryYears = 5;

    /// <summary>A fiscal year is 52 or 53 weeks or a calendar year, and a year to date lines up with last year's to within a week or so.</summary>
    private const int MinYearDays = 350, MaxYearDays = 380, CalendarDriftDays = 10;

    private readonly record struct Period(DateOnly Start, DateOnly End)
    {
        public int Days => End.DayNumber - Start.DayNumber + 1;

        public bool IsFiscalYear => Days is >= MinYearDays and <= MaxYearDays;
    }

    public static FundamentalsResult Normalise(CompanyFacts? document)
    {
        var usGaap = document?.Facts?.GetValueOrDefault("us-gaap");
        var revenueByTag = RevenueTags.Select(tag => Series(usGaap, tag, "USD")).ToList();
        var netIncome = Series(usGaap, NetIncomeTag, "USD");
        var dilutedShares = Series(usGaap, DilutedSharesTag, "shares");

        if (netIncome.Count == 0 && dilutedShares.Count == 0 && revenueByTag.All(series => series.Count == 0))
        {
            return Unsupported(UnsupportedFiler.NotUsDomesticFiler);
        }

        double? Revenue(Period period) =>
            revenueByTag.Where(series => series.ContainsKey(period)).Select(series => (double?)series[period]).FirstOrDefault();

        var revenuePeriods = revenueByTag.SelectMany(series => series.Keys).ToHashSet();
        if (revenuePeriods.Count == 0) return Unsupported(UnsupportedFiler.NoUsableRevenue);

        // Ordered, so that which of two year-long periods ending on one day is taken does not vary by run.
        var fiscalYears = revenuePeriods.Where(p => p.IsFiscalYear).OrderBy(p => p.End).ThenBy(p => p.Start).ToList();
        if (fiscalYears.Count == 0) return Unsupported(UnsupportedFiler.TooFewPeriods);

        var latestYear = fiscalYears.MaxBy(p => p.End);
        var figuresTo = revenuePeriods.Max(p => p.End);

        // What is added to the latest fiscal year and what is taken from it. Nothing, when the
        // latest filing is the 10-K itself.
        (Period Period, int Sign)[] terms = [(latestYear, +1)];
        if (figuresTo != latestYear.End)
        {
            var yearToDate = new Period(latestYear.End.AddDays(1), figuresTo);
            var sameSpanLastYear = revenuePeriods
                .Where(p => p.Start == latestYear.Start && Math.Abs(p.Days - yearToDate.Days) <= CalendarDriftDays)
                .ToList();
            // A year to date that does not start where the fiscal year ended means a 10-K is
            // missing from companyfacts, which has been seen; there is no TTM to be had then either.
            if (!revenuePeriods.Contains(yearToDate) || sameSpanLastYear.Count == 0)
            {
                return Unsupported(UnsupportedFiler.TooFewPeriods);
            }
            var lastYearToDate = sameSpanLastYear.MinBy(p => Math.Abs(p.Days - yearToDate.Days));
            terms = [(latestYear, +1), (yearToDate, +1), (lastYearToDate, -1)];
        }

        double? Ttm(Func<Period, double?> figure)
        {
            double total = 0;
            foreach (var (period, sign) in terms)
            {
                if (figure(period) is not { } value) return null;
                total += sign * value;
            }
            return total;
        }

        var ttmRevenue = Ttm(Revenue)!.Value;
        if (Ttm(p => Lookup(netIncome, p)) is not { } ttmNetIncome) return Unsupported(UnsupportedFiler.NoNetIncome);

        // A 10-Q reports the quarter and the year to date, both ending on the same day; the
        // shortest period is the most current average.
        var latestSharePeriods = dilutedShares.Keys.Where(p => p.End == figuresTo).ToList();
        if (latestSharePeriods.Count == 0) return Unsupported(UnsupportedFiler.NoDilutedShares);

        return new FundamentalsResult.Normalised(new Fundamentals(
            ttmRevenue,
            ttmNetIncome,
            ttmRevenue > 0 ? ttmNetIncome / ttmRevenue : null,
            dilutedShares[latestSharePeriods.MinBy(p => p.Days)],
            figuresTo,
            History(latestYear, fiscalYears, Revenue, netIncome, dilutedShares)));
    }

    /// <summary>
    /// Walks back from the latest fiscal year, each year ending where the next began, and stops at
    /// the first year missing any of the three figures: the growth and dilution defaults are CAGRs
    /// over the same span, so a year is whole or it is not in the history.
    /// </summary>
    private static List<AnnualFigures> History(
        Period latestYear,
        List<Period> fiscalYears,
        Func<Period, double?> revenue,
        Dictionary<Period, double> netIncome,
        Dictionary<Period, double> dilutedShares)
    {
        var history = new List<AnnualFigures>();
        Period? year = latestYear;
        while (year is { } y && history.Count < MaxHistoryYears
            && revenue(y) is { } yearRevenue
            && Lookup(netIncome, y) is { } yearNetIncome
            && Lookup(dilutedShares, y) is { } yearShares)
        {
            history.Insert(0, new AnnualFigures(y.End, yearRevenue, yearNetIncome, yearShares));

            var previousEnd = y.Start.AddDays(-1);
            year = fiscalYears
                .Where(p => Math.Abs(p.End.DayNumber - previousEnd.DayNumber) <= CalendarDriftDays)
                .Select(p => (Period?)p)
                .MinBy(p => Math.Abs(p!.Value.End.DayNumber - previousEnd.DayNumber));
        }
        return history;
    }

    /// <summary>One concept's 10-K and 10-Q facts in one unit, one figure per period: the latest filed.</summary>
    private static Dictionary<Period, double> Series(Dictionary<string, CompanyConcept?>? taxonomy, string tag, string unit)
    {
        var facts = taxonomy?.GetValueOrDefault(tag)?.Units?.GetValueOrDefault(unit) ?? [];
        return facts
            .Where(f => f is { Start: not null, Form: not null } && Forms.Contains(f.Form))
            .GroupBy(f => new Period(f!.Start!.Value, f.End))
            // A stable sort: of two facts filed the same day, the one EDGAR lists later is taken.
            .ToDictionary(period => period.Key, period => period.OrderBy(f => f!.Filed).Last()!.Val);
    }

    private static double? Lookup(Dictionary<Period, double> series, Period period) =>
        series.TryGetValue(period, out var value) ? value : null;

    private static FundamentalsResult.Unsupported Unsupported(UnsupportedFiler kind) => new(kind, kind switch
    {
        UnsupportedFiler.NotUsDomesticFiler =>
            "This company is not a US domestic filer: it files no 10-K or 10-Q reports with the SEC, " +
            "so its figures cannot be looked up. You can enter them yourself.",
        UnsupportedFiler.NoUsableRevenue =>
            "This company's SEC filings report no revenue that can be read, so its figures cannot be looked up. " +
            "You can enter them yourself.",
        UnsupportedFiler.TooFewPeriods =>
            "The SEC holds too few of this company's annual and quarterly reports to add up its last twelve months. " +
            "You can enter its figures yourself.",
        UnsupportedFiler.NoNetIncome =>
            "This company's SEC filings report no net income for its last twelve months, so its margin cannot be worked out. " +
            "You can enter its figures yourself.",
        UnsupportedFiler.NoDilutedShares =>
            "This company's SEC filings report no diluted share count for its latest period. " +
            "You can enter its figures yourself.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    });
}
