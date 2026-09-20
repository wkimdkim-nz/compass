using System.Text.Json.Serialization;

namespace Compass.Api.Projections;

/// <summary>
/// The valuation multiple applied at the end of the horizon: P/E on future net income, or P/S on
/// future revenue. A closed pair of cases rather than a number with a flag beside it, so a
/// multiple cannot exist without saying what it multiplies. On the wire it is the vectors file's
/// <c>{ "kind": "pe" | "ps", "value": n }</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Pe), "pe")]
[JsonDerivedType(typeof(Ps), "ps")]
public abstract record ExitMultiple
{
    private ExitMultiple(double value) => Value = value;

    public double Value { get; }

    public sealed record Pe(double Value) : ExitMultiple(Value);

    public sealed record Ps(double Value) : ExitMultiple(Value);
}

/// <summary>The three base facts the formula reads. Raw values, not millions.</summary>
public record BaseFacts(double CurrentPrice, double TtmRevenue, double DilutedShares);

/// <summary>
/// One scenario's assumptions. Rates are decimals per year (0.10 = 10%); the margin is ignored on
/// the P/S path; the horizon is whole years.
/// </summary>
public record AssumptionSet(
    double GrowthRate,
    double FutureNetMargin,
    ExitMultiple ExitMultiple,
    double DilutionRate,
    int Horizon);

public record ProjectionOutputs(double FuturePrice, double X, double Cagr);

/// <summary>Why a projection was refused. The web calculator's reasons carry the same names.</summary>
public enum ProjectionRejection
{
    NotFinite,
    HorizonNotPositive,
    PriceNotPositive,
    SharesNotPositive,
    RevenueNegative,
    GrowthBelowMinusOne,
    DilutionNotAboveMinusOne,
    ExitMultipleNegative,
    MarginNegativeOnPe,
}

/// <summary>Either the outputs or the reason there are none. Never outputs holding NaN or infinity.</summary>
public abstract record ProjectionResult
{
    private ProjectionResult() { }

    public sealed record Computed(ProjectionOutputs Outputs) : ProjectionResult;

    public sealed record Rejected(ProjectionRejection Reason) : ProjectionResult;
}

/// <summary>
/// The authoritative projection calculator: the glossary's core formula (docs/glossary.md) in
/// double, with no I/O, no database and no configuration. Its twin is <c>calculator.ts</c> in the
/// web app, the live preview; both assert every case in <c>spec/projection-vectors.json</c>, so
/// the two cannot drift (ADR 0004, amended 2026-09-17).
/// </summary>
public static class ProjectionCalculator
{
    public static ProjectionResult Calculate(BaseFacts facts, AssumptionSet assumptions)
    {
        var (currentPrice, ttmRevenue, dilutedShares) = facts;
        var (growthRate, futureNetMargin, exitMultiple, dilutionRate, horizon) = assumptions;
        // The margin plays no part on the P/S path, so there it is not checked either.
        var margin = exitMultiple is ExitMultiple.Ps ? 1 : futureNetMargin;

        double[] values = [currentPrice, ttmRevenue, dilutedShares, growthRate, margin, exitMultiple.Value, dilutionRate];
        if (!values.All(double.IsFinite)) return Rejected(ProjectionRejection.NotFinite);
        if (horizon <= 0) return Rejected(ProjectionRejection.HorizonNotPositive);
        if (currentPrice <= 0) return Rejected(ProjectionRejection.PriceNotPositive);
        if (dilutedShares <= 0) return Rejected(ProjectionRejection.SharesNotPositive);
        if (ttmRevenue < 0) return Rejected(ProjectionRejection.RevenueNegative);
        // Below -100% a company has lost more than all of its revenue, and the sign of the
        // compounded figure would flip with the horizon's parity.
        if (growthRate < -1) return Rejected(ProjectionRejection.GrowthBelowMinusOne);
        // At -100% every share is bought back and the per-share price divides by zero.
        if (dilutionRate <= -1) return Rejected(ProjectionRejection.DilutionNotAboveMinusOne);
        if (exitMultiple.Value < 0) return Rejected(ProjectionRejection.ExitMultipleNegative);
        // A loss at the horizon has no P/E price; the glossary's answer is to use P/S instead.
        if (margin < 0) return Rejected(ProjectionRejection.MarginNegativeOnPe);

        var futureRevenue = ttmRevenue * Math.Pow(1 + growthRate, horizon);
        var futureShares = dilutedShares * Math.Pow(1 + dilutionRate, horizon);
        // `margin` is 1 on the P/S path, which makes this the revenue multiple the glossary states.
        var futureMarketCap = futureRevenue * margin * exitMultiple.Value;
        var futurePrice = futureMarketCap / futureShares;
        var x = futurePrice / currentPrice;
        var cagr = Math.Pow(x, 1.0 / horizon) - 1;

        // Every input was in range, so anything non-finite here is a double overflowing.
        if (!double.IsFinite(futurePrice) || !double.IsFinite(x) || !double.IsFinite(cagr))
        {
            return Rejected(ProjectionRejection.NotFinite);
        }
        return new ProjectionResult.Computed(new ProjectionOutputs(futurePrice, x, cagr));
    }

    private static ProjectionResult.Rejected Rejected(ProjectionRejection reason) => new(reason);
}
