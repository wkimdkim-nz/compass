using System.Security.Claims;

namespace Compass.Api.Auth;

/// <summary>Reads the signed-in caller's identity off the principal the session cookie resolved to.</summary>
public static class CallerIdentity
{
    public static Guid UserId(this ClaimsPrincipal caller) =>
        Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
