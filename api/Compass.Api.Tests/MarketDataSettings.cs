using Compass.Api.MarketData;
using Microsoft.AspNetCore.Hosting;

namespace Compass.Api.Tests;

/// <summary>How a test host is given, or denied, the two market-data provider settings.</summary>
public static class MarketDataSettings
{
    /// <summary>
    /// Pins both settings absent, so the app runs on the stub ports whatever the machine holds.
    /// Without it a developer's own <c>dotnet user-secrets</c> key and exported contact address
    /// would reach the test host, and the suite would call the real providers (ADR 0010: tests run
    /// against the stubs only).
    /// </summary>
    public static IWebHostBuilder WithoutMarketDataSettings(this IWebHostBuilder builder) => builder
        .UseSetting(FinnhubOptions.ApiKeySetting, "")
        .UseSetting(EdgarOptions.ContactEmailSetting, "");

    /// <summary>Both settings present, for a host booted outside Development, which refuses to start without them.</summary>
    public static IWebHostBuilder WithMarketDataSettings(this IWebHostBuilder builder) => builder
        .UseSetting(FinnhubOptions.ApiKeySetting, "not-a-real-key")
        .UseSetting(EdgarOptions.ContactEmailSetting, "tests@example.com");
}
