namespace Compass.Api.MarketData;

/// <summary>What the quote source needs to reach Finnhub (ADR 0010).</summary>
public sealed class FinnhubOptions
{
    public const string SectionName = "Finnhub";

    /// <summary>
    /// The configuration key, in every environment. ECS injects the foundation stack's
    /// <c>compass/finnhub-api-key</c> secret as the environment variable <c>Finnhub__ApiKey</c>;
    /// locally it comes from <c>dotnet user-secrets</c>. Never from source.
    /// </summary>
    public const string ApiKeySetting = $"{SectionName}:{nameof(ApiKey)}";

    public string? ApiKey { get; set; }
}
