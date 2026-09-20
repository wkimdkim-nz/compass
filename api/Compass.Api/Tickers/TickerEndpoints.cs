namespace Compass.Api.Tickers;

/// <summary>
/// The ticker lookup, <c>GET /api/tickers/{symbol}</c>, with the four answers ADR 0010 gives it. A
/// failed quote is not among the failures: it is a 200 with a null price. No Problem body carries
/// anything a provider said.
/// </summary>
public static class TickerEndpoints
{
    public static IEndpointRouteBuilder MapTickerEndpoints(this IEndpointRouteBuilder routes)
    {
        var tickers = routes.MapGroup("/api/tickers").RequireAuthorization();

        tickers.MapGet("/{symbol}", async (string symbol, TickerLookup lookup, CancellationToken cancellationToken) =>
            await lookup.LookUpAsync(symbol, cancellationToken) switch
            {
                TickerLookupResult.Found found => Results.Ok(found.Response),
                TickerLookupResult.Failed failed => Results.Problem(
                    statusCode: failed switch
                    {
                        TickerLookupResult.UnknownTicker => StatusCodes.Status404NotFound,
                        TickerLookupResult.Unsupported => StatusCodes.Status422UnprocessableEntity,
                        TickerLookupResult.FundamentalsUnavailable => StatusCodes.Status503ServiceUnavailable,
                        var other => throw new InvalidOperationException($"Unhandled lookup failure {other.GetType().Name}."),
                    },
                    detail: failed.Reason),
                var other => throw new InvalidOperationException($"Unhandled lookup result {other.GetType().Name}."),
            });

        return routes;
    }
}
