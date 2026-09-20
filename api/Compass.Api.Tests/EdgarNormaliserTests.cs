using Compass.Api.MarketData;

namespace Compass.Api.Tests;

/// <summary>
/// The EDGAR normaliser against recorded <c>companyfacts</c> documents (ticket #72, ADR 0010). No
/// network and no class fixture: the normaliser is pure, so each test reads a recorded document
/// and asserts on the figures. Where no recorded filer shows a case, the test says so and makes one up.
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

    /// <summary>
    /// Nvidia split ten for one in June 2024. The 10-K filed in February 2025 restates fiscal 2023
    /// (2,507,000,000 becomes 25,070,000,000) and the 10-Q filed on 28 August 2024 is the first
    /// filing on the new count, but fiscal 2022 was last filed in February 2024 and no later 10-K
    /// reaches back to it. Left as filed, the history would show the share count growing tenfold
    /// in a year.
    /// </summary>
    [Fact]
    public void A_year_no_filing_restated_after_a_split_is_put_on_the_post_split_count()
    {
        var nvidia = Normalised("NVDA");

        // FY2026 215,938 − 6M FY2026 90,805 + 6M FY2027 177,837.
        Assert.Equal(302_970 * Million, nvidia.TtmRevenue);
        // FY2026 120,067 − 6M FY2026 45,197 + 6M FY2027 118,010.
        Assert.Equal(192_880 * Million, nvidia.TtmNetIncome);
        Assert.Equal(24_285_000_000, nvidia.DilutedShares);
        Assert.Equal(
            [
                // Filed as 2,535,000,000, last on 21 February 2024; × 10.
                Year("2022-01-30", 26_914, 9_752, 25_350_000_000),
                Year("2023-01-29", 26_974, 4_368, 25_070_000_000),
                Year("2024-01-28", 60_922, 29_760, 24_940_000_000),
                Year("2025-01-26", 130_497, 72_880, 24_804_000_000),
                Year("2026-01-25", 215_938, 120_067, 24_514_000_000),
            ],
            nvidia.History);
    }

    /// <summary>
    /// Nvidia also split four for one in July 2021, first seen in the 10-Q filed on 20 August 2021.
    /// Fiscal 2022 was filed after that on the four-for-one count already, so only the later split
    /// applies to it: × 40 would be 101,400,000,000.
    /// </summary>
    [Fact]
    public void A_split_is_applied_only_to_figures_last_filed_before_it()
    {
        var fiscal2022 = Normalised("NVDA").History[0];

        Assert.Equal(new DateOnly(2022, 1, 30), fiscal2022.FiscalYearEnd);
        Assert.Equal(25_350_000_000, fiscal2022.DilutedShares);
    }

    /// <summary>
    /// Lucid consolidated one for ten in September 2025; the 10-Q filed on 5 November 2025 is the
    /// first on the new count and the 10-K after it restates 2023 (2,081,772,622 becomes
    /// 208,177,262) and 2024. The two years before were last filed in 2024 and 2025.
    /// </summary>
    [Fact]
    public void A_reverse_split_divides_the_years_no_filing_restated()
    {
        Assert.Equal(
            [
                // Filed as 740,393,759; ÷ 10 is 74,039,375.9, to the whole share.
                new AnnualFigures(new DateOnly(2021, 12, 31), 27_111_000, -2_579_761_000, 74_039_376),
                // Filed as 1,693,258,608; ÷ 10 is 169,325,860.8.
                new AnnualFigures(new DateOnly(2022, 12, 31), 608_181_000, -1_304_460_000, 169_325_861),
                new AnnualFigures(new DateOnly(2023, 12, 31), 595_271_000, -2_828_420_000, 208_177_262),
                new AnnualFigures(new DateOnly(2024, 12, 31), 807_832_000, -2_713_942_000, 244_517_654),
                new AnnualFigures(new DateOnly(2025, 12, 31), 1_353_790_000, -2_698_051_000, 313_400_136),
            ],
            Normalised("LCID").History);
    }

    /// <summary>
    /// A filer changing its fiscal year end reports the short year between on a 10-KT (and 10-QT),
    /// which is a 10-K or 10-Q in every way the normaliser cares about. No such filer was recorded,
    /// so this one is Nvidia with its latest 10-K, filed on 25 February 2026, or its latest 10-Q,
    /// filed on 26 August 2026, relabelled. Ignored, either would leave no TTM to add up.
    /// </summary>
    [Theory]
    [InlineData("2026-02-25", "10-KT")]
    [InlineData("2026-02-25", "10-KT/A")]
    [InlineData("2026-08-26", "10-QT")]
    [InlineData("2026-08-26", "10-QT/A")]
    public void A_transition_report_is_read_like_the_report_it_stands_in_for(string filed, string form)
    {
        var relabelled = Relabelled(EdgarFixtures.CompanyFacts("NVDA"), DateOnly.Parse(filed), form);

        var nvidia = Assert.IsType<FundamentalsResult.Normalised>(EdgarNormaliser.Normalise(relabelled)).Fundamentals;

        Assert.Equal(302_970 * Million, nvidia.TtmRevenue);
        Assert.Equal(new DateOnly(2026, 1, 25), nvidia.History[^1].FiscalYearEnd);
    }

    /// <summary>
    /// Made up, as are the filers below it: a calendar-year filer moves its year end to June and
    /// files a 10-KT for the six months between. The short year is read, but it is not a fiscal
    /// year, so the history stops at it.
    /// </summary>
    [Fact]
    public void A_short_transition_year_ends_the_history()
    {
        var filer = Filer(
            ("10-K", "2022-02-20", [("2020-01-01", "2020-12-31", 500), ("2021-01-01", "2021-12-31", 500)]),
            ("10-KT", "2022-08-20", [("2022-01-01", "2022-06-30", 500)]),
            ("10-K", "2023-08-20", [("2022-07-01", "2023-06-30", 500)]));

        var fundamentals = Assert.IsType<FundamentalsResult.Normalised>(EdgarNormaliser.Normalise(filer)).Fundamentals;

        Assert.Equal([new AnnualFigures(new DateOnly(2023, 6, 30), FilerRevenue, FilerNetIncome, 500)], fundamentals.History);
    }

    /// <summary>
    /// Four for one, with the restated counts rounded to the million: 626,000,000 becomes
    /// 2,502,000,000, a ratio of 3.9968, and 630,000,000 becomes 2,520,000,000. Two sightings, and
    /// the median of them is the rounded one, which is read as four: 2018 is 620,000,000 × 4, where
    /// × 3.9968 would be 2,478,019,169.
    /// </summary>
    [Fact]
    public void A_ratio_left_off_the_whole_number_by_rounding_is_read_as_it()
    {
        var filer = Filer(
            ("10-K", "2021-02-20", [Calendar(2018, 620_000_000), Calendar(2019, 626_000_000), Calendar(2020, 630_000_000)]),
            ("10-K", "2022-02-20", [Calendar(2019, 2_502_000_000), Calendar(2020, 2_520_000_000), Calendar(2021, 2_530_000_000)]));

        Assert.Equal(
            [2_480_000_000, 2_502_000_000, 2_520_000_000, 2_530_000_000],
            History(filer).Select(year => year.DilutedShares));
    }

    /// <summary>Three for two, the smallest split looked for: 627,000,000 becomes 941,000,000, a ratio of 1.5008.</summary>
    [Fact]
    public void Three_for_two_is_read_as_one_and_a_half()
    {
        var filer = Filer(
            ("10-K", "2021-02-20", [Calendar(2018, 620_000_000), Calendar(2019, 627_000_000), Calendar(2020, 630_000_000)]),
            ("10-K", "2022-02-20", [Calendar(2019, 941_000_000), Calendar(2020, 945_000_000), Calendar(2021, 950_000_000)]));

        Assert.Equal(
            [930_000_000, 941_000_000, 945_000_000, 950_000_000],
            History(filer).Select(year => year.DilutedShares));
    }

    /// <summary>A restatement of 1.3 is under three for two, so it is taken for a correction and 2018 is left as filed.</summary>
    [Fact]
    public void A_restatement_under_three_for_two_is_not_a_split()
    {
        var filer = Filer(
            ("10-K", "2021-02-20", [Calendar(2018, 620_000_000), Calendar(2019, 626_000_000), Calendar(2020, 630_000_000)]),
            ("10-K", "2022-02-20", [Calendar(2019, 813_800_000), Calendar(2020, 819_000_000), Calendar(2021, 825_000_000)]));

        Assert.Equal(620_000_000, History(filer)[0].DilutedShares);
    }

    /// <summary>
    /// Ten for one in 2020, then four for one in 2021. 2017 was last filed before both, so it is
    /// × 40. The first split has four sightings: 2018 and 2019 restated by the next 10-K, × 10,
    /// and the quarter and half year to June 2019, not filed again until both splits had happened,
    /// × 40. The median of an even number is the nearer to one for one of the middle two, so the
    /// pair spanning both splits does not make the first split forty for one and 2017 × 160. Less
    /// the ten, that pair is a sighting of the second split, and the earliest: it is first seen in
    /// the 10-Q of August 2021, and dated by the 10-K after it the pair would not reconcile.
    /// </summary>
    [Fact]
    public void Sightings_spanning_two_splits_do_not_set_the_factor_of_the_first()
    {
        var filer = Filer(
            ("10-K", "2020-02-20", [Calendar(2017, 61_000_000), Calendar(2018, 62_000_000), Calendar(2019, 62_600_000)]),
            ("10-K", "2021-02-20", [Calendar(2018, 620_000_000), Calendar(2019, 626_000_000), Calendar(2020, 630_000_000)]),
            ("10-K", "2022-02-20", [Calendar(2019, 2_504_000_000), Calendar(2020, 2_520_000_000), Calendar(2021, 2_530_000_000)]));
        Shares(filer).AddRange(
        [
            new CompanyFact(new DateOnly(2019, 4, 1), new DateOnly(2019, 6, 30), 62_500_000, "10-Q", new DateOnly(2019, 8, 5)),
            new CompanyFact(new DateOnly(2019, 1, 1), new DateOnly(2019, 6, 30), 62_400_000, "10-Q", new DateOnly(2019, 8, 5)),
            new CompanyFact(new DateOnly(2019, 4, 1), new DateOnly(2019, 6, 30), 2_500_000_000, "10-Q", new DateOnly(2021, 8, 5)),
            new CompanyFact(new DateOnly(2019, 1, 1), new DateOnly(2019, 6, 30), 2_496_000_000, "10-Q", new DateOnly(2021, 8, 5)),
        ]);

        Assert.Equal(
            [2_440_000_000, 2_480_000_000, 2_504_000_000, 2_520_000_000, 2_530_000_000],
            History(filer).Select(year => year.DilutedShares));
    }

    /// <summary>
    /// Of two facts for one period filed on one day, the later listed is the one taken, and so the
    /// one a split is read from: here the 10-K after a four for one lists each comparative on the
    /// old count and then on the new.
    /// </summary>
    [Fact]
    public void A_split_is_read_from_the_fact_taken_of_two_filed_on_one_day()
    {
        var filer = Filer(
            ("10-K", "2021-02-20", [Calendar(2018, 620_000_000), Calendar(2019, 626_000_000), Calendar(2020, 630_000_000)]),
            ("10-K", "2022-02-20",
            [
                Calendar(2019, 626_000_000), Calendar(2019, 2_504_000_000),
                Calendar(2020, 630_000_000), Calendar(2020, 2_520_000_000),
                Calendar(2021, 2_530_000_000),
            ]));

        Assert.Equal(2_480_000_000, History(filer)[0].DilutedShares);
    }

    /// <summary>
    /// Tonix consolidates every year, and twice between May 2024 and March 2025 with two 10-Qs
    /// between. The first quarter of 2024 was filed on 13 May 2024 as 80,879,108 and on 12 May 2025
    /// as 27,886, one for 2,900; the second, filed on 16 August 2024, was 4,085,132 and a year on
    /// 41,011, one for 99.6. So one of about 29 fell between those two 10-Qs and one of 100 after
    /// them. 2023, filed on 1 April 2024 as 17,039,309, is 7,925 in the 10-K of 18 March 2025,
    /// which is on today's count. Before the splits were checked that sighting was taken for the
    /// split of May 2024, the one of 100 was dated 12 May 2025, and 2023 came out as 7,925 ÷ 99.5
    /// = 80 shares beside 736,339 for 2024.
    /// <para>
    /// No set of dated factors fits these restatements: they are of weighted averages, which the
    /// other consolidations move too (the quarters of 2023 are restated by 18.3, 19.4 and 21.2
    /// for the one split), and the first quarter of 2024's one for 2,900 sits over a split first
    /// seen on 18 March 2025 and another, of what is left, on 12 May 2025. So nothing is adjusted,
    /// and the history keeps only the years filed since 12 May 2025, the last day a split was
    /// first seen: 2024 and 2025, both in the 10-K of 12 March 2026. 2023 is lost though it was
    /// right, and there is no revenue before it to go further back for.
    /// </para>
    /// </summary>
    [Fact]
    public void Splits_that_do_not_reconcile_with_every_restatement_end_the_history_instead()
    {
        var tonix = Normalised("TNXP");

        Assert.Equal(16_635_254, tonix.DilutedShares);
        Assert.Equal(
            [
                new AnnualFigures(new DateOnly(2024, 12, 31), 10_094_000, -130_036_000, 736_339),
                new AnnualFigures(new DateOnly(2025, 12, 31), 13_107_000, -124_021_000, 8_511_318),
            ],
            tonix.History);
    }

    /// <summary>
    /// Tonix is too ragged to show the sums, so this is the same made up: one for ten in March
    /// 2021 and one for five in June. The one clean sighting of the first is the comparative in
    /// the 10-Q of May 2021, 6,150 million to 615 million; the August 10-Q and the next 10-K
    /// restate by fifty, and the median of 10, 50, 50, 50, 50 is fifty. The second is then read as
    /// five from the first quarter of 2021, 630 million to 126 million a year on, and 2019, last
    /// filed before both, would be 6,100 million ÷ 50 ÷ 5 = 24,400,000. The May 2021 sighting of
    /// ten is not the fifty inferred, so the history is the years filed since the second was first
    /// seen on 10 May 2022: the three in the 10-K of February 2023.
    /// </summary>
    [Fact]
    public void Two_splits_a_quarter_apart_end_the_history_rather_than_adjust_it_twice()
    {
        var filer = Filer(
            ("10-Q", "2020-05-10", [("2020-01-01", "2020-03-31", 6_150_000_000)]),
            ("10-Q", "2020-08-10", [("2020-04-01", "2020-06-30", 6_160_000_000), ("2020-01-01", "2020-06-30", 6_155_000_000)]),
            ("10-K", "2021-02-20", [Calendar(2018, 6_000_000_000), Calendar(2019, 6_100_000_000), Calendar(2020, 6_200_000_000)]),
            ("10-Q", "2021-05-10", [("2020-01-01", "2020-03-31", 615_000_000), ("2021-01-01", "2021-03-31", 630_000_000)]),
            ("10-Q", "2021-08-10", [("2020-04-01", "2020-06-30", 123_200_000), ("2020-01-01", "2020-06-30", 123_100_000)]),
            ("10-K", "2022-02-20", [Calendar(2019, 122_000_000), Calendar(2020, 124_000_000), Calendar(2021, 128_000_000)]),
            ("10-Q", "2022-05-10", [("2021-01-01", "2021-03-31", 126_000_000)]),
            ("10-K", "2023-02-20", [Calendar(2020, 124_000_000), Calendar(2021, 128_000_000), Calendar(2022, 130_000_000)]));

        Assert.Equal([124_000_000, 128_000_000, 130_000_000], History(filer).Select(year => year.DilutedShares));
    }

    /// <summary>
    /// No recorded filer shows it: 2019 filed in thousands as though in units, 626,000, and put
    /// right a year on, which reads as a thousand for one. But 2020 is in both 10-Ks and did not
    /// move, so no split came between them. Adjusted, 2018 would be 620 billion; it was last filed
    /// before the split that was not, and the history ends short of it.
    /// </summary>
    [Fact]
    public void A_scaling_error_put_right_is_not_a_split_when_its_neighbours_did_not_move()
    {
        var filer = Filer(
            ("10-K", "2021-02-20", [Calendar(2018, 620_000_000), Calendar(2019, 626_000), Calendar(2020, 630_000_000)]),
            ("10-K", "2022-02-20", [Calendar(2019, 626_000_000), Calendar(2020, 630_000_000), Calendar(2021, 640_000_000)]));

        Assert.Equal([626_000_000, 630_000_000, 640_000_000], History(filer).Select(year => year.DilutedShares));
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

    [Fact]
    public void No_document_at_all_is_refused_not_thrown_on() => Unsupported(null);

    private const double FilerRevenue = 1_000 * Million, FilerNetIncome = 100 * Million;

    /// <summary>
    /// A made-up filer, for what no recorded one shows: each filing reports the periods given, all
    /// with the same revenue and net income and each with its own diluted share count.
    /// </summary>
    private static CompanyFacts Filer(params (string Form, string Filed, (string Start, string End, double Shares)[] Periods)[] filings)
    {
        CompanyConcept Concept(string unit, Func<double, double> value) => new(new()
        {
            [unit] = filings
                .SelectMany(filing => filing.Periods.Select(period => (CompanyFact?)new CompanyFact(
                    DateOnly.Parse(period.Start), DateOnly.Parse(period.End), value(period.Shares), filing.Form, DateOnly.Parse(filing.Filed))))
                .ToList(),
        });

        return new CompanyFacts("Made Up Inc", new()
        {
            ["us-gaap"] = new()
            {
                ["Revenues"] = Concept("USD", _ => FilerRevenue),
                ["NetIncomeLoss"] = Concept("USD", _ => FilerNetIncome),
                ["WeightedAverageNumberOfDilutedSharesOutstanding"] = Concept("shares", shares => shares),
            },
        });
    }

    private static (string Start, string End, double Shares) Calendar(int year, double shares) =>
        ($"{year}-01-01", $"{year}-12-31", shares);

    private static List<CompanyFact?> Shares(CompanyFacts filer) =>
        filer.Facts!["us-gaap"]!["WeightedAverageNumberOfDilutedSharesOutstanding"]!.Units!["shares"]!;

    private static IReadOnlyList<AnnualFigures> History(CompanyFacts filer) =>
        Assert.IsType<FundamentalsResult.Normalised>(EdgarNormaliser.Normalise(filer)).Fundamentals.History;

    /// <summary>The document as recorded, with every fact from the filing made on one day given another form.</summary>
    private static CompanyFacts Relabelled(CompanyFacts facts, DateOnly filed, string form) =>
        facts with
        {
            Facts = facts.Facts!.ToDictionary(
                taxonomy => taxonomy.Key,
                taxonomy => taxonomy.Value?.ToDictionary(
                    concept => concept.Key,
                    concept => concept.Value is null
                        ? null
                        : new CompanyConcept(concept.Value.Units?.ToDictionary(
                            unit => unit.Key,
                            unit => unit.Value?.Select(f => f?.Filed == filed ? f with { Form = form } : f).ToList())))),
        };

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
