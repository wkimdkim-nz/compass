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
/// <c>us-gaap</c> ones. A transition report (10-KT, 10-QT), which a filer changing its fiscal year
/// end files for the short year between, counts as the 10-K or 10-Q it stands in for. A filer with
/// none of these is not a US domestic filer.</item>
/// <item>The same period is filed many times over, as a comparative in later filings and again
/// when restated. For each <c>(start, end)</c> the <b>latest-filed</b> fact is the one taken, so
/// nothing is counted twice and a restatement wins.</item>
/// <item><b>Revenue</b> has no single tag: a filer moves between them over the years, and may use
/// one in its 10-K and another in its 10-Qs. Each period takes the first tag in
/// <see cref="RevenueTags"/> that reports it.</item>
/// <item><b>TTM</b> is not a filed concept, and a 10-K carries no fourth quarter. It is the latest
/// fiscal year, plus the year to date since, less the same year to date a year earlier: the four
/// latest quarters with Q4 = FY − nine-month YTD, written without needing each quarter singly.</item>
/// <item><b>Share splits</b> are nowhere in <c>companyfacts</c>, and a 10-K restates only the two
/// years before its own, so after a split the older years of the history are still on the old
/// count. The splits are read out of the restatements that were made: see <see cref="Splits"/>.</item>
/// </list>
/// Pure: no I/O, and it answers a document it cannot use with a reason rather than an exception.
/// </summary>
/// <remarks>
/// Not handled: a split since the latest filing, which nothing filed shows yet, so every share
/// count is on the old basis while the quote is on the new one; a split no filing restated a
/// period for, as when a company's first filings come after it; and a split smaller than three
/// for two, or a stock dividend, which cannot be told from a corrected figure.
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

    private static readonly HashSet<string> Forms =
        ["10-K", "10-K/A", "10-Q", "10-Q/A", "10-KT", "10-KT/A", "10-QT", "10-QT/A"];

    private const int MaxHistoryYears = 5;

    /// <summary>A fiscal year is 52 or 53 weeks or a calendar year, and a year to date lines up with last year's to within a week or so.</summary>
    private const int MinYearDays = 350, MaxYearDays = 380, CalendarDriftDays = 10;

    /// <summary>
    /// A period's share count restated by this much or more, either way, is a split: three for two
    /// is the smallest looked for. A correction moves a weighted average by a percent or two.
    /// </summary>
    private const double MinSplitRatio = 1.4;

    /// <summary>How far a restated count's rounding may leave the ratio from the split's own and still be read as it.</summary>
    private const double SplitRoundingTolerance = 0.01;

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
        var dilutedShares = SplitAdjustedSeries(usGaap, DilutedSharesTag, "shares");

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
    private static Dictionary<Period, double> Series(Dictionary<string, CompanyConcept?>? taxonomy, string tag, string unit) =>
        FactsByPeriod(taxonomy, tag, unit).ToDictionary(period => period.Key, period => period.Value[^1].Val);

    /// <summary>
    /// A share-count <see cref="Series"/> with every figure on the count of the latest filing: one
    /// last filed before a split was first seen is multiplied by it, to the whole share, as the
    /// filer would have restated it had a later filing reached back that far.
    /// </summary>
    private static Dictionary<Period, double> SplitAdjustedSeries(Dictionary<string, CompanyConcept?>? taxonomy, string tag, string unit)
    {
        var factsByPeriod = FactsByPeriod(taxonomy, tag, unit);
        var splits = Splits(factsByPeriod.Values);
        return factsByPeriod.ToDictionary(period => period.Key, period =>
        {
            var latest = period.Value[^1];
            var since = splits.Where(split => split.FirstFiled > latest.Filed).ToList();
            return since.Count == 0
                ? latest.Val
                : Math.Round(since.Aggregate(latest.Val, (shares, split) => shares * split.Factor), MidpointRounding.AwayFromZero);
        });
    }

    /// <summary>A split, as seen from the filings: the day the first filing on the new count was made, and new shares per old.</summary>
    private readonly record struct Split(DateOnly FirstFiled, double Factor);

    /// <summary>
    /// Reads the splits out of one share-count concept. A period whose count differs between two
    /// successive filings by <see cref="MinSplitRatio"/> or more was restated for a split that took
    /// effect between them, and the ratio is the split. One split is seen this way many times, in
    /// each 10-Q and 10-K of the year after it; the earliest restating filing dates it, since any
    /// filing on the new count restates its own comparatives. Every restatement whose earlier
    /// filing predates that day is a sighting of the same split, and the median of their ratios is
    /// taken, so that a sighting which spans two splits does not set the factor. A sighting that
    /// spans two is further from one for one than the split itself, so the ratios are ranked by
    /// that distance, and of an even number the nearer of the middle two is the median.
    /// </summary>
    private static List<Split> Splits(IEnumerable<List<CompanyFact>> factsByPeriod)
    {
        var restatements = factsByPeriod
            // One figure per filing day, the one Series takes: the last EDGAR lists.
            .Select(facts => facts.GroupBy(f => f.Filed).Select(sameDay => sameDay.Last()).ToList())
            .SelectMany(facts => facts.Zip(facts.Skip(1), (before, after) => (Before: before, After: after)))
            .Where(pair => pair.Before.Val > 0 && pair.After.Val > 0)
            .Select(pair => (Before: pair.Before.Filed, After: pair.After.Filed, Ratio: pair.After.Val / pair.Before.Val))
            .Where(restatement => restatement.Ratio >= MinSplitRatio || restatement.Ratio <= 1 / MinSplitRatio)
            .OrderBy(restatement => restatement.After)
            .ToList();

        var splits = new List<Split>();
        while (restatements.Count > 0)
        {
            var firstFiled = restatements[0].After;
            var sightings = restatements
                .Where(r => r.Before < firstFiled)
                .Select(r => r.Ratio)
                .OrderBy(ratio => Math.Abs(Math.Log(ratio)))
                .ToList();
            splits.Add(new Split(firstFiled, NearestSplitRatio(sightings[(sightings.Count - 1) / 2])));
            restatements.RemoveAll(r => r.Before < firstFiled);
        }
        return splits;
    }

    /// <summary>
    /// A count restated in millions gives four for one as 3.9968. A split is whole shares or halves
    /// for one, or one for them, so a ratio within <see cref="SplitRoundingTolerance"/> of such a
    /// number is read as it; any other is kept as found.
    /// </summary>
    private static double NearestSplitRatio(double ratio)
    {
        if (ratio < 1) return 1 / NearestSplitRatio(1 / ratio);
        var nearest = Math.Round(ratio * 2) / 2;
        return Math.Abs(ratio / nearest - 1) <= SplitRoundingTolerance ? nearest : ratio;
    }

    /// <summary>One concept's 10-K and 10-Q facts in one unit, by period, each period's in the order they were filed.</summary>
    private static Dictionary<Period, List<CompanyFact>> FactsByPeriod(Dictionary<string, CompanyConcept?>? taxonomy, string tag, string unit)
    {
        var facts = taxonomy?.GetValueOrDefault(tag)?.Units?.GetValueOrDefault(unit) ?? [];
        return facts
            .Where(f => f is { Start: not null, Form: not null } && Forms.Contains(f.Form))
            .GroupBy(f => new Period(f!.Start!.Value, f.End))
            // A stable sort: of two facts filed the same day, the one EDGAR lists later is taken.
            .ToDictionary(period => period.Key, period => period.OrderBy(f => f!.Filed).Select(f => f!).ToList());
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
