using System.Globalization;
using Compass.Api.Data;

namespace Compass.Api.Projections;

// Every figure is nullable so that one left out of the body is seen to be missing: a bare double
// would arrive as zero, which most of them can validly be, and be saved as if it had been sent.
public sealed record ExitMultipleRequest(string? Kind, double? Value);

public sealed record ScenarioRequest(
    string? Name,
    double? GrowthRate,
    double? FutureNetMargin,
    ExitMultipleRequest? ExitMultiple,
    double? DilutionRate,
    int? Horizon);

public sealed record BaseFactsRequest(double? CurrentPrice, double? TtmRevenue, double? DilutedShares);

/// <summary>
/// What a create or a replace is sent. The base facts are the ones on the user's form, fetched or
/// typed: every scenario is recomputed against them and they are then let go, never stored
/// (ADR 0004, amended). Raw values and decimal rates, as everywhere on the wire.
/// </summary>
/// <param name="Ticker">Required by a create. A replace takes it from the route, and refuses a body that names another.</param>
/// <param name="Cik">Null for a company typed in by hand.</param>
/// <param name="Scenarios">One to three, each of Bear, Base and Bull at most once.</param>
public sealed record SaveProjectionRequest(
    string? Ticker,
    string? CompanyName,
    long? Cik,
    BaseFactsRequest? BaseFacts,
    IReadOnlyList<ScenarioRequest?>? Scenarios);

public abstract record SaveProjectionResult
{
    private SaveProjectionResult() { }

    /// <summary>What is to be stored, and the outputs the .NET calculator gave for it.</summary>
    /// <param name="Scenarios">New rows, belonging to no projection yet, in Bear, Base, Bull order.</param>
    public sealed record Valid(
        string Ticker,
        string CompanyName,
        long? Cik,
        IReadOnlyList<Scenario> Scenarios,
        IReadOnlyDictionary<string, ProjectionOutputs> OutputsByName) : SaveProjectionResult;

    /// <param name="Errors">Keyed by the field at fault, as the body spells it: <c>scenarios[1].horizon</c>.</param>
    public sealed record Invalid(Dictionary<string, string[]> Errors) : SaveProjectionResult;
}

/// <summary>
/// Validates a save and recomputes it, in one pass, because the calculator is most of the
/// validation: whatever it rejects is a 400 under the field at fault. Pure.
/// </summary>
/// <remarks>
/// A rate is turned to the decimal its <c>numeric</c> column holds before anything is computed, and
/// the outputs are computed from that decimal, so what a save answers with is what the stored
/// assumptions give and not what the request's would have. The decimal is the double's shortest
/// round-tripping spelling, so the two differ only for a figure too small for a decimal to tell from zero.
/// </remarks>
public static class SaveProjection
{
    /// <param name="routeTicker">The ticker a replace is addressed to; null for a create, which reads the body's.</param>
    public static SaveProjectionResult Validate(SaveProjectionRequest request, string? routeTicker = null)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors.TryAdd(field, [message]);

        var ticker = Projection.NormaliseTicker(routeTicker ?? request.Ticker ?? "");
        if (ticker.Length == 0)
        {
            Error("ticker", "A ticker is required.");
        }
        else if (ticker.Length > Projection.MaxTickerLength)
        {
            Error("ticker", $"A ticker is at most {Projection.MaxTickerLength} characters.");
        }
        else if (routeTicker is not null && request.Ticker is not null && Projection.NormaliseTicker(request.Ticker) != ticker)
        {
            Error("ticker", "The ticker in the body is not the one in the address.");
        }

        var companyName = request.CompanyName?.Trim() ?? "";
        if (companyName.Length == 0)
        {
            Error("companyName", "A company name is required.");
        }
        else if (companyName.Length > Projection.MaxCompanyNameLength)
        {
            Error("companyName", $"A company name is at most {Projection.MaxCompanyNameLength} characters.");
        }

        BaseFacts? baseFacts = null;
        if (request.BaseFacts is not { } facts)
        {
            Error("baseFacts", "The base facts are required: the scenarios are computed against them.");
        }
        else
        {
            var sent = new BaseFacts(
                Required(facts.CurrentPrice, "baseFacts.currentPrice", "The current price"),
                Required(facts.TtmRevenue, "baseFacts.ttmRevenue", "The TTM revenue"),
                Required(facts.DilutedShares, "baseFacts.dilutedShares", "The diluted shares"));
            if (!errors.Keys.Any(key => key.StartsWith("baseFacts", StringComparison.Ordinal))) baseFacts = sent;
        }

        var scenarios = new List<Scenario>();
        var outputs = new Dictionary<string, ProjectionOutputs>();
        if (request.Scenarios is not { Count: >= 1 and <= 3 })
        {
            Error("scenarios", "A projection has one to three scenarios.");
        }
        else
        {
            for (var i = 0; i < request.Scenarios.Count; i++)
            {
                var field = $"scenarios[{i}]";
                if (request.Scenarios[i] is not { } sent)
                {
                    Error(field, "A scenario is required.");
                    continue;
                }

                var name = sent.Name?.Trim().ToLowerInvariant();
                if (name is null || !Scenario.Names.Contains(name))
                {
                    Error($"{field}.name", "A scenario is named bear, base or bull.");
                }
                else if (scenarios.Any(s => s.Name == name))
                {
                    Error($"{field}.name", $"There is already a {name} scenario.");
                }

                var kind = sent.ExitMultiple?.Kind;
                if (sent.ExitMultiple is null)
                {
                    Error($"{field}.exitMultiple", "An exit multiple is required.");
                }
                else if (kind is not (Scenario.Pe or Scenario.Ps))
                {
                    Error($"{field}.exitMultiple.kind", "An exit multiple is a pe or a ps.");
                }

                var scenario = new Scenario
                {
                    Name = name ?? "",
                    GrowthRate = Numeric(Required(sent.GrowthRate, $"{field}.growthRate", "The growth rate"), $"{field}.growthRate"),
                    FutureNetMargin = Numeric(Required(sent.FutureNetMargin, $"{field}.futureNetMargin", "The future net margin"), $"{field}.futureNetMargin"),
                    ExitMultipleKind = kind ?? "",
                    // A multiple that is missing whole is reported once, above, and not again for its value.
                    ExitMultipleValue = sent.ExitMultiple is null
                        ? 0
                        : Numeric(Required(sent.ExitMultiple.Value, $"{field}.exitMultiple.value", "The exit multiple's value"), $"{field}.exitMultiple.value"),
                    DilutionRate = Numeric(Required(sent.DilutionRate, $"{field}.dilutionRate", "The dilution rate"), $"{field}.dilutionRate"),
                    Horizon = sent.Horizon ?? 0,
                };
                if (sent.Horizon is null) Error($"{field}.horizon", "The horizon is required.");
                scenarios.Add(scenario);

                // Computed only for a scenario sound so far, so that a missing multiple is not also reported as a bad one.
                if (baseFacts is null || errors.Keys.Any(key => key.StartsWith(field, StringComparison.Ordinal))) continue;

                switch (ProjectionCalculator.Calculate(baseFacts, scenario.ToAssumptionSet()))
                {
                    case ProjectionResult.Computed computed:
                        outputs[scenario.Name] = computed.Outputs;
                        break;
                    case ProjectionResult.Rejected rejected:
                        var (at, message) = Describe(rejected.Reason, field);
                        Error(at, message);
                        break;
                }
            }
        }

        if (errors.Count > 0) return new SaveProjectionResult.Invalid(errors);

        return new SaveProjectionResult.Valid(
            ticker, companyName, request.Cik, [.. scenarios.OrderBy(s => Scenario.Order(s.Name))], outputs);

        double Required(double? value, string at, string what)
        {
            if (value is null) Error(at, $"{what} is required.");
            return value ?? 0;
        }

        decimal Numeric(double value, string at)
        {
            if (double.IsFinite(value) &&
                decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            {
                return numeric;
            }

            Error(at, "That figure is too large to save.");
            return 0;
        }
    }

    public static AssumptionSet ToAssumptionSet(this Scenario scenario) => new(
        (double)scenario.GrowthRate,
        (double)scenario.FutureNetMargin,
        scenario.ExitMultipleKind == Scenario.Pe
            ? new ExitMultiple.Pe((double)scenario.ExitMultipleValue)
            : new ExitMultiple.Ps((double)scenario.ExitMultipleValue),
        (double)scenario.DilutionRate,
        scenario.Horizon);

    /// <summary>The field a rejection is reported under, and the words for it. A base fact is at fault for every scenario alike, so it is reported once.</summary>
    private static (string Field, string Message) Describe(ProjectionRejection reason, string scenario) => reason switch
    {
        ProjectionRejection.HorizonNotPositive => ($"{scenario}.horizon", "The horizon is a whole number of years, at least 1."),
        ProjectionRejection.PriceNotPositive => ("baseFacts.currentPrice", "The current price is more than zero."),
        ProjectionRejection.SharesNotPositive => ("baseFacts.dilutedShares", "The diluted shares are more than zero."),
        ProjectionRejection.RevenueNegative => ("baseFacts.ttmRevenue", "The TTM revenue is not negative."),
        ProjectionRejection.GrowthBelowMinusOne => ($"{scenario}.growthRate", "The growth rate is no lower than -1, which is losing all of the revenue."),
        ProjectionRejection.DilutionNotAboveMinusOne => ($"{scenario}.dilutionRate", "The dilution rate is above -1, which is buying back every share."),
        ProjectionRejection.ExitMultipleNegative => ($"{scenario}.exitMultiple.value", "The exit multiple is not negative."),
        ProjectionRejection.MarginNegativeOnPe => ($"{scenario}.futureNetMargin", "A loss at the horizon has no P/E price: use a P/S multiple instead."),
        ProjectionRejection.NotFinite => (scenario, "These assumptions compound to a figure too large to compute."),
        _ => throw new InvalidOperationException($"Unhandled rejection {reason}."),
    };
}
