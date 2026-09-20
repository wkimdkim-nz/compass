using Compass.Api.MarketData;

namespace Compass.Api.Tests;

/// <summary>
/// The recorded SEC EDGAR documents under <c>Fixtures/Edgar</c>, copied beside the test assembly.
/// Each is the real document cut down to the concepts the normaliser reads and never edited;
/// <c>scripts/record-edgar-fixture.sh</c> says how, and records a new one.
/// </summary>
public static class EdgarFixtures
{
    public static string Json(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Edgar", $"{name}.json"));

    public static CompanyFacts CompanyFacts(string ticker) => MarketData.CompanyFacts.Parse(Json(ticker));

    public static EdgarTickerMap TickerMap() => EdgarTickerMap.Parse(Json("company_tickers"));
}
