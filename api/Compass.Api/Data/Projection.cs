namespace Compass.Api.Data;

/// <summary>
/// A user's saved thesis on one company (ADR 0004, amended): the ticker and up to three
/// <see cref="Scenario"/>s. There is at most one per user per ticker. It holds no base facts and
/// no outputs: the first are fetched live each time it is read, the second computed.
/// </summary>
public class Projection
{
    public const int MaxTickerLength = 16;
    public const int MaxCompanyNameLength = 200;

    public Guid Id { get; set; }

    /// <summary>Scalar foreign key to <c>"AspNetUsers".id</c>. No navigation property, on purpose.</summary>
    public Guid UserId { get; set; }

    /// <summary>Normalised, which is what makes the unique index on it and the user case-blind. See <see cref="NormaliseTicker"/>.</summary>
    public string Ticker { get; set; } = "";

    public string CompanyName { get; set; } = "";

    /// <summary>The company's SEC EDGAR CIK, so a reload need not resolve the ticker again. Null for a company typed in by hand.</summary>
    public long? Cik { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<Scenario> Scenarios { get; set; } = [];

    /// <summary>
    /// The ticker as the row keys on it: upper-cased, and a share class written the way the SEC
    /// writes it, BRK-B, as the ticker lookup reads one. Typed BRK.B it is the same company, and so
    /// the same projection.
    /// </summary>
    public static string NormaliseTicker(string ticker) => ticker.Trim().ToUpperInvariant().Replace('.', '-');
}
