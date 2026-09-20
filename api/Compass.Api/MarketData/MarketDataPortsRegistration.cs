using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compass.Api.MarketData;

public static class MarketDataPortsRegistration
{
    /// <summary>
    /// Fills the two ports the way <see cref="MarketDataProviders"/> resolved them: the stub where
    /// the port's setting is absent, the real adapter otherwise. A test swaps either by
    /// registering its own after this.
    /// </summary>
    public static IServiceCollection AddMarketDataPorts(this IServiceCollection services, MarketDataProviders providers)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MarketDataCache>();

        if (providers.FundamentalsSourceIsStub)
        {
            services.AddSingleton<IFundamentalsSource, StubFundamentalsSource>();
        }
        else
        {
            services.AddTransient<IFundamentalsSource, EdgarFundamentalsSource>();
        }

        if (providers.QuoteSourceIsStub)
        {
            services.AddSingleton<IQuoteSource, StubQuoteSource>();
        }
        else
        {
            services.AddFinnhubQuoteSource();
        }

        return services;
    }
}
