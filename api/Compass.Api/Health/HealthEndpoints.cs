namespace Compass.Api.Health;

/// <summary>
/// The liveness path the load balancer polls.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is shallow on purpose and must stay that way.</b> It answers 200 without opening a
/// database connection, so a database fault presents as failing requests rather than as an endless
/// kill-and-replace loop — restarting a task never repairs a database. It would also make the
/// migration race unrecoverable: migrations run as a one-off task, and a service whose health
/// check depends on the schema cannot survive being started first.
/// </para>
/// <para>
/// It sits under <c>/api</c> with everything else because CloudFront path-routes only <c>/api/*</c>
/// to the origin. A health path outside that prefix would be unreachable from the public site, and
/// so unverifiable by the deployed smoke test.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/health", () => Results.Ok()).AllowAnonymous();

        return routes;
    }
}
