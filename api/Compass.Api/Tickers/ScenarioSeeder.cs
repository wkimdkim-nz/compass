using Compass.Api.MarketData;

namespace Compass.Api.Tickers;

/// <summary>The exit multiple a scenario is seeded with. The value is null when there is no price to work it out from.</summary>
/// <param name="Kind"><c>pe</c> or <c>ps</c>, as the vectors file spells them.</param>
public sealed record SeededExitMultiple(string Kind, double? Value);

/// <summary>One scenario's default assumptions. Rates are decimals per year, as they are everywhere on the wire.</summary>
public sealed record SeededScenario(
    string Name,
    double GrowthRate,
    double FutureNetMargin,
    SeededExitMultiple ExitMultiple,
    double DilutionRate,
    int Horizon);

/// <summary>
/// The Bear, Base and Bull defaults a company's own filed history gives (ADR 0004, amended
/// 2026-09-17, point 4). Pure.
/// </summary>
public static class ScenarioSeeder
{
    private const int Horizon = 5;

    /// <summary>
    /// Empty when there is nothing sound to seed from: fewer than two years of history, no current
    /// margin, or a history a growth rate cannot be taken over (revenue or shares that start at
    /// nothing or below). Never a scenario holding NaN or infinity.
    /// </summary>
    /// <param name="currentPrice">Null when the quote source failed: the scenarios are seeded all the same, less the exit multiple's value.</param>
    public static IReadOnlyList<SeededScenario> Seed(Fundamentals fundamentals, double? currentPrice)
    {
        var history = fundamentals.History;
        if (history.Count < 2 || fundamentals.CurrentNetMargin is not { } margin) return [];

        var years = history.Count - 1;
        var growth = Cagr(history[0].Revenue, history[^1].Revenue, years);
        var dilution = Cagr(history[0].DilutedShares, history[^1].DilutedShares, years);

        // P/E is price ÷ earnings per share and P/S price ÷ revenue per share, on the TTM figures.
        var (kind, perShareOf) = fundamentals.TtmNetIncome > 0 ? ("pe", fundamentals.TtmNetIncome) : ("ps", fundamentals.TtmRevenue);
        var multiple = currentPrice * fundamentals.DilutedShares / perShareOf;

        if (!double.IsFinite(growth) || !double.IsFinite(dilution) || multiple is { } m && !double.IsFinite(m)) return [];

        var spread = Math.Abs(growth) / 2;
        SeededScenario Scenario(string name, double growthRate) =>
            new(name, growthRate, margin, new SeededExitMultiple(kind, multiple), dilution, Horizon);

        return [Scenario("bear", growth - spread), Scenario("base", growth), Scenario("bull", growth + spread)];
    }

    /// <summary>NaN where there is no rate to speak of: a first figure that is not positive, or a last that is negative.</summary>
    private static double Cagr(double first, double last, int years) =>
        first > 0 ? Math.Pow(last / first, 1.0 / years) - 1 : double.NaN;
}
