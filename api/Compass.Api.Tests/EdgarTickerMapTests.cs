using Compass.Api.MarketData;

namespace Compass.Api.Tests;

/// <summary>
/// Ticker → CIK through a recorded copy of the SEC ticker file (ticket #72). The file lists every
/// SEC registrant with a ticker, foreign ones included, so resolving says only that EDGAR knows
/// the company; whether Compass can read its filings is the normaliser's question.
/// </summary>
public class EdgarTickerMapTests
{
    private static readonly EdgarTickerMap Map = EdgarFixtures.TickerMap();

    [Fact]
    public void A_ticker_resolves_to_its_company() =>
        Assert.Equal(new EdgarCompany(320193, "AAPL", "Apple Inc."), Map.Resolve("AAPL"));

    [Theory]
    [InlineData("aapl")]
    [InlineData("Aapl")]
    [InlineData("  aapl ")]
    public void Resolving_ignores_case_and_surrounding_space(string typed) =>
        Assert.Equal(320193, Map.Resolve(typed)?.Cik);

    [Fact]
    public void The_ticker_answered_is_the_files_upper_cased_one_not_what_was_typed() =>
        Assert.Equal("MSFT", Map.Resolve("msft")?.Ticker);

    [Theory]
    [InlineData("ZZZZZZ")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unknown_ticker_is_not_found(string typed) => Assert.Null(Map.Resolve(typed));

    [Fact]
    public void Two_share_classes_of_one_company_resolve_to_the_same_cik()
    {
        Assert.Equal(1652044, Map.Resolve("GOOGL")?.Cik);
        Assert.Equal(1652044, Map.Resolve("GOOG")?.Cik);
    }

    [Theory]
    [InlineData("BRK-B")]
    [InlineData("brk.b")]
    public void A_share_class_suffix_resolves_whether_typed_with_a_dash_or_a_dot(string typed) =>
        Assert.Equal(1067983, Map.Resolve(typed)?.Cik);

    [Fact]
    public void A_foreign_filer_the_file_lists_still_resolves() =>
        Assert.Equal(1046179, Map.Resolve("TSM")?.Cik);

    [Fact]
    public void A_ticker_file_with_nothing_in_it_knows_no_ticker() =>
        Assert.Null(EdgarTickerMap.Parse("{}").Resolve("AAPL"));
}
