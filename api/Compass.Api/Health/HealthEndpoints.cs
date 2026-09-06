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
/// <para>
/// <b>Deviation, recorded deliberately.</b> ADR 0007's key-ring amendment and spec
/// <see href="https://github.com/wkimdkim-nz/compass/issues/28">#28</see> both write this path as
/// <c>/health</c>. This is that path, moved under the only prefix CloudFront routes — the same
/// requirement <see href="https://github.com/wkimdkim-nz/compass/issues/43">#43</see> states as
/// "<c>/api/*</c> health requests through the public site return 200". The load balancer's target
/// group and the deployed smoke test must therefore both name <c>/api/health</c>, not
/// <c>/health</c>; nothing answers on the shorter path.
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
