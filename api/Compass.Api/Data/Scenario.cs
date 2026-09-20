using System.Collections.Immutable;

namespace Compass.Api.Data;

/// <summary>
/// One named Assumption Set of a <see cref="Projection"/>: Bear, Base or Bull. The rates and the
/// multiple are <c>numeric</c> in Postgres, never floating point (ADR 0008), and so decimal here;
/// the calculators work in double, and the endpoints convert at the edge.
/// </summary>
public class Scenario
{
    /// <summary>The names a scenario can have, in the order they are answered in.</summary>
    public static readonly ImmutableArray<string> Names = ["bear", "base", "bull"];

    public const int MaxNameLength = 8;

    /// <summary>The two kinds of exit multiple, as the vectors file spells them.</summary>
    public const string Pe = "pe", Ps = "ps";

    public Guid Id { get; set; }

    public Guid ProjectionId { get; set; }

    public string Name { get; set; } = "";

    public decimal GrowthRate { get; set; }

    public decimal FutureNetMargin { get; set; }

    /// <summary><see cref="Pe"/> or <see cref="Ps"/>.</summary>
    public string ExitMultipleKind { get; set; } = "";

    public decimal ExitMultipleValue { get; set; }

    public decimal DilutionRate { get; set; }

    public int Horizon { get; set; }
}
