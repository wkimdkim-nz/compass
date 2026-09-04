using System.Security.Claims;
using Compass.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace Compass.Api.Auth;

/// <summary>What the API tells the SPA about the signed-in user.</summary>
public record MeResponse(Guid Id, string Email);

/// <summary>The JSON body of register and login. Properties are nullable so a missing field is validated, not thrown.</summary>
public record Credentials(string? Email, string? Password);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/api/auth");

        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);
        auth.MapPost("/logout", Logout).RequireAuthorization();

        // The session check the HttpOnly cookie makes necessary: the SPA cannot read the cookie,
        // so this is how it learns whether it is signed in, and as whom.
        auth.MapGet("/me", (ClaimsPrincipal caller) => Results.Ok(caller.ToMeResponse()))
            .RequireAuthorization();

        return routes;
    }

    private static async Task<IResult> Register(
        Credentials body,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn)
    {
        if (body.MissingFields() is { Count: > 0 } missing)
        {
            return Results.ValidationProblem(missing);
        }

        var user = new ApplicationUser
        {
            // Guid v7 everywhere: it is what EF Core generates, and time-ordered keys index well.
            Id = Guid.CreateVersion7(),
            UserName = body.Email,
            Email = body.Email,
        };

        var result = await users.CreateAsync(user, body.Password!);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(result.Errors.ToFieldErrors());
        }

        // Signed in the moment registration succeeds; the cookie is always persistent.
        await signIn.SignInAsync(user, isPersistent: true);
        return Results.Created("/api/auth/me", user.ToMeResponse());
    }

    private static async Task<IResult> Login(
        Credentials body,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn)
    {
        // Every failure — missing field, unknown email, wrong password, locked out — answers with
        // the same generic 401, so the response cannot be used to discover whether an account exists.
        if (body.MissingFields().Count > 0)
        {
            return InvalidCredentials();
        }

        var result = await signIn.PasswordSignInAsync(
            body.Email!, body.Password!, isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        // The account can vanish between the sign-in and this lookup. Answer the same generic 401
        // rather than dereferencing null and handing back a 500 with a session cookie attached.
        var user = await users.FindByEmailAsync(body.Email!);
        if (user is null)
        {
            return InvalidCredentials();
        }

        return Results.Ok(user.ToMeResponse());
    }

    private static IResult InvalidCredentials() =>
        Results.Problem(title: "Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);

    private static async Task<IResult> Logout(SignInManager<ApplicationUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> MissingFields(this Credentials body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.Email)) errors["email"] = ["Email is required."];
        if (string.IsNullOrWhiteSpace(body.Password)) errors["password"] = ["Password is required."];
        return errors;
    }

    /// <summary>
    /// Maps Identity's error codes onto the form fields the SPA shows. UserName is always the
    /// email, so Identity reports every email problem twice (once per property); the UserName
    /// copy is dropped so each problem surfaces under one field.
    /// </summary>
    private static Dictionary<string, string[]> ToFieldErrors(this IEnumerable<IdentityError> errors) =>
        errors
            .Select(e => (Field: FieldFor(e.Code), e.Description))
            .Where(e => e.Field is not null)
            .GroupBy(e => e.Field!)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

    /// <summary>Errors that belong to no single field (e.g. Identity's <c>DefaultError</c>) go under this key.</summary>
    private const string FormLevel = "general";

    private static string? FieldFor(string code) => code switch
    {
        _ when code.StartsWith("Password", StringComparison.Ordinal) => "password",
        "DuplicateEmail" or "InvalidEmail" => "email",
        "DuplicateUserName" or "InvalidUserName" => null,
        _ => FormLevel,
    };

    private static MeResponse ToMeResponse(this ApplicationUser user) => new(user.Id, user.Email!);

    private static MeResponse ToMeResponse(this ClaimsPrincipal caller) =>
        new(caller.UserId(), caller.Email());
}
