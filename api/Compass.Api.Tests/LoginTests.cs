using System.Net;
using System.Net.Http.Json;

namespace Compass.Api.Tests;

public class LoginTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Login_with_valid_credentials_answers_200_and_a_session_cookie()
    {
        var email = Http.UniqueEmail();
        await factory.CreateAnonymousClient().Register(email, Password);
        var client = factory.CreateBrowserLikeClient();

        var response = await client.Login(email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(Http.Json);
        Assert.Equal(email, me!.Email);

        var setCookie = response.SessionSetCookie();
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        var check = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", Http.Json);
        Assert.Equal(me, check);
    }

    [Fact]
    public async Task Login_failure_is_byte_identical_for_a_wrong_password_and_an_unknown_email()
    {
        var email = Http.UniqueEmail();
        await factory.CreateAnonymousClient().Register(email, Password);
        var client = factory.CreateAnonymousClient();

        var wrongPassword = await client.Login(email, "not the password");
        var unknownEmail = await client.Login(Http.UniqueEmail(), Password);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal("application/problem+json", wrongPassword.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownEmail.Content.ReadAsStringAsync());
        Assert.False(wrongPassword.Headers.Contains("Set-Cookie"));
        Assert.False(unknownEmail.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Repeated_login_failures_lock_the_account_so_even_the_right_password_is_refused()
    {
        var email = Http.UniqueEmail();
        await factory.CreateAnonymousClient().Register(email, Password);
        var client = factory.CreateAnonymousClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.Login(email, "not the password");
        }
        var lockedOut = await client.Login(email, Password);
        var wrongPassword = await client.Login(email, "not the password");

        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);
        // Lockout does not leak either: same body as any other failure.
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await lockedOut.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logout_ends_the_session_on_the_server()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.Register(Http.UniqueEmail(), Password);

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
}
