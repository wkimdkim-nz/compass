using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Compass.Api.Tests;

/// <summary>
/// ADR 0010's fallback rule through the HTTP seam: in Development, with neither provider setting
/// present, the API still starts, serves, and says in its log that both ports are stubs. The other
/// half of the rule — refusing to start anywhere else — is in <see cref="MarketDataProvidersTests"/>.
/// </summary>
public class StubPortFallbackTests(StubPortFallbackTests.NoProviderSettingsFactory factory)
    : IClassFixture<StubPortFallbackTests.NoProviderSettingsFactory>
{
    /// <summary>
    /// Development with both settings pinned absent, and its log kept. No container: the health
    /// path never opens a connection, so it runs on whatever connection string Development has.
    /// </summary>
    public sealed class NoProviderSettingsFactory : WebApplicationFactory<Program>
    {
        public ConcurrentQueue<string> Log { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.WithoutMarketDataSettings();
            builder.ConfigureLogging(logging => logging.AddProvider(new QueueLoggerProvider(Log)));
        }
    }

    [Fact]
    public async Task With_both_settings_absent_in_Development_the_API_starts_and_logs_that_the_stub_ports_are_in_use()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.Log, line => line.Contains("stub quote source") && line.Contains("Finnhub:ApiKey"));
        Assert.Contains(factory.Log, line => line.Contains("stub fundamentals source") && line.Contains("Edgar:ContactEmail"));
    }

    private sealed class QueueLoggerProvider(ConcurrentQueue<string> log) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new QueueLogger(log);

        public void Dispose() { }

        private sealed class QueueLogger(ConcurrentQueue<string> log) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => log.Enqueue(formatter(state, exception));
        }
    }
}
