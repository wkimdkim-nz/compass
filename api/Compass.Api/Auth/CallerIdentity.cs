using System.Security.Claims;

namespace Compass.Api.Auth;

/// <summary>Reads the signed-in caller's identity off the principal the session cookie resolved to.</summary>
public static class CallerIdentity
{
    public static Guid UserId(this ClaimsPrincipal caller) =>
        Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>The caller's email. Identity puts UserName in the name claim, and UserName is always the email.</summary>
    public static string Email(this ClaimsPrincipal caller) =>
        caller.FindFirstValue(ClaimTypes.Name)!;
}
