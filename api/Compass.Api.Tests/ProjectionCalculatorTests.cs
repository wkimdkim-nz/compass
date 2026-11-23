using System.Text.Json;
using Compass.Api.Projections;

namespace Compass.Api.Tests;

/// <summary>
/// The authoritative calculator against the shared vectors. The cases are read from
/// <c>spec/projection-vectors.json</c> at the repo root, linked into the test output rather than
/// copied into a list here, so a new case fails this suite and the web one until each is fixed.
/// No fixture: the calculator is pure, so nothing here boots the app or touches a database.
/// </summary>
public class ProjectionCalculatorTests
{
    private sealed record VectorsFile(VectorTolerance Tolerance, List<VectorCase> Cases);

    private sealed record VectorTolerance(double Relative);

    private sealed record VectorCase(string Id, VectorInputs Inputs, ProjectionOutputs Expected);

    private sealed record VectorInputs(
        double CurrentPrice,
        double TtmRevenue,
        double DilutedShares,
        double GrowthRate,
        double FutureNetMargin,
        ExitMultiple ExitMultiple,
        double DilutionRate,
        int Horizon)
    {
        public BaseFacts Facts => new(CurrentPrice, TtmRevenue, DilutedShares);

        public AssumptionSet Assumptions =>
            new(GrowthRate, FutureNetMargin, ExitMultiple, DilutionRate, Horizon);
    }

    private static readonly VectorsFile Vectors = JsonSerializer.Deserialize<VectorsFile>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "projection-vectors.json")),
        JsonSerializerOptions.Web)!;

    public static TheoryData<string> VectorIds => [.. Vectors.Cases.Select(c => c.Id)];

    private static readonly BaseFacts Facts = new(40, 1_000_000_000, 100_000_000);

    private static readonly AssumptionSet Assumptions = new(0.1, 0.2, new ExitMultiple.Pe(20), 0.02, 5);

    private static ProjectionOutputs OutputsOf(ProjectionResult result) =>
        Assert.IsType<ProjectionResult.Computed>(result).Outputs;

    private static void AssertRejected(ProjectionRejection reason, ProjectionResult result) =>
        Assert.Equal(reason, Assert.IsType<ProjectionResult.Rejected>(result).Reason);

    [Fact]
    public void The_vectors_file_holds_at_least_one_case() => Assert.NotEmpty(Vectors.Cases);

    [Theory]
    [MemberData(nameof(VectorIds))]
    public void Every_vector_is_matched_within_the_tolerance_the_file_states(string id)
    {
        var vector = Vectors.Cases.Single(c => c.Id == id);

        var outputs = OutputsOf(ProjectionCalculator.Calculate(vector.Inputs.Facts, vector.Inputs.Assumptions));

        AssertWithin(vector.Expected.FuturePrice, outputs.FuturePrice);
        AssertWithin(vector.Expected.X, outputs.X);
        AssertWithin(vector.Expected.Cagr, outputs.Cagr);
    }

    private static void AssertWithin(double expected, double actual) =>
        Assert.InRange(Math.Abs(actual - expected), 0, Vectors.Tolerance.Relative * Math.Max(1, Math.Abs(expected)));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_horizon_of_zero_or_less_is_rejected(int horizon) =>
        AssertRejected(
            ProjectionRejection.HorizonNotPositive,
            ProjectionCalculator.Calculate(Facts, Assumptions with { Horizon = horizon }));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_price_of_zero_or_less_is_rejected(double price) =>
        AssertRejected(
            ProjectionRejection.PriceNotPositive,
            ProjectionCalculator.Calculate(Facts with { CurrentPrice = price }, Assumptions));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_share_count_of_zero_or_less_is_rejected(double shares) =>
        AssertRejected(
            ProjectionRejection.SharesNotPositive,
            ProjectionCalculator.Calculate(Facts with { DilutedShares = shares }, Assumptions));

    [Fact]
    public void Negative_revenue_is_rejected() =>
        AssertRejected(
            ProjectionRejection.RevenueNegative,
            ProjectionCalculator.Calculate(Facts with { TtmRevenue = -1 }, Assumptions));

    [Fact]
    public void Growth_below_a_total_loss_is_rejected() =>
        AssertRejected(
            ProjectionRejection.GrowthBelowMinusOne,
            ProjectionCalculator.Calculate(Facts, Assumptions with { GrowthRate = -1.5 }));

    [Fact]
    public void Dilution_that_erases_every_share_is_rejected() =>
        AssertRejected(
            ProjectionRejection.DilutionNotAboveMinusOne,
            ProjectionCalculator.Calculate(Facts, Assumptions with { DilutionRate = -1 }));

    [Fact]
    public void A_negative_exit_multiple_is_rejected() =>
        AssertRejected(
            ProjectionRejection.ExitMultipleNegative,
            ProjectionCalculator.Calculate(Facts, Assumptions with { ExitMultiple = new ExitMultiple.Ps(-2) }));

    [Fact]
    public void A_negative_margin_is_rejected_on_the_pe_path_and_ignored_on_the_ps_path()
    {
        var loss = Assumptions with { FutureNetMargin = -0.1 };

        AssertRejected(ProjectionRejection.MarginNegativeOnPe, ProjectionCalculator.Calculate(Facts, loss));
        OutputsOf(ProjectionCalculator.Calculate(Facts, loss with { ExitMultiple = new ExitMultiple.Ps(4) }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_input_that_is_not_a_finite_number_is_rejected(double value) =>
        AssertRejected(
            ProjectionRejection.NotFinite,
            ProjectionCalculator.Calculate(Facts with { TtmRevenue = value }, Assumptions));

    [Fact]
    public void Outputs_too_large_for_a_double_are_rejected_rather_than_returned_as_infinity() =>
        AssertRejected(
            ProjectionRejection.NotFinite,
            ProjectionCalculator.Calculate(Facts with { TtmRevenue = 1e300 }, Assumptions with { GrowthRate = 1e10 }));

    [Fact]
    public void Zero_margin_on_pe_zero_revenue_and_a_total_loss_of_revenue_give_a_worthless_share_not_NaN()
    {
        var worthless = new ProjectionOutputs(FuturePrice: 0, X: 0, Cagr: -1);

        Assert.Equal(worthless, OutputsOf(ProjectionCalculator.Calculate(Facts, Assumptions with { FutureNetMargin = 0 })));
        Assert.Equal(worthless, OutputsOf(ProjectionCalculator.Calculate(Facts with { TtmRevenue = 0 }, Assumptions)));
        Assert.Equal(worthless, OutputsOf(ProjectionCalculator.Calculate(
            Facts with { TtmRevenue = 0 }, Assumptions with { ExitMultiple = new ExitMultiple.Ps(6) })));
        Assert.Equal(worthless, OutputsOf(ProjectionCalculator.Calculate(Facts, Assumptions with { GrowthRate = -1 })));
    }

    [Fact]
    public void No_combination_of_inputs_lets_NaN_or_infinity_out()
    {
        double[] samples =
        [
            -1e308, -2, -1, -0.5, 0, 0.5, 1, 7, 1e308,
            double.PositiveInfinity, double.NegativeInfinity, double.NaN,
        ];
        int[] horizons = [int.MinValue, -1, 0, 1, 5, int.MaxValue];

        foreach (var a in samples)
        foreach (var b in samples)
        foreach (var horizon in horizons)
        foreach (ExitMultiple multiple in new ExitMultiple[] { new ExitMultiple.Pe(a), new ExitMultiple.Ps(a), new ExitMultiple.Pe(3) })
        {
            ProjectionResult[] results =
            [
                ProjectionCalculator.Calculate(new BaseFacts(a, b, 100), Assumptions with { ExitMultiple = multiple, Horizon = horizon }),
                ProjectionCalculator.Calculate(Facts with { DilutedShares = a }, Assumptions with { GrowthRate = b, ExitMultiple = multiple, Horizon = horizon }),
                ProjectionCalculator.Calculate(Facts, Assumptions with { DilutionRate = a, FutureNetMargin = b, ExitMultiple = multiple, Horizon = horizon }),
            ];

            foreach (var computed in results.OfType<ProjectionResult.Computed>())
            {
                Assert.True(double.IsFinite(computed.Outputs.FuturePrice));
                Assert.True(double.IsFinite(computed.Outputs.X));
                Assert.True(double.IsFinite(computed.Outputs.Cagr));
            }
        }
    }
}
