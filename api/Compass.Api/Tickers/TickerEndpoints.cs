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
                TickerLookupResult.UnknownTicker => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    detail: "No company with that ticker files with the SEC. Check the symbol, or enter the figures yourself."),
                TickerLookupResult.Unsupported unsupported => Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    detail: unsupported.Reason),
                TickerLookupResult.FundamentalsUnavailable => Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    detail: "The company's figures could not be fetched just now. Try again shortly, or enter them yourself."),
                var other => throw new InvalidOperationException($"Unhandled lookup result {other.GetType().Name}."),
            });

        return routes;
    }
}
