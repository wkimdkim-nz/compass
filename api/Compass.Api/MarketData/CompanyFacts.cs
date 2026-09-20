using System.Text.Json;

namespace Compass.Api.MarketData;

/// <summary>
/// One company's SEC EDGAR <c>companyfacts</c> document, as served: every filed XBRL fact, keyed
/// taxonomy → concept → unit. Only the parts <see cref="EdgarNormaliser"/> reads are modelled.
/// Everything is nullable because the document is somebody else's; the normaliser answers a
/// missing piece with a reason, never an exception.
/// </summary>
public sealed record CompanyFacts(
    string? EntityName,
    Dictionary<string, Dictionary<string, CompanyConcept?>?>? Facts)
{
    public static CompanyFacts? Parse(string json) =>
        JsonSerializer.Deserialize<CompanyFacts>(json, JsonSerializerOptions.Web);
}

public sealed record CompanyConcept(Dictionary<string, List<CompanyFact?>?>? Units);

/// <summary>
/// One filed fact. <see cref="Form"/> and <see cref="Filed"/> describe the filing it came from,
/// not the period it covers: only <see cref="Start"/> and <see cref="End"/> say that. An instant
/// fact (a balance-sheet or cover-page figure) has no <see cref="Start"/>.
/// </summary>
public sealed record CompanyFact(DateOnly? Start, DateOnly End, double Val, string? Form, DateOnly Filed);
