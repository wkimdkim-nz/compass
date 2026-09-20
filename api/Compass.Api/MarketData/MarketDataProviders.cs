namespace Compass.Api.MarketData;

/// <summary>
/// Which of the two market-data ports is a stub in this process, settled once at startup by the
/// rule of ADR 0010: one setting per port, and a port whose setting is absent gets its stub
/// <b>only in Development</b> — which is also what the test factory boots as. Anywhere else an
/// absent setting stops the process, so a deployed task either talks to the real providers or is
/// not healthy, and the smoke test cannot be satisfied by a stub.
/// </summary>
/// <remarks>
/// Absent means unset or blank. It is a test of the value and not of where it came from: a shell
/// variable passed through docker-compose unset arrives as an empty string.
/// </remarks>
public sealed record MarketDataProviders(bool QuoteSourceIsStub, bool FundamentalsSourceIsStub)
{
    public static MarketDataProviders Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var quoteSourceIsStub = string.IsNullOrWhiteSpace(configuration[FinnhubOptions.ApiKeySetting]);
        var fundamentalsSourceIsStub = string.IsNullOrWhiteSpace(configuration[EdgarOptions.ContactEmailSetting]);

        if (!environment.IsDevelopment())
        {
            string[] missing =
            [
                .. quoteSourceIsStub ? [Describe(FinnhubOptions.ApiKeySetting)] : Array.Empty<string>(),
                .. fundamentalsSourceIsStub ? [Describe(EdgarOptions.ContactEmailSetting)] : Array.Empty<string>(),
            ];
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Not configured: {string.Join(" and ", missing)}. The stub market-data ports are " +
                    $"registered only in Development; the {environment.EnvironmentName} environment " +
                    "must reach the real providers (ADR 0010).");
            }
        }

        return new MarketDataProviders(quoteSourceIsStub, fundamentalsSourceIsStub);
    }

    /// <summary>Says which ports are stubs, and the setting that would make each one real.</summary>
    public void LogStubs(ILogger logger)
    {
        if (QuoteSourceIsStub)
        {
            logger.LogWarning(
                "Using the stub quote source: {Setting} is not set. No call to Finnhub will be made.",
                FinnhubOptions.ApiKeySetting);
        }
        if (FundamentalsSourceIsStub)
        {
            logger.LogWarning(
                "Using the stub fundamentals source: {Setting} is not set. No call to SEC EDGAR will be made.",
                EdgarOptions.ContactEmailSetting);
        }
    }

    /// <summary>The key as configuration spells it and as an environment variable spells it.</summary>
    private static string Describe(string setting) =>
        $"'{setting}' (environment variable {setting.Replace(":", "__")})";
}
