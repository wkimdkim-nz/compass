namespace Compass.Api.MarketData;

/// <summary>What the fundamentals source needs to reach SEC EDGAR (ADR 0010).</summary>
public sealed class EdgarOptions
{
    public const string SectionName = "Edgar";

    /// <summary>
    /// The configuration key, in every environment. EDGAR needs no key but refuses requests whose
    /// User-Agent carries no contact address; this is that address, and it arrives as the
    /// environment variable <c>Edgar__ContactEmail</c> everywhere. Never from source.
    /// </summary>
    public const string ContactEmailSetting = $"{SectionName}:{nameof(ContactEmail)}";

    public string? ContactEmail { get; set; }
}
