using Compass.Api.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Compass.Api.Tests;

/// <summary>
/// The environment-gated fallback rule of ADR 0010, at its own seam: which port gets a stub, and
/// where a missing setting stops the process instead. The Development half is also proven through
/// HTTP in <see cref="StubPortFallbackTests"/>; the refusal can only be proven here, because an app
/// that will not start has no HTTP seam to ask.
/// </summary>
public class MarketDataProvidersTests
{
    private const string Key = "a-finnhub-key";
    private const string Contact = "someone@example.com";

    [Fact]
    public void With_both_settings_present_neither_port_is_a_stub_in_any_environment()
    {
        var providers = MarketDataProviders.Resolve(Settings(Key, Contact), In("Production"));

        Assert.False(providers.QuoteSourceIsStub);
        Assert.False(providers.FundamentalsSourceIsStub);
    }

    [Fact]
    public void In_Development_each_absent_setting_stubs_its_own_port_and_only_that_one()
    {
        var noKey = MarketDataProviders.Resolve(Settings(null, Contact), In("Development"));
        var noContact = MarketDataProviders.Resolve(Settings(Key, null), In("Development"));

        Assert.True(noKey.QuoteSourceIsStub);
        Assert.False(noKey.FundamentalsSourceIsStub);
        Assert.False(noContact.QuoteSourceIsStub);
        Assert.True(noContact.FundamentalsSourceIsStub);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_Development_a_missing_Finnhub_key_stops_startup_and_is_named(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => MarketDataProviders.Resolve(Settings(null, Contact), In(environment)));

        Assert.Contains("Finnhub:ApiKey", error.Message);
        Assert.Contains("Finnhub__ApiKey", error.Message);
        Assert.DoesNotContain("Edgar", error.Message);
    }

    [Fact]
    public void Outside_Development_a_missing_EDGAR_contact_stops_startup_and_is_named()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => MarketDataProviders.Resolve(Settings(Key, null), In("Production")));

        Assert.Contains("Edgar:ContactEmail", error.Message);
        Assert.Contains("Edgar__ContactEmail", error.Message);
        Assert.DoesNotContain("Finnhub", error.Message);
    }

    [Fact]
    public void Outside_Development_both_missing_settings_are_named_in_the_one_error()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => MarketDataProviders.Resolve(Settings(null, null), In("Production")));

        Assert.Contains("Finnhub:ApiKey", error.Message);
        Assert.Contains("Edgar:ContactEmail", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_setting_is_an_absent_one(string blank)
    {
        // An unset shell variable passed through docker-compose arrives as "", not as nothing.
        Assert.Throws<InvalidOperationException>(
            () => MarketDataProviders.Resolve(Settings(blank, blank), In("Production")));
        Assert.True(MarketDataProviders.Resolve(Settings(blank, Contact), In("Development")).QuoteSourceIsStub);
    }

    private static IConfiguration Settings(string? finnhubApiKey, string? edgarContactEmail) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Finnhub:ApiKey"] = finnhubApiKey,
                ["Edgar:ContactEmail"] = edgarContactEmail,
            })
            .Build();

    private static IHostEnvironment In(string environment) => new FakeEnvironment(environment);

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Compass.Api";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
