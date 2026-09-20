using Compass.Api.MarketData;

namespace Compass.Api.Tests;

/// <summary>
/// The EDGAR normaliser against recorded <c>companyfacts</c> documents (ticket #72, ADR 0010). No
/// network and no class fixture: the normaliser is pure, so each test reads a recorded document
/// and asserts on the figures.
/// </summary>
/// <remarks>
/// Every expected figure was derived by hand from the fixture, and the sums are written out in
/// the test so they can be checked against the file. They are the filer's own numbers in whole
/// dollars and whole shares, so they are compared exactly.
/// </remarks>
public class EdgarNormaliserTests
{
    private const double Million = 1_000_000;

    private static Fundamentals Normalised(string ticker) =>
        Assert.IsType<FundamentalsResult.Normalised>(EdgarNormaliser.Normalise(EdgarFixtures.CompanyFacts(ticker)))
            .Fundamentals;

    private static FundamentalsResult.Unsupported Unsupported(CompanyFacts? facts) =>
        Assert.IsType<FundamentalsResult.Unsupported>(EdgarNormaliser.Normalise(facts));

    private static AnnualFigures Year(string fiscalYearEnd, double revenueMillions, double netIncomeMillions, double dilutedShares) =>
        new(DateOnly.Parse(fiscalYearEnd), revenueMillions * Million, netIncomeMillions * Million, dilutedShares);

    /// <summary>
    /// Apple's latest filing is the 10-Q to 27 June 2026, three quarters into fiscal 2026, and its
    /// 10-Ks carry no fourth-quarter facts. So the TTM is Q4 of fiscal 2025, which exists only as
    /// FY − nine months, plus the nine months since.
    /// </summary>
    [Fact]
    public void The_fourth_quarter_is_the_year_less_its_first_nine_months()
    {
        var apple = Normalised("AAPL");

        // FY2025 416,161 − 9M FY2025 313,695 = Q4 102,466; + 9M FY2026 364,357.
        Assert.Equal(466_823 * Million, apple.TtmRevenue);
        // FY2025 112,010 − 9M FY2025 84,544 = Q4 27,466; + 9M FY2026 101,464.
        Assert.Equal(128_930 * Million, apple.TtmNetIncome);
        Assert.Equal(128_930.0 / 466_823, apple.CurrentNetMargin!.Value, precision: 12);
        Assert.Equal(new DateOnly(2026, 6, 27), apple.FiguresTo);
    }

    [Fact]
    public void Current_diluted_shares_are_the_latest_quarters_average_not_the_year_to_dates()
    {
        // The 10-Q to 27 June 2026 reports 14,714,676,000 for the quarter and 14,750,302,000 for the nine months.
        Assert.Equal(14_714_676_000, Normalised("AAPL").DilutedShares);
    }

    [Fact]
    public void A_buyback_series_is_a_share_count_that_falls_every_year()
    {
        var apple = Normalised("AAPL");

        Assert.Equal(
            [
                Year("2021-09-25", 365_817, 94_680, 16_864_919_000),
                Year("2022-09-24", 394_328, 99_803, 16_325_819_000),
                Year("2023-09-30", 383_285, 96_995, 15_812_547_000),
                Year("2024-09-28", 391_035, 93_736, 15_408_095_000),
                Year("2025-09-27", 416_161, 112_010, 15_004_697_000),
            ],
            apple.History);
    }

    /// <summary>
    /// The nine months to 28 June 2025 are in the fixture twice, from the 10-Q that first reported
    /// them and again as the comparative in the 2026 10-Q. Subtracted twice, the TTM would be
    /// 313,695 short.
    /// </summary>
    [Fact]
    public void A_period_repeated_as_a_comparative_is_counted_once()
    {
        var facts = EdgarFixtures.CompanyFacts("AAPL");
        var nineMonthsToJune2025 = facts.Facts!["us-gaap"]!["RevenueFromContractWithCustomerExcludingAssessedTax"]!
            .Units!["USD"]!
            .Count(f => f!.Start == new DateOnly(2024, 9, 29) && f.End == new DateOnly(2025, 6, 28));

        Assert.Equal(2, nineMonthsToJune2025);
        Assert.Equal(466_823 * Million, Normalised("AAPL").TtmRevenue);
    }

    /// <summary>
    /// When the latest filing is the 10-K itself there is nothing to add or take away. Microsoft's
    /// year ends in June; the 10-K for fiscal 2026 was filed in July and no 10-Q has followed it.
    /// </summary>
    [Fact]
    public void When_the_latest_filing_is_the_annual_report_the_ttm_is_that_year()
    {
        var microsoft = Normalised("MSFT");

        Assert.Equal(331_839 * Million, microsoft.TtmRevenue);
        Assert.Equal(133_749 * Million, microsoft.TtmNetIncome);
        Assert.Equal(133_749.0 / 331_839, microsoft.CurrentNetMargin!.Value, precision: 12);
        // A 10-K carries no fourth-quarter share count either, so the year's average is the latest there is.
        Assert.Equal(7_453_000_000, microsoft.DilutedShares);
        Assert.Equal(new DateOnly(2026, 6, 30), microsoft.FiguresTo);
        Assert.Equal(
            [
                Year("2022-06-30", 198_270, 72_738, 7_540_000_000),
                Year("2023-06-30", 211_915, 72_361, 7_472_000_000),
                Year("2024-06-30", 245_122, 88_136, 7_469_000_000),
                Year("2025-06-30", 281_724, 101_832, 7_465_000_000),
                Year("2026-06-30", 331_839, 133_749, 7_453_000_000),
            ],
            microsoft.History);
    }

    /// <summary>
    /// Alphabet files its revenue under <c>Revenues</c>, except that the only 10-Ks reporting
    /// fiscal 2022 used <c>RevenueFromContractWithCustomerExcludingAssessedTax</c>. One tag alone
    /// leaves a hole in the history; the tag is chosen per period.
    /// </summary>
    [Fact]
    public void A_year_filed_under_a_different_revenue_tag_is_still_found()
    {
        var alphabet = Normalised("GOOGL");

        Assert.Equal(
            [
                Year("2022-12-31", 282_836, 59_972, 13_159_000_000),
                Year("2023-12-31", 307_394, 73_795, 12_722_000_000),
                Year("2024-12-31", 350_018, 100_118, 12_447_000_000),
                Year("2025-12-31", 402_836, 132_170, 12_230_000_000),
            ],
            alphabet.History);
    }

    /// <summary>
    /// Alphabet has revenue and net income for 2021 but <c>companyfacts</c> holds no diluted share
    /// count for that year. A year missing any of the three would leave the
    /// growth and dilution defaults measured over different spans, so the history stops there.
    /// </summary>
    [Fact]
    public void The_history_stops_at_the_first_year_missing_a_figure() =>
        Assert.Equal(new DateOnly(2022, 12, 31), Normalised("GOOGL").History[0].FiscalYearEnd);

    /// <summary>
    /// Alphabet's cover-page share count is reported per class and so is absent from
    /// <c>companyfacts</c>. Nothing here reads it: shares are the diluted weighted average.
    /// </summary>
    [Fact]
    public void A_filer_with_no_cover_page_share_count_normalises_like_any_other()
    {
        var facts = EdgarFixtures.CompanyFacts("GOOGL");
        Assert.DoesNotContain("EntityCommonStockSharesOutstanding", facts.Facts!["dei"]!.Keys);

        var alphabet = Normalised("GOOGL");

        // FY2025 402,836 − 6M 2025 186,662 + 6M 2026 229,692.
        Assert.Equal(445_866 * Million, alphabet.TtmRevenue);
        // FY2025 132,170 − 6M 2025 62,736 + 6M 2026 174,771.
        Assert.Equal(244_205 * Million, alphabet.TtmNetIncome);
        Assert.Equal(244_205.0 / 445_866, alphabet.CurrentNetMargin!.Value, precision: 12);
        Assert.Equal(12_309_000_000, alphabet.DilutedShares);
        Assert.Equal(new DateOnly(2026, 6, 30), alphabet.FiguresTo);
    }

    /// <summary>
    /// JPMorgan's 10-K reports revenue under <c>Revenues</c> and its 10-Qs only under
    /// <c>RevenuesNetOfInterestExpense</c>, so one TTM is put together from two tags.
    /// </summary>
    [Fact]
    public void The_year_and_the_quarters_may_be_filed_under_different_revenue_tags()
    {
        var jpmorgan = Normalised("JPM");

        // FY2025 182,447 (Revenues) − 6M 2025 90,222 + 6M 2026 107,183 (both RevenuesNetOfInterestExpense).
        Assert.Equal(199_408 * Million, jpmorgan.TtmRevenue);
        // Net income 65,067: see the next test.
        Assert.Equal(65_067.0 / 199_408, jpmorgan.CurrentNetMargin!.Value, precision: 12);
        Assert.Equal(2_694_200_000, jpmorgan.DilutedShares);
        Assert.Equal(new DateOnly(2026, 6, 30), jpmorgan.FiguresTo);
    }

    /// <summary>
    /// JPMorgan's proxy statement, filed after the 10-K, repeats each year's net income rounded to
    /// the nearest hundred million. Latest-filed wins only among 10-Ks and 10-Qs.
    /// </summary>
    [Fact]
    public void A_fact_from_any_other_form_is_ignored_even_when_it_was_filed_later()
    {
        var jpmorgan = Normalised("JPM");

        // FY2025 57,048 − 6M 2025 29,630 + 6M 2026 37,649. The DEF 14A says 57,000 for the year.
        Assert.Equal(65_067 * Million, jpmorgan.TtmNetIncome);
        Assert.Equal(
            [
                Year("2021-12-31", 121_649, 48_334, 3_026_600_000),
                Year("2022-12-31", 128_695, 37_676, 2_970_000_000),
                Year("2023-12-31", 158_104, 49_552, 2_943_100_000),
                Year("2024-12-31", 177_556, 58_471, 2_879_000_000),
                Year("2025-12-31", 182_447, 57_048, 2_781_500_000),
            ],
            jpmorgan.History);
    }

    /// <summary>
    /// CoreWeave first reported the six months to June 2025 as 2,194,420,000 of revenue and a loss
    /// of 605,150,000; the 2026 10-Q restates them in millions, as 2,194 and 605. The later filing
    /// is the company's current word on the period.
    /// </summary>
    [Fact]
    public void A_restated_period_takes_the_figure_from_the_latest_filing()
    {
        var coreweave = Normalised("CRWV");

        // FY2025 5,131 − 6M 2025 2,194 + 6M 2026 4,653.
        Assert.Equal(7_590 * Million, coreweave.TtmRevenue);
        // FY2025 −1,167 − 6M 2025 −605 + 6M 2026 −1,366.
        Assert.Equal(-1_928 * Million, coreweave.TtmNetIncome);
    }

    [Fact]
    public void An_unprofitable_company_has_a_negative_margin_and_a_three_year_history()
    {
        var coreweave = Normalised("CRWV");

        Assert.Equal(-1_928.0 / 7_590, coreweave.CurrentNetMargin!.Value, precision: 12);
        Assert.Equal(551_000_000, coreweave.DilutedShares);
        Assert.Equal(new DateOnly(2026, 6, 30), coreweave.FiguresTo);
        // Listed in March 2025: its first 10-K carries three years and there is nothing before them.
        Assert.Equal(
            [
                Year("2023-12-31", 229, -594, 192_000_000),
                Year("2024-12-31", 1_915, -863, 218_000_000),
                Year("2025-12-31", 5_131, -1_167, 436_000_000),
            ],
            coreweave.History);
    }

    /// <summary>
    /// Millrose was spun out of Lennar in February 2025 and has reported revenue for one fiscal
    /// year. The base facts are all there; one year of history is too little to seed from, which
    /// is the lookup's business (ADR 0004), not a reason to refuse the company.
    /// </summary>
    [Fact]
    public void A_company_with_one_year_behind_it_normalises_with_a_one_year_history()
    {
        var millrose = Normalised("MRP");

        // FY2025 600,461,000 − 6M 2025 231,700,000 + 6M 2026 391,781,000.
        Assert.Equal(760_542_000, millrose.TtmRevenue);
        // FY2025 379,864,000 − 6M 2025 152,566,000 + 6M 2026 248,765,000.
        Assert.Equal(476_063_000, millrose.TtmNetIncome);
        Assert.Equal(476_063.0 / 760_542, millrose.CurrentNetMargin!.Value, precision: 12);
        Assert.Equal(166_060_914, millrose.DilutedShares);
        Assert.Equal(new DateOnly(2026, 6, 30), millrose.FiguresTo);
        Assert.Equal([new AnnualFigures(new DateOnly(2025, 12, 31), 600_461_000, 379_864_000, 166_026_608)], millrose.History);
    }

    /// <summary>TSMC files a 20-F in IFRS tags: <c>companyfacts</c> holds no us-gaap fact for it at all.</summary>
    [Fact]
    public void A_20_F_issuer_is_refused_as_not_a_US_domestic_filer()
    {
        var refusal = Unsupported(EdgarFixtures.CompanyFacts("TSM"));

        Assert.Equal(UnsupportedFiler.NotUsDomesticFiler, refusal.Kind);
        Assert.Contains("not a US domestic filer", refusal.Reason);
    }

    /// <summary>Vor Biopharma is pre-revenue: years of 10-Ks, and no revenue under any tag.</summary>
    [Fact]
    public void A_filer_with_no_usable_revenue_tag_is_refused_for_that()
    {
        var refusal = Unsupported(EdgarFixtures.CompanyFacts("VOR"));

        Assert.Equal(UnsupportedFiler.NoUsableRevenue, refusal.Kind);
        Assert.Contains("revenue", refusal.Reason);
    }

    /// <summary>
    /// Bob's Discount Furniture listed in 2026 and has filed two 10-Qs and no 10-K, so there is no
    /// fiscal year to take the prior-year quarters away from.
    /// </summary>
    [Fact]
    public void A_filer_with_too_few_periods_to_form_a_ttm_is_refused_for_that()
    {
        var refusal = Unsupported(EdgarFixtures.CompanyFacts("BOBS"));

        Assert.Equal(UnsupportedFiler.TooFewPeriods, refusal.Kind);
        Assert.Contains("twelve months", refusal.Reason);
    }

    /// <summary>
    /// TIC Solutions reports one weighted average for basic and diluted together, under another
    /// tag, and never the diluted one ADR 0010 settles on.
    /// </summary>
    [Fact]
    public void A_filer_with_no_diluted_share_count_is_refused_for_that()
    {
        var refusal = Unsupported(EdgarFixtures.CompanyFacts("TIC"));

        Assert.Equal(UnsupportedFiler.NoDilutedShares, refusal.Kind);
        Assert.Contains("share count", refusal.Reason);
    }

    /// <summary>No filer without net income was found to record, so this one is Apple with the concept taken out.</summary>
    [Fact]
    public void A_filer_with_no_net_income_for_its_ttm_periods_is_refused_for_that()
    {
        var refusal = Unsupported(Without(EdgarFixtures.CompanyFacts("AAPL"), "NetIncomeLoss"));

        Assert.Equal(UnsupportedFiler.NoNetIncome, refusal.Kind);
        Assert.Contains("net income", refusal.Reason);
    }

    [Fact]
    public void Every_refusal_has_its_own_plain_reason()
    {
        string[] reasons =
        [
            Unsupported(EdgarFixtures.CompanyFacts("TSM")).Reason,
            Unsupported(EdgarFixtures.CompanyFacts("VOR")).Reason,
            Unsupported(EdgarFixtures.CompanyFacts("BOBS")).Reason,
            Unsupported(EdgarFixtures.CompanyFacts("TIC")).Reason,
            Unsupported(Without(EdgarFixtures.CompanyFacts("AAPL"), "NetIncomeLoss")).Reason,
        ];

        Assert.Equal(reasons.Length, reasons.Distinct().Count());
        Assert.All(reasons, reason =>
        {
            // What a user reads in the 422, so: a sentence, and nothing of EDGAR's vocabulary or ours.
            Assert.EndsWith(".", reason);
            Assert.DoesNotContain("us-gaap", reason);
            Assert.DoesNotContain("Exception", reason);
            Assert.DoesNotContain("NetIncomeLoss", reason);
            Assert.DoesNotContain("WeightedAverage", reason);
        });
    }

    public static TheoryData<string> DocumentsWithPiecesMissing =>
    [
        "null",
        "{}",
        """{ "facts": null }""",
        """{ "facts": { "us-gaap": null } }""",
        """{ "facts": { "us-gaap": { "Revenues": null, "NetIncomeLoss": { "units": null } } } }""",
        """{ "facts": { "us-gaap": { "Revenues": { "units": { "USD": null, "EUR": [null] } } } } }""",
        """{ "facts": { "us-gaap": { "Revenues": { "units": { "USD": [{ "end": "2025-12-31", "val": 1, "form": null, "filed": "2026-02-01" }] } } } } }""",
    ];

    [Theory]
    [MemberData(nameof(DocumentsWithPiecesMissing))]
    public void A_document_with_pieces_missing_is_refused_not_thrown_on(string json) =>
        Unsupported(CompanyFacts.Parse(json));

    /// <summary>The document as recorded, less one us-gaap concept.</summary>
    private static CompanyFacts Without(CompanyFacts facts, string concept) =>
        facts with
        {
            Facts = facts.Facts!.ToDictionary(
                taxonomy => taxonomy.Key,
                taxonomy => taxonomy.Value is null
                    ? null
                    : taxonomy.Value.Where(c => c.Key != concept).ToDictionary(c => c.Key, c => c.Value)),
        };
}
